namespace Game.Simulation.Tick;

/// <summary>
/// Pure decision logic for <c>Replication:AdaptiveDegrade</c> — ADR-0011's "reduce frequency
/// before dropping". TickBroadcaster tracks its own previous broadcast wall-clock time (the
/// budget truth — see the comment on <c>RecordBroadcastPhases</c> usage in TickBroadcaster) and
/// asks this class whether the CURRENT tick should degrade. The rule is stateless beyond that
/// one number, so degrade lifts the instant a broadcast fits inside budget again — no cooldown,
/// no hysteresis.
/// </summary>
public static class AdaptiveDegrade
{
    /// <summary>
    /// True when adaptive degrade is enabled AND the previous tick's broadcast wall time
    /// exceeded the tick budget. Exactly-at-budget is not an overrun.
    /// </summary>
    public static bool ShouldDegrade(bool enabled, double previousBroadcastWallMs, double budgetMs = TickMetrics.TickBudgetMs)
        => enabled && previousBroadcastWallMs > budgetMs;

    /// <summary>
    /// Alternating half-selection used for radius/full policies (which have no mid-band to
    /// suppress): true means "skip this session's update this tick". Operates on the session's
    /// position within the ROTATED order (see <see cref="SendFanOut.RotateOffset"/>), so which
    /// physical sessions get skipped shifts tick to tick along with the fairness rotation —
    /// no single session is skipped every degraded tick.
    /// </summary>
    public static bool ShouldSkipAlternating(int rotatedPosition) => (rotatedPosition & 1) == 1;
}
