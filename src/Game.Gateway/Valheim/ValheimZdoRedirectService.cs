using System.Collections.Concurrent;

namespace Game.Gateway.Valheim;

/// <summary>
/// A single redirected ZDO envelope, as forwarded by the Harmony mod after it
/// suppressed the original Valheim send.
/// </summary>
public sealed record ValheimZdoRedirectEnvelope
{
    public long? Seq { get; init; }
    public long? UidUser { get; init; }
    public long? UidId { get; init; }
    public long? Owner { get; init; }
    public int? OwnerRev { get; init; }
    public int? DataRev { get; init; }
    public int? Prefab { get; init; }
    public double[]? Pos { get; init; }
    public string? BodyB64 { get; init; }
}

/// <summary>
/// POST body for /valheim/zdo-redirect/receipts. window_id and a non-null
/// envelopes list (each carrying seq) are required; everything else is
/// best-effort bookkeeping.
/// </summary>
public sealed record ValheimZdoRedirectRequest
{
    public string? Source { get; init; }
    public string? WindowId { get; init; }
    public List<ValheimZdoRedirectEnvelope>? Envelopes { get; init; }
}

public sealed record ValheimZdoRedirectRecordResult(int Received, long Total);
public sealed record ValheimZdoRedirectAckResult(int Acknowledged, int Unknown);

public sealed record ValheimZdoRedirectWindowStatus(
    string WindowId,
    long Receipts,
    long DistinctSeq,
    long Acknowledged,
    long Pending,
    long Duplicates,
    long? MinSeq,
    long? MaxSeq,
    long MissingSeq,
    bool SeqTrackingSaturated,
    long EmptyBodyCount,
    DateTime? FirstUtc,
    DateTime? LastUtc,
    IReadOnlyDictionary<string, long> PerPrefab,
    IReadOnlyDictionary<string, long> PerSource);

/// <summary>
/// Receipt counter for redirected Valheim ZDO payloads. The Harmony mod
/// suppresses a ZDO send and forwards it here instead; the gate-math test is
/// receipt count == suppressed-send count, with sequence-gap loss detection
/// (missing = max_seq - distinct_seq, assuming seq starts at 1 and is
/// monotonic per window). This service only counts and reports — the reader
/// does the gate math.
/// </summary>
public sealed class ValheimZdoRedirectService
{
    private readonly ConcurrentDictionary<string, ValheimZdoRedirectWindow> _windows =
        new(StringComparer.Ordinal);

    public ValheimZdoRedirectRecordResult RecordEnvelopes(
        string windowId,
        string source,
        IReadOnlyList<ValheimZdoRedirectEnvelope> envelopes)
    {
        var window = _windows.GetOrAdd(windowId, static _ => new ValheimZdoRedirectWindow());
        var total = window.RecordBatch(source, envelopes);
        return new ValheimZdoRedirectRecordResult(envelopes.Count, total);
    }

    public ValheimZdoRedirectWindowStatus GetStatus(string windowId) =>
        _windows.TryGetValue(windowId, out var window)
            ? window.ToStatus(windowId)
            : ValheimZdoRedirectWindow.Empty(windowId);

