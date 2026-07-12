using Microsoft.Extensions.Configuration;

namespace Game.Simulation.World;

/// <summary>
/// Selects how <see cref="InterestManager"/> filters entity updates per observer.
/// Phase 1 of the replication-policy experiment rig: these policies are for
/// A/B measurement against the existing off-host harness, not (yet) the
/// optimized send path — see docs on the 400-bot broadcast-phase knee.
/// </summary>
public enum ReplicationPolicy
{
    /// <summary>Existing near/mid/far behavior — the default, byte-for-byte unchanged.</summary>
    Tiered,

    /// <summary>No interest filtering — every observer gets every changed entity every tick.</summary>
    Full,

    /// <summary>Hard cutoff at NearRadius: inside → every tick, outside → dropped.</summary>
    Radius,
}

/// <summary>
/// Replication policy configuration, read once at startup from raw <see cref="IConfiguration"/>
/// (matching the <c>Udp:Port</c> / <c>Udp__Port</c> pattern used by <c>UdpTransport</c>).
///
/// Env vars: Replication__Policy, Replication__NearRadius, Replication__MidRadius,
/// Replication__MidTickInterval, Replication__SendWorkers, Replication__BroadcastDeadlineMs,
/// Replication__AdaptiveDegrade (send-loop rework, phase 2 — see TickBroadcaster),
/// Replication__UdpSockets (phase 3a — see UdpTransport).
/// </summary>
public sealed record ReplicationOptions
{
    public const double DefaultNearRadius = 100.0;
    public const double DefaultMidRadius = 300.0;
    public const int DefaultMidTickInterval = 4;
    public const int DefaultSendWorkers = 1;
    public const int DefaultBroadcastDeadlineMs = 0;
    public const bool DefaultAdaptiveDegrade = false;
    public const int DefaultUdpSockets = 1;

    public ReplicationPolicy Policy { get; init; } = ReplicationPolicy.Tiered;
    public double NearRadius { get; init; } = DefaultNearRadius;
    public double MidRadius { get; init; } = DefaultMidRadius;
    public int MidTickInterval { get; init; } = DefaultMidTickInterval;

    /// <summary>
    /// Broadcast send-phase parallelism. 1 (default) = today's serial <c>foreach</c>, exactly.
    /// 0 = auto (<see cref="Game.Simulation.Tick.SendFanOut.ResolveWorkerCount"/>). N>1 = split each region's
    /// session snapshot into N contiguous chunks and fan them out as concurrent tasks.
    /// </summary>
    public int SendWorkers { get; init; } = DefaultSendWorkers;

    /// <summary>
    /// Per-broadcast deadline in milliseconds. 0 (default) = off — no CancellationTokenSource,
    /// no behavior change. &gt;0 = sessions whose send hasn't completed by the deadline are
    /// aborted so the tick can end (see <see cref="Game.Simulation.Tick.BroadcastDeadline"/>).
    /// </summary>
    public int BroadcastDeadlineMs { get; init; } = DefaultBroadcastDeadlineMs;

    /// <summary>
    /// ADR-0011 "reduce frequency before dropping". False (default) = off. True = when the
    /// previous tick's broadcast wall time exceeded budget, this tick suppresses mid-band
    /// updates (tiered) or every-other-session (radius/full) — see <see cref="Game.Simulation.Tick.AdaptiveDegrade"/>.
    /// </summary>
    public bool AdaptiveDegrade { get; init; } = DefaultAdaptiveDegrade;

    /// <summary>
    /// Phase-3a experiment knob: how many UDP send sockets <see cref="Game.Gateway.WebSocket.UdpTransport"/>
    /// uses for outbound datagram-lane sends. 1 (default) = today's exact behavior — the single
    /// bound socket that receives also sends every reply. 0 = auto — resolve to the effective
    /// <see cref="SendWorkers"/> count (see <see cref="Game.Simulation.Tick.SendFanOut.ResolveUdpSocketCount"/>)
    /// so each worker chunk gets its own socket. N&gt;1 = exactly N sockets. Tests the hypothesis
    /// that a single shared <c>UdpClient</c> (synchronous <c>Send</c>, kernel-serialized) is why
    /// parallel send workers showed zero overlap (Follow-up E). Extra sockets bind to ephemeral
    /// ports — see UdpTransport for the NAT caveat that makes this experiment-only, not a
    /// production default.
    /// </summary>
    public int UdpSockets { get; init; } = DefaultUdpSockets;

    /// <summary>Lowercase policy name, for logging and metrics tagging.</summary>
    public string PolicyName => Policy switch
    {
        ReplicationPolicy.Full => "full",
        ReplicationPolicy.Radius => "radius",
        _ => "tiered",
    };

    public static ReplicationOptions FromConfiguration(IConfiguration config)
    {
        var policy = (config["Replication:Policy"] ?? "tiered").Trim().ToLowerInvariant() switch
        {
            "full" => ReplicationPolicy.Full,
            "radius" => ReplicationPolicy.Radius,
            "tiered" => ReplicationPolicy.Tiered,
            _ => ReplicationPolicy.Tiered, // unknown value — fall back to the safe default
        };

        return new ReplicationOptions
        {
            Policy = policy,
            NearRadius = config.GetValue("Replication:NearRadius", DefaultNearRadius),
            MidRadius = config.GetValue("Replication:MidRadius", DefaultMidRadius),
            MidTickInterval = config.GetValue("Replication:MidTickInterval", DefaultMidTickInterval),
            SendWorkers = config.GetValue("Replication:SendWorkers", DefaultSendWorkers),
            BroadcastDeadlineMs = config.GetValue("Replication:BroadcastDeadlineMs", DefaultBroadcastDeadlineMs),
            AdaptiveDegrade = config.GetValue("Replication:AdaptiveDegrade", DefaultAdaptiveDegrade),
            UdpSockets = config.GetValue("Replication:UdpSockets", DefaultUdpSockets),
        };
    }
}
