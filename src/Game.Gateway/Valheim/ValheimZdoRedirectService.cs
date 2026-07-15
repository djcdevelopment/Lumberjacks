using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

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
/// Durable queue for redirected Valheim ZDO payloads. The Harmony mod
/// suppresses a ZDO send and forwards it here instead. When a WAL path is
/// configured, records and acknowledgements are flushed before the in-memory
/// state changes so an interrupted Gateway can resume the same window.
/// </summary>
public sealed class ValheimZdoRedirectService
{
    private const int MaxWalEntryBytes = 64 * 1024 * 1024;
    private readonly ConcurrentDictionary<string, ValheimZdoRedirectWindow> _windows =
        new(StringComparer.Ordinal);
    private readonly object _persistenceGate = new();
    private readonly string? _walPath;

    public bool PersistenceEnabled => _walPath is not null;
    public bool PersistenceHealthy { get; private set; } = true;
    public long WalBytes => _walPath is null || !File.Exists(_walPath) ? 0 : new FileInfo(_walPath).Length;

    public ValheimZdoRedirectService()
        : this(Environment.GetEnvironmentVariable("VALHEIM_ZDO_QUEUE_PATH"))
    {
    }

    public ValheimZdoRedirectService(string? walPath)
    {
        if (string.IsNullOrWhiteSpace(walPath)) return;
        _walPath = Path.GetFullPath(walPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_walPath)!);
        ReplayWal();
    }

    public ValheimZdoRedirectRecordResult RecordEnvelopes(
        string windowId,
        string source,
        IReadOnlyList<ValheimZdoRedirectEnvelope> envelopes)
    {
        lock (_persistenceGate)
        {
            var observedUtc = DateTime.UtcNow;
            AppendWal(new() { Op = "record", WindowId = windowId, Source = source,
                Envelopes = envelopes.ToList(), ObservedUtc = observedUtc });
            var window = _windows.GetOrAdd(windowId, static _ => new ValheimZdoRedirectWindow());
            var total = window.RecordBatch(source, envelopes, observedUtc);
            return new ValheimZdoRedirectRecordResult(envelopes.Count, total);
        }
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
        lock (_persistenceGate)
        {
            AppendWal(new() { Op = "ack", WindowId = windowId, Sequences = sequences.ToArray() });
            return _windows.TryGetValue(windowId, out var window)
                ? window.Acknowledge(sequences)
                : new(0, sequences.Count);
        }
    }

    /// <summary>Clears a single window. Returns whether it existed.</summary>
    public bool Reset(string windowId)
    {
        lock (_persistenceGate)
        {
            AppendWal(new() { Op = "reset", WindowId = windowId });
            return _windows.TryRemove(windowId, out _);
        }
    }

    /// <summary>Clears all windows. Returns how many were cleared.</summary>
    public int ResetAll()
    {
        lock (_persistenceGate)
        {
            AppendWal(new() { Op = "reset_all" });
            var count = _windows.Count;
            _windows.Clear();
            return count;
        }
    }

    private void AppendWal(RedirectWalEntry entry)
    {
        if (_walPath is null) return;
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(entry);
            if (payload.Length > MaxWalEntryBytes)
                throw new InvalidOperationException($"ZDO WAL entry is too large ({payload.Length} bytes)");
            using var stream = new FileStream(_walPath, FileMode.Append, FileAccess.Write, FileShare.Read,
                bufferSize: 64 * 1024, FileOptions.WriteThrough);
            Span<byte> length = stackalloc byte[4];
            BitConverter.TryWriteBytes(length, payload.Length);
            stream.Write(length);
            stream.Write(payload);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            PersistenceHealthy = false;
            throw;
        }
    }

    private void ReplayWal()
    {
        if (_walPath is null || !File.Exists(_walPath)) return;
        try
        {
            using var stream = new FileStream(_walPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            while (stream.Position < stream.Length)
            {
                var entryStart = stream.Position;
                if (stream.Length - stream.Position < sizeof(int))
                {
                    stream.SetLength(entryStart);
                    break;
                }
                var length = reader.ReadInt32();
                if (length <= 0 || length > MaxWalEntryBytes)
                    throw new InvalidDataException($"Invalid ZDO WAL entry length {length} at {entryStart}");
                if (stream.Length - stream.Position < length)
                {
                    stream.SetLength(entryStart);
                    break;
                }
                var payload = reader.ReadBytes(length);
                var entry = JsonSerializer.Deserialize<RedirectWalEntry>(payload)
                    ?? throw new InvalidDataException($"Empty ZDO WAL entry at {entryStart}");
                ApplyWalEntry(entry);
            }
        }
        catch
        {
            PersistenceHealthy = false;
            throw;
        }
    }

    private void ApplyWalEntry(RedirectWalEntry entry)
    {
        switch (entry.Op)
        {
            case "record" when !string.IsNullOrWhiteSpace(entry.WindowId) && entry.Envelopes is not null:
                _windows.GetOrAdd(entry.WindowId, static _ => new ValheimZdoRedirectWindow())
                    .RecordBatch(entry.Source ?? "unknown", entry.Envelopes, entry.ObservedUtc ?? DateTime.UtcNow);
                break;
            case "ack" when !string.IsNullOrWhiteSpace(entry.WindowId) && entry.Sequences is not null:
                if (_windows.TryGetValue(entry.WindowId, out var window)) window.Acknowledge(entry.Sequences);
                break;
            case "reset" when !string.IsNullOrWhiteSpace(entry.WindowId):
                _windows.TryRemove(entry.WindowId, out _);
                break;
            case "reset_all":
                _windows.Clear();
                break;
            default:
                throw new InvalidDataException($"Invalid ZDO WAL operation '{entry.Op}'");
        }
    }

    private sealed record RedirectWalEntry
    {
        public string? Op { get; init; }
        public string? WindowId { get; init; }
        public string? Source { get; init; }
        public List<ValheimZdoRedirectEnvelope>? Envelopes { get; init; }
        public long[]? Sequences { get; init; }
        public DateTime? ObservedUtc { get; init; }
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

        public long RecordBatch(string source, IReadOnlyList<ValheimZdoRedirectEnvelope> envelopes,
            DateTime? observedUtc = null)
        {
            lock (_gate)
            {
                var now = observedUtc ?? DateTime.UtcNow;

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
