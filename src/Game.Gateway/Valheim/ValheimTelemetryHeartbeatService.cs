namespace Game.Gateway.Valheim;

/// <summary>Bounded latest-value store for the mod's sanitized runtime heartbeat.</summary>
public sealed class ValheimTelemetryHeartbeatService
{
    private readonly object _gate = new();
    private ValheimTelemetryHeartbeat? _latest;
    private DateTimeOffset? _lastSeen;

    public void Record(ValheimTelemetryHeartbeat heartbeat)
    {
        lock (_gate)
        {
            _latest = heartbeat;
            _lastSeen = DateTimeOffset.UtcNow;
        }
    }

    public object Snapshot()
    {
        lock (_gate)
        {
            var stale = _lastSeen is null || DateTimeOffset.UtcNow - _lastSeen > TimeSpan.FromSeconds(15);
            return new
            {
                stability = "unstable",
                stale,
                last_seen = _lastSeen,
                heartbeat = _latest is null ? null : new
                {
                    mod_version = _latest.ModVersion,
                    instance_id = _latest.InstanceId,
                    server_role = _latest.ServerRole,
                    server_state = _latest.ServerState,
                    peer_count = _latest.PeerCount,
                    handshake_accepted = _latest.HandshakeAccepted,
                    handshake_rejected = _latest.HandshakeRejected,
                    redirect_suppressed = _latest.RedirectSuppressed,
                    redirect_received = _latest.RedirectReceived,
                    redirect_missing = _latest.RedirectMissing,
                    redirect_duplicates = _latest.RedirectDuplicates,
                    injection_applied = _latest.InjectionApplied,
                    injection_rendered = _latest.InjectionRendered,
                    injection_rejected = _latest.InjectionRejected,
                    sample_timestamp_utc = _latest.TimestampUtc,
                },
            };
        }
    }
}

public sealed record ValheimTelemetryHeartbeat
{
    public string? InstanceId { get; init; }
    public string? ModVersion { get; init; }
    public string? TimestampUtc { get; init; }
    public string? ServerRole { get; init; }
    public string? ServerState { get; init; }
    public int? PeerCount { get; init; }
    public long? HandshakeAccepted { get; init; }
    public long? HandshakeRejected { get; init; }
    public long? RedirectSuppressed { get; init; }
    public long? RedirectReceived { get; init; }
    public long? RedirectMissing { get; init; }
    public long? RedirectDuplicates { get; init; }
    public long? InjectionApplied { get; init; }
    public long? InjectionRendered { get; init; }
    public long? InjectionRejected { get; init; }
}