    public IReadOnlyList<ValheimZdoRedirectWindowStatus> GetAllStatuses() =>
        _windows
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value.ToStatus(kv.Key))
            .ToList();

    public IReadOnlyList<ValheimZdoRedirectEnvelope> Pending(string windowId, int limit)
    {
        return _windows.TryGetValue(windowId, out var window)
            ? window.Pending(Math.Clamp(limit, 1, 256))
            : Array.Empty<ValheimZdoRedirectEnvelope>();
    }

    public ValheimZdoRedirectAckResult Acknowledge(string windowId, IReadOnlyList<long> sequences)
    {
        return _windows.TryGetValue(windowId, out var window)
            ? window.Acknowledge(sequences)
            : new(0, sequences.Count);
    }

    /// <summary>Clears a single window. Returns whether it existed.</summary>
    public bool Reset(string windowId) => _windows.TryRemove(windowId, out _);

    /// <summary>Clears all windows. Returns how many were cleared.</summary>
    public int ResetAll()
    {
        var count = _windows.Count;
        _windows.Clear();
        return count;
    }

    private sealed class ValheimZdoRedirectWindow
    {
        // Bounded so a runaway/misbehaving sender can't grow this without limit;
        // past the cap we keep counting totals but flag seq tracking as saturated.
        private const int MaxTrackedSeq = 1_000_000;

        private readonly object _gate = new();
        private readonly HashSet<long> _distinctSeq = new();
        private readonly Dictionary<long, ValheimZdoRedirectEnvelope> _pending = new();
        private readonly Dictionary<int, long> _perPrefab = new();
        private readonly Dictionary<string, long> _perSource = new(StringComparer.Ordinal);

        private long _receipts;
        private long _duplicates;
        private long _acknowledged;
        private long? _minSeq;
        private long? _maxSeq;
        private long _emptyBodyCount;
        private bool _seqTrackingSaturated;
        private DateTime? _firstUtc;
        private DateTime? _lastUtc;

        public long RecordBatch(string source, IReadOnlyList<ValheimZdoRedirectEnvelope> envelopes)
        {
            lock (_gate)
            {
                var now = DateTime.UtcNow;

                foreach (var envelope in envelopes)
                {
                    var seq = envelope.Seq!.Value;

                    _receipts++;
                    _firstUtc ??= now;
                    _lastUtc = now;

                    if (_minSeq is null || seq < _minSeq)
                        _minSeq = seq;
                    if (_maxSeq is null || seq > _maxSeq)
                        _maxSeq = seq;

                    if (_distinctSeq.Contains(seq))
                    {
                        _duplicates++;
                    }

                    else if (_distinctSeq.Count < MaxTrackedSeq)
                    {
                        _distinctSeq.Add(seq);
                    }
                    else
                    {
                        // Cap reached: can no longer reliably tell new-vs-duplicate
                        // beyond this point. Totals below still keep counting.
                        _seqTrackingSaturated = true;
                    }

                    _pending.TryAdd(seq, envelope);

                    if (string.IsNullOrEmpty(envelope.BodyB64))
                        _emptyBodyCount++;

                    var prefabKey = envelope.Prefab.GetValueOrDefault(0);
                    _perPrefab[prefabKey] = _perPrefab.GetValueOrDefault(prefabKey) + 1;
                    _perSource[source] = _perSource.GetValueOrDefault(source) + 1;
                }

                return _receipts;
            }
        }

        public IReadOnlyList<ValheimZdoRedirectEnvelope> Pending(int limit)
        {
            lock (_gate)
            {
                return _pending.OrderBy(kv => kv.Key).Take(limit).Select(kv => kv.Value).ToList();
            }
        }

        public ValheimZdoRedirectAckResult Acknowledge(IReadOnlyList<long> sequences)
        {
            lock (_gate)
            {
                var acknowledged = 0;
                var unknown = 0;
                foreach (var sequence in sequences.Distinct())
                {
                    if (_pending.Remove(sequence))
                    {
                        acknowledged++;
                        _acknowledged++;
                    }
                    else unknown++;
                }
                return new(acknowledged, unknown);
            }
        }

        public ValheimZdoRedirectWindowStatus ToStatus(string windowId)
        {
            lock (_gate)
            {
                var distinctCount = (long)_distinctSeq.Count;
                var missing = _maxSeq is null ? 0 : Math.Max(0, _maxSeq.Value - distinctCount);

                return new ValheimZdoRedirectWindowStatus(
                    windowId,
                    _receipts,
                    distinctCount,
                    _acknowledged,
                    _pending.Count,
                    _duplicates,
                    _minSeq,
                    _maxSeq,
                    missing,
                    _seqTrackingSaturated,
                    _emptyBodyCount,
                    _firstUtc,
                    _lastUtc,
                    _perPrefab.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                    new Dictionary<string, long>(_perSource));
            }
        }

        public static ValheimZdoRedirectWindowStatus Empty(string windowId) =>
            new(
                windowId,
                Receipts: 0,
                DistinctSeq: 0,
                Acknowledged: 0,
                Pending: 0,
                Duplicates: 0,
                MinSeq: null,
                MaxSeq: null,
                MissingSeq: 0,
                SeqTrackingSaturated: false,
                EmptyBodyCount: 0,
                FirstUtc: null,
                LastUtc: null,
                PerPrefab: new Dictionary<string, long>(),
                PerSource: new Dictionary<string, long>());
    }
}
