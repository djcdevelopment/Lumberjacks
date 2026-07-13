using Game.ServiceDefaults;
using Game.Simulation.Tick;
using Game.Simulation.World;
using Microsoft.Extensions.Configuration;

namespace Game.Simulation.Endpoints;

/// <summary>
/// Public Telemetry API v0 (community-telemetry-strategy.md Phase 3, G1) — the four
/// world/tick-facing endpoints that only need <see cref="WorldState"/> / <see cref="TickMetrics"/>
/// / configuration (no Gateway-only session state). The sessions aggregate endpoint lives
/// alongside SessionManager in Game.Gateway.Endpoints instead — see TelemetryV0SessionsEndpoints.
///
/// Read-only, versioned, explicitly unstable (see <see cref="PublicTelemetryV0"/>). Every
/// response is built by a plain static method taking only its dependencies as parameters (no
/// HttpContext) so it's directly unit-testable without spinning up a host — see
/// tests/Game.Simulation.Tests/TelemetryV0EndpointsTests.cs, including the privacy test that
/// serializes every v0 response and asserts no player id ever appears.
///
/// Hard privacy rule: no player ids, names, or positions in ANY v0 response, anywhere. These
/// four endpoints only ever touch aggregate/static world facts (tick counters, timing
/// percentiles, replication counters, region bounds) — never Player/session records.
/// </summary>
public static class TelemetryV0Endpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapTelemetryGroup();

        group.MapGet("/server", (WorldState world, IConfiguration config) =>
            Results.Ok(BuildServerInfo(world, config)));

        group.MapGet("/tick", (HttpContext http) =>
        {
            var metrics = http.RequestServices.GetService<TickMetrics>();
            return Results.Ok(BuildTickInfo(metrics));
        });

        group.MapGet("/delivery", () => Results.Ok(BuildDeliveryInfo()));

        group.MapGet("/regions", (WorldState world) => Results.Ok(BuildRegionsInfo(world)));
    }

    /// <summary>
    /// api_version/stability envelope + current_tick/tick_rate_hz/uptime_seconds/started_at +
    /// the active replication policy and its configured knobs. send_workers is the EFFECTIVE
    /// (post-auto-resolve) count — see <see cref="SendFanOut.ResolveWorkerCount"/> — computed
    /// the same pure way TickBroadcaster does at startup, so this is available immediately
    /// (doesn't wait on the first ~5s TickMetrics window).
    /// </summary>
    public static object BuildServerInfo(WorldState world, IConfiguration config)
    {
        var uptime = DateTimeOffset.UtcNow - world.StartedAt;
        var options = ReplicationOptions.FromConfiguration(config);
        var effectiveSendWorkers = SendFanOut.ResolveWorkerCount(options.SendWorkers, Environment.ProcessorCount);

        return new
        {
            api_version = PublicTelemetryV0.ApiVersion,
            stability = PublicTelemetryV0.Stability,
            current_tick = world.CurrentTick,
            tick_rate_hz = 20,
            uptime_seconds = (int)uptime.TotalSeconds,
            started_at = world.StartedAt,
            replication = new
            {
                policy = options.PolicyName,
                near_radius = options.NearRadius,
                mid_radius = options.MidRadius,
                mid_tick_interval = options.MidTickInterval,
                send_workers = effectiveSendWorkers,
                deadline_ms = options.BroadcastDeadlineMs,
                adaptive = options.AdaptiveDegrade,
            },
        };
    }

    /// <summary>
    /// The TickMetrics LastWindow snapshot (same shape as /tick's tick_timing — phases +
    /// replication counters) wrapped in the v0 envelope. Null (graceful, not an error) until
    /// the first ~5s window closes, exactly like /tick.
    /// </summary>
    public static object BuildTickInfo(TickMetrics? metrics) => new
    {
        api_version = PublicTelemetryV0.ApiVersion,
        stability = PublicTelemetryV0.Stability,
        tick_timing = metrics?.LastWindow,
    };

    /// <summary>Delivery-path and session-transition counters — both are aggregate tallies only.</summary>
    public static object BuildDeliveryInfo() => new
    {
        api_version = PublicTelemetryV0.ApiVersion,
        stability = PublicTelemetryV0.Stability,
        delivery = LumberjacksTelemetry.SnapshotDelivery(),
        transitions = LumberjacksTelemetry.SnapshotTransitions(),
    };

    /// <summary>Per-region static world facts — id, name, live player_count, bounds, tick_rate.</summary>
    public static object BuildRegionsInfo(WorldState world) => new
    {
        api_version = PublicTelemetryV0.ApiVersion,
        stability = PublicTelemetryV0.Stability,
        regions = world.Regions.Values.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            player_count = r.PlayerCount,
            bounds = new
            {
                min = new { x = r.BoundsMin.X, y = r.BoundsMin.Y, z = r.BoundsMin.Z },
                max = new { x = r.BoundsMax.X, y = r.BoundsMax.Y, z = r.BoundsMax.Z },
            },
            tick_rate = r.TickRate,
        }).ToList(),
    };
}
