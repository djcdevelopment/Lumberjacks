using System.Collections.Concurrent;

namespace Game.Gateway.Valheim;

/// <summary>
/// Last-observed authoritative-consumer activity per window — the seat gate's liveness signal.
///
/// The Gateway is never told that a player left: there is no disconnect event, and
/// <c>ValheimHandshakeService</c>'s connected-uid set only ever grows. A seat reservation on a
/// fixed timer is therefore wrong in both directions. Too short and it frees while the holder is
/// still playing; too long and the sole volunteer is locked out of their own server for the whole
/// lease after a crash (plan §5.4), while a ghost accept — one vanilla's post-verdict ticket check
/// overturns, which the Gateway never hears about (§5.5) — holds the seat for exactly as long.
///
/// The consumer's own poll/ack traffic settles it. It is live, it is already window-keyed
/// (<c>/valheim/zdo-redirect/pending/{windowId}</c>, <c>/ack/{windowId}</c>), and the frozen 0.5.31
/// mod already sends it on its own cadence — so a holder who is really there refreshes the seat
/// continuously, and one who crashed or never landed stops instantly. No mod cut required, which is
/// what makes this buildable in stage 2 at all; §5.4's telemetry <c>peer_count</c> route was
/// rejected because that payload carries no window id, only a per-boot instance id.
///
/// Deliberately its own service rather than state inside <c>ValheimZdoRedirectService</c>: the ZDO
/// hot path stays untouched and the coupling is one call per endpoint, easy to remove if the seat
/// gate is later driven from somewhere better.
/// </summary>
public sealed class ValheimWindowActivityService
{
    readonly ConcurrentDictionary<string, DateTime> _lastUtc = new(StringComparer.Ordinal);

    /// <summary>Records a sign of life for <paramref name="windowId"/>. Monotonic: a late-arriving
    /// older timestamp never moves the mark backwards.</summary>
    public void Touch(string windowId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(windowId))
            return;
        _lastUtc.AddOrUpdate(windowId, utcNow, (_, prior) => utcNow > prior ? utcNow : prior);
    }

    public DateTime? LastActivityUtc(string windowId) =>
        !string.IsNullOrWhiteSpace(windowId) && _lastUtc.TryGetValue(windowId, out var utc)
            ? utc
            : null;

    /// <summary>Drops the mark so a window reset also drops its liveness, rather than leaving a
    /// stale sign of life that would hold a seat in a freshly reset window.</summary>
    public bool Clear(string windowId) => _lastUtc.TryRemove(windowId, out _);

    public int ClearAll()
    {
        var count = _lastUtc.Count;
        _lastUtc.Clear();
        return count;
    }
}
