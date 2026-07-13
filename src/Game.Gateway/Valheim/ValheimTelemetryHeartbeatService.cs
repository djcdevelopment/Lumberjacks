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
                    cutover_mode = _latest.CutoverMode,
                    enrollment_manifest_id = _latest.EnrollmentManifestId,
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
                    coverage_total = _latest.CoverageTotal,
                    coverage_lumberjacks = _latest.CoverageLumberjacks,
                    coverage_native_only = _latest.CoverageNativeOnly,
                    native_fallbacks = _latest.NativeFallbacks,
                    sample_timestamp_utc = _latest.TimestampUtc,
                },
            };
        }
    }

    public object CutoverSnapshot()
    {
        lock (_gate)
        {
            var stale = _lastSeen is null || DateTimeOffset.UtcNow - _lastSeen > TimeSpan.FromSeconds(15);
            var mode = _latest?.CutoverMode;
            var coverageTotal = _latest?.CoverageTotal;
            var coverageLumberjacks = _latest?.CoverageLumberjacks;
            var nativeOnly = _latest?.CoverageNativeOnly;
            var coveragePercent = coverageTotal is > 0 && coverageLumberjacks.HasValue
                ? Math.Round(100d * coverageLumberjacks.Value / coverageTotal.Value, 2)
                : (double?)null;

            return new
            {
                state = stale ? "stale" : (mode ?? "unknown"),
                stale,
                mode,
                enrollment_manifest_id = _latest?.EnrollmentManifestId,
                coverage_total = coverageTotal,
                coverage_lumberjacks = coverageLumberjacks,
                coverage_native_only = nativeOnly,
                coverage_percent = coveragePercent,
                native_fallbacks = _latest?.NativeFallbacks,
                last_seen = _lastSeen,
                mod_version = _latest?.ModVersion,
                instance_id = _latest?.InstanceId,
            };
        }
    }

    public object EnrollmentSnapshot(string manifestId)
    {
        lock (_gate)
        {
            var stale = _lastSeen is null || DateTimeOffset.UtcNow - _lastSeen > TimeSpan.FromSeconds(15);
            var mode = _latest?.CutoverMode ?? "native";
            return new
            {
                manifest_id = manifestId,
                state = stale ? "stale" : "advertised",
                mode,
                server_instance_id = _latest?.InstanceId,
                mod_version = _latest?.ModVersion,
                required_transport = "lumberjacks-progressive",
                native_fallback_allowed = true,
                coverage_gate = new
                {
                    total = _latest?.CoverageTotal,
                    lumberjacks = _latest?.CoverageLumberjacks,
                    native_only = _latest?.CoverageNativeOnly,
                    complete = _latest?.CoverageTotal is > 0 && _latest.CoverageNativeOnly == 0,
                },
                last_seen = _lastSeen,
            };
        }
    }
}

public sealed record ValheimTelemetryHeartbeat
{
    public string? CutoverMode { get; init; }
    public string? EnrollmentManifestId { get; init; }
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
    public long? CoverageTotal { get; init; }
    public long? CoverageLumberjacks { get; init; }
    public long? CoverageNativeOnly { get; init; }
    public long? NativeFallbacks { get; init; }
}
