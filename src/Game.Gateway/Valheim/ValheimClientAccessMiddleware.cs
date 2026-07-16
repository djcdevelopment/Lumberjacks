namespace Game.Gateway.Valheim;

/// <summary>
/// Optional shared-client gate for the Valheim control plane.  It is disabled when
/// no key is configured so the private OMEN tunnel remains backwards compatible.
/// Enable it on a volunteer endpoint with LUMBERJACKS_CLIENT_ACCESS_KEY.
/// </summary>
public sealed class ValheimClientAccessMiddleware
{
    private readonly RequestDelegate _next;

    public ValheimClientAccessMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, SteamEnrollmentService enrollments)
    {
        if (RequiresClientKey(context) && !IsPrivateOrLoopback(context.Connection.RemoteIpAddress))
        {
            var expected = Environment.GetEnvironmentVariable("LUMBERJACKS_CLIENT_ACCESS_KEY");
            var supplied = context.Request.Headers["X-Lumberjacks-Client-Key"].ToString();
            var enrollmentId = context.Request.Headers["X-Lumberjacks-Enrollment-Id"].ToString();
            var sharedKeyValid = !string.IsNullOrWhiteSpace(expected) && CryptographicEquals(supplied, expected);
            var enrollmentValid = enrollments.IsCredentialValid(enrollmentId, supplied);
            if (!sharedKeyValid && !enrollmentValid)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await _next(context);
    }

    static bool RequiresClientKey(HttpContext context) =>
        context.WebSockets.IsWebSocketRequest ||
        context.Request.Path.StartsWithSegments("/valheim") ||
        context.Request.Path.StartsWithSegments("/api/v0/valheim/enrollment");

    static bool IsPrivateOrLoopback(System.Net.IPAddress? address)
    {
        if (address is null || System.Net.IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return false;
        return bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168);
    }

    static bool CryptographicEquals(string actual, string expected)
    {
        var left = System.Text.Encoding.UTF8.GetBytes(actual);
        var right = System.Text.Encoding.UTF8.GetBytes(expected);
        return left.Length == right.Length &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(left, right);
    }
}
