using System.Net;
using System.Text;

namespace Game.Gateway.Valheim;

public static class SteamEnrollmentEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/v0/enrollment/invites", (HttpRequest request, SteamEnrollmentService service) =>
        {
            var admin = Environment.GetEnvironmentVariable("LUMBERJACKS_ADMIN_KEY");
            if (!string.IsNullOrWhiteSpace(admin) && request.Headers["X-Lumberjacks-Admin-Key"] != admin) return Results.Unauthorized();
            var invite = service.CreateInvite(TimeSpan.FromHours(24));
            var baseUrl = Environment.GetEnvironmentVariable("LUMBERJACKS_ENROLLMENT_PUBLIC_URL") ?? "http://127.0.0.1:4006";
            return Results.Ok(new { invite_url = $"{baseUrl.TrimEnd('/')}/join?t={invite.Token}", expires_utc = invite.ExpiresUtc });
        });

        app.MapGet("/join", (HttpRequest request) =>
        {
            var token = request.Query["t"].ToString();
            var baseUrl = Environment.GetEnvironmentVariable("LUMBERJACKS_ENROLLMENT_PUBLIC_URL") ?? $"{request.Scheme}://{request.Host}";
            var callback = $"{baseUrl.TrimEnd('/')}/join/steam-callback?t={Uri.EscapeDataString(token)}";
            var login = "https://steamcommunity.com/openid/login?openid.ns=http%3A%2F%2Fspecs.openid.net%2Fauth%2F2.0&openid.mode=checkid_setup&openid.return_to=" + Uri.EscapeDataString(callback) + "&openid.realm=" + Uri.EscapeDataString(baseUrl) + "&openid.identity=http%3A%2F%2Fspecs.openid.net%2Fauth%2F2.0%2Fidentifier_select&openid.claimed_id=http%3A%2F%2Fspecs.openid.net%2Fauth%2F2.0%2Fidentifier_select";
            return Results.Text($"<html><body><h1>Lumberjacks invite</h1><p>Use Steam to redeem this one-time invitation.</p><a href=\"{WebUtility.HtmlEncode(login)}\">Sign in with Steam</a></body></html>", "text/html");
        });

        app.MapGet("/join/steam-callback", async (HttpRequest request, SteamEnrollmentService service, IHttpClientFactory clients) =>
        {
            var token = request.Query["t"].ToString();
            var values = request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
            if (!string.Equals(values.GetValueOrDefault("openid.mode"), "id_res", StringComparison.Ordinal) ||
                !values.TryGetValue("openid.claimed_id", out var claimed) ||
                !claimed.StartsWith("https://steamcommunity.com/openid/id/", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Steam authentication was not completed" });

            using var form = new FormUrlEncodedContent(values.Concat(new[] { new KeyValuePair<string, string>("openid.mode", "check_authentication") }));
            using var response = await clients.CreateClient().PostAsync("https://steamcommunity.com/openid/login", form);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode || !body.Contains("is_valid:true", StringComparison.OrdinalIgnoreCase)) return Results.Unauthorized();

            var steamId = claimed[(claimed.LastIndexOf('/') + 1)..];
            if (!service.Redeem(token, steamId, out var enrollment)) return Results.BadRequest(new { error = "invite invalid, expired, or already used" });
            var gateway = Environment.GetEnvironmentVariable("LUMBERJACKS_PLAYER_GATEWAY_URL") ?? baseUrlFor(request);
            return Results.Text(BuildConfig(enrollment, gateway), "text/plain");
        });

    }

    static string baseUrlFor(HttpRequest request) => $"{request.Scheme}://{request.Host}";

    static object BuildConfigObject(SteamEnrollmentService.Enrollment enrollment, string gateway) => new
    {
        lumberjacksGatewayUrl = gateway,
        lumberjacksAuthoritativeWindowId = enrollment.QueueWindowId,
        lumberjacksEnrollmentId = enrollment.ManifestId,
        lumberjacksClientAccessKey = enrollment.AccessToken,
    };

    static string BuildConfig(SteamEnrollmentService.Enrollment enrollment, string gateway) =>
        $"Lumberjacks enrollment complete. SteamID={enrollment.SteamId}\n\n" +
        "[Lumberjacks]\n" +
        $"lumberjacksGatewayUrl = {gateway}\n" +
        $"lumberjacksAuthoritativeWindowId = {enrollment.QueueWindowId}\n" +
        $"lumberjacksEnrollmentId = {enrollment.ManifestId}\n" +
        $"lumberjacksClientAccessKey = {enrollment.AccessToken}\n";

}
