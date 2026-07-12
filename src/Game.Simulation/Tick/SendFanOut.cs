namespace Game.Simulation.Tick;

/// <summary>
/// Pure helpers for the chunked parallel broadcast fan-out (<c>Replication:SendWorkers</c>) and
/// the always-on per-tick fairness rotation. No I/O and no <c>GameSession</c> dependency —
/// Gateway's TickBroadcaster applies these to its per-region session snapshot.
///
/// The three pieces compose as: resolve an effective worker count once at startup, then each
/// tick pick a rotation offset from the tick number, chunk the (conceptually rotated) session
/// list into that many contiguous pieces, and map each chunk-local position back to a real
/// index via <see cref="RotatedIndex"/> — no list copy needed to "rotate" it.
/// </summary>
public static class SendFanOut
{
    /// <summary>Cap for auto worker resolution (Replication:SendWorkers=0).</summary>
    public const int AutoWorkerCap = 8;

    /// <summary>
    /// Resolve the configured <c>Replication:SendWorkers</c> value to an effective worker count.
    /// 0 means auto: <c>min(AutoWorkerCap, processorCount)</c>. Any configured value &lt;= 0
    /// other than exactly 0 (i.e. a bad negative config) is clamped up to 1 so callers never see
    /// a non-positive worker count. The default configured value is 1, which resolves to 1 here
    /// — today's serial behavior, unchanged.
    /// </summary>
    public static int ResolveWorkerCount(int configured, int processorCount)
    {
        if (configured == 0)
            return Math.Clamp(Math.Min(AutoWorkerCap, processorCount), 1, AutoWorkerCap);
        return Math.Max(1, configured);
    }

    /// <summary>
    /// This tick's rotation offset: <c>tick % count</c>, normalized into <c>[0, count)</c>.
    /// Returns 0 for an empty (or non-positive) session count. Applied every tick regardless of
    /// SendWorkers — session order was never a guaranteed fairness contract, so this is always on.
    /// </summary>
    public static int RotateOffset(long tick, int count)
    {
        if (count <= 0) return 0;
        var offset = tick % count;
        return offset < 0 ? (int)(offset + count) : (int)offset;
    }

    /// <summary>
    /// Split <paramref name="count"/> items into <paramref name="workers"/> contiguous chunks,
    /// described as (Start, Length) in ROTATED index space — position 0 means "the item at the
    /// tick's rotation offset", not necessarily index 0 of the original list. Every item lands
    /// in exactly one chunk; chunk sizes differ by at most 1 (the first <c>count % workers</c>
    /// chunks get one extra item). If <paramref name="workers"/> exceeds
    /// <paramref name="count"/> (or count is 0), the trailing chunks are simply empty
    /// (Length 0) — callers can always fan out one task per chunk without special-casing.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Chunk(int count, int workers)
    {
        if (workers <= 0) throw new ArgumentOutOfRangeException(nameof(workers), "must be >= 1");
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "must be >= 0");

        var result = new (int Start, int Length)[workers];
        var baseSize = count / workers;
        var remainder = count % workers;
        var pos = 0;
        for (var i = 0; i < workers; i++)
        {
            var len = baseSize + (i < remainder ? 1 : 0);
            result[i] = (pos, len);
            pos += len;
        }
        return result;
    }

    /// <summary>
    /// Map a rotated-space position (as produced by <see cref="Chunk"/>, 0-based from the tick's
    /// rotation offset) back to the real index into the original (unrotated) session list —
    /// without ever materializing a rotated copy of that list.
    /// </summary>
    public static int RotatedIndex(int rotatedPosition, int offset, int count)
    {
        if (count <= 0) return rotatedPosition;
        var idx = (rotatedPosition + offset) % count;
        return idx < 0 ? idx + count : idx;
    }
}
