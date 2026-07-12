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
/// Env vars: Replication__Policy, Replication__NearRadius, Replication__MidRadius, Replication__MidTickInterval
/// </summary>
public sealed record ReplicationOptions
{
    public const double DefaultNearRadius = 100.0;
    public const double DefaultMidRadius = 300.0;
    public const int DefaultMidTickInterval = 4;

    public ReplicationPolicy Policy { get; init; } = ReplicationPolicy.Tiered;
    public double NearRadius { get; init; } = DefaultNearRadius;
    public double MidRadius { get; init; } = DefaultMidRadius;
    public int MidTickInterval { get; init; } = DefaultMidTickInterval;

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
        };
    }
}
