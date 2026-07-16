using Game.ServiceDefaults;

namespace Game.Gateway.Endpoints;

/// <summary>
/// Serves the generated, self-contained Valheim volunteer roadmap. The source of
/// truth is docs/roadmap; scripts/roadmap.mjs renders this published asset and checks
/// its append-only commit journal. Like the other community surfaces, the document is
/// read once at startup and a missing optional asset degrades to a small explanation
/// instead of preventing the Gateway from starting.
/// </summary>
public static class RoadmapViewEndpoints
{
    private const string RelativePath = "Community/roadmap.html";

    private const string FallbackHtml =
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Lumberjacks — Valheim roadmap</title></head>" +
        "<body style=\"background:#0b1013;color:#edf5f2;font-family:system-ui,sans-serif;padding:2rem\">" +
        "<h1>Valheim roadmap unavailable</h1>" +
        "<p>The generated roadmap asset was not included in this Gateway build. " +
        "Run <code>npm run roadmap:render</code>, publish again, or read " +
        "<code>docs/network/valheim-volunteer-platform-plan.md</code>.</p>" +
        "</body></html>";

    public static void Map(WebApplication app)
    {
        var html = LoadHtml(app.Environment.ContentRootPath, app.Logger);

        app.MapGet("/roadmap", () => Results.Text(html, "text/html; charset=utf-8"))
            .RequireCors(PublicTelemetryV0.CorsPolicyName);
    }

    internal static string LoadHtml(string contentRoot, ILogger logger)
    {
        var path = Path.Combine(contentRoot, RelativePath);
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex,
                "Could not load {Path} for GET /roadmap — serving a minimal fallback page instead.",
                path);
            return FallbackHtml;
        }
    }
}
