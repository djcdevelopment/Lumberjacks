using Game.ServiceDefaults;

namespace Game.Gateway.Valheim;

public static class ValheimTelemetryHeartbeatEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/valheim/telemetry");

        group.MapPost("/heartbeat", (HttpRequest request,
            ValheimTelemetryHeartbeat heartbeat,
            ValheimTelemetryHeartbeatService service) =>
        {
            var expected = Environment.GetEnvironmentVariable("VALHEIM_TELEMETRY_KEY");
            if (!string.IsNullOrWhiteSpace(expected) &&
                request.Headers["X-Lumberjacks-Telemetry-Key"] != expected)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(heartbeat.InstanceId) ||
                string.IsNullOrWhiteSpace(heartbeat.ModVersion) ||
                string.IsNullOrWhiteSpace(heartbeat.TimestampUtc))
            {
                return Results.BadRequest(new { error = "instance_id, mod_version, and timestamp_utc are required" });
            }

            if (!string.IsNullOrWhiteSpace(heartbeat.CutoverMode) &&
                heartbeat.CutoverMode is not ("native" or "mirrored" or "lumberjacks-primary"))
            {
                return Results.BadRequest(new
                {
                    error = "cutover_mode must be native, mirrored, or lumberjacks-primary",
                });
            }

            service.Record(heartbeat);
            return Results.Ok(new { ok = true, received_at = DateTimeOffset.UtcNow });
        });

        app.MapGet("/api/v0/telemetry/valheim", (ValheimTelemetryHeartbeatService service) =>
            Results.Ok(service.Snapshot()))
            .RequireCors(PublicTelemetryV0.CorsPolicyName);

        app.MapGet("/api/v0/telemetry/cutover", (ValheimTelemetryHeartbeatService service) =>
            Results.Ok(service.CutoverSnapshot()))
            .RequireCors(PublicTelemetryV0.CorsPolicyName);
    }
}
