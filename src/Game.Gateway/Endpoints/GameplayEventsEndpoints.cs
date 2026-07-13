using Game.ServiceDefaults;

namespace Game.Gateway.Endpoints;

/// <summary>
/// Gameplay Event Telemetry (community-telemetry-strategy.md Phase 4/G4 UI first pass) — a
/// single self-contained HTML page (inline CSS+JS, zero external deps) served verbatim from
/// Community/events.html (drafted by Gemini Pro via HEARTH, reviewed and fixed up here — see
/// docs/ui/g3-g4-g5-first-pass.md).
///
/// HONESTY RULE: the gameplay-event feed backend does not exist yet (quest trigger events —
/// first hit, killing blow, weapon usage, projectile, trigger — are not yet emitted to any
/// queryable endpoint). The page attempts a real fetch to the documented future endpoint
/// (<c>GET /api/v0/telemetry/events</c>, not implemented) and always falls back to a
/// hardcoded, clearly-labeled SAMPLE dataset behind a persistent "Sample data — backend
/// pending" banner — never presented as live. Each timeline entry carries a provenance badge
/// reusing the exact four-tier vocabulary/colors already established by the achievements
/// model (<see cref="Game.Contracts.Achievements.ProvenanceTier"/>) so the whole telemetry
/// surface shares one provenance mental model instead of inventing a second one.
///
/// Sibling of <see cref="CommunityViewEndpoints"/> — same serving pattern (read once at
/// startup, cached in memory, graceful fallback if the file is missing).
/// </summary>
public static class GameplayEventsEndpoints
{
    private const string RelativePath = "Community/events.html";

    private const string FallbackHtml =
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Lumberjacks — Gameplay Event Telemetry</title></head>" +
        "<body style=\"background:#0f1115;color:#e2e8f0;font-family:system-ui,sans-serif;padding:2rem\">" +
        "<h1>Gameplay Event Telemetry unavailable</h1>" +
        "<p>The events.html asset failed to load on the server. This page is a first-pass mockup — " +
        "the gameplay-event feed backend does not exist yet.</p>" +
        "</body></html>";

    public static void Map(WebApplication app)
    {
        var html = LoadHtml(app.Environment.ContentRootPath, app.Logger);

        app.MapGet("/events", () => Results.Text(html, "text/html"))
            .RequireCors(PublicTelemetryV0.CorsPolicyName);
    }

    private static string LoadHtml(string contentRoot, ILogger logger)
    {
        var path = Path.Combine(contentRoot, RelativePath);
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex,
                "Could not load {Path} for GET /events — serving a minimal fallback page instead.", path);
            return FallbackHtml;
        }
    }
}
