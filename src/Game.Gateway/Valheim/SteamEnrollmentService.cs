using System.Security.Cryptography;
using System.Text.Json;

namespace Game.Gateway.Valheim;

/// <summary>Small file-backed invite store for the trusted volunteer pilot.</summary>
public sealed class SteamEnrollmentService
{
    readonly object _gate = new();
    readonly string _path;
    Dictionary<string, Invite> _invites = new(StringComparer.Ordinal);

    public SteamEnrollmentService(IConfiguration configuration)
    {
        _path = configuration["LUMBERJACKS_ENROLLMENT_PATH"] ?? "/var/lib/lumberjacks/enrollment/invites.json";
        Load();
    }

    public InviteReceipt CreateInvite(TimeSpan lifetime)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var invite = new Invite(token, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(lifetime));
        lock (_gate) { _invites[token] = invite; Save(); }
        return new InviteReceipt(token, invite.ExpiresUtc);
    }

    public bool Redeem(string token, string steamId, out Enrollment enrollment)
    {
        lock (_gate)
        {
            enrollment = null!;
            if (!_invites.TryGetValue(token, out var invite) || invite.Used || invite.ExpiresUtc <= DateTimeOffset.UtcNow || string.IsNullOrWhiteSpace(steamId)) return false;
            enrollment = new Enrollment(
                steamId,
                Guid.NewGuid().ToString("N"),
                DateTimeOffset.UtcNow,
                NewToken(),
                Environment.GetEnvironmentVariable("LUMBERJACKS_AUTHORITATIVE_WINDOW_ID") ?? "p7-primary-v1");
            _invites[token] = invite with { Used = true, Enrollment = enrollment };
            Save();
            return true;
        }
    }

    public bool IsCredentialValid(string enrollmentId, string accessToken)
    {
        if (string.IsNullOrWhiteSpace(enrollmentId) || string.IsNullOrWhiteSpace(accessToken)) return false;
        lock (_gate)
        {
            var enrollment = _invites.Values.Select(invite => invite.Enrollment)
                .FirstOrDefault(item => item is not null && string.Equals(item.ManifestId, enrollmentId, StringComparison.Ordinal));
            return enrollment is not null && FixedTimeEquals(enrollment.AccessToken, accessToken);
        }
    }

    static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static bool FixedTimeEquals(string left, string right)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(left ?? string.Empty);
        var b = System.Text.Encoding.UTF8.GetBytes(right ?? string.Empty);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    void Load()
    {
        try { if (File.Exists(_path)) _invites = JsonSerializer.Deserialize<Dictionary<string, Invite>>(File.ReadAllText(_path)) ?? _invites; } catch { }
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_invites, new JsonSerializerOptions { WriteIndented = true }));
    }

    public sealed record Invite(string Token, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, bool Used = false, Enrollment? Enrollment = null);
    public sealed record InviteReceipt(string Token, DateTimeOffset ExpiresUtc);
    public sealed record Enrollment(
        string SteamId,
        string ManifestId,
        DateTimeOffset EnrolledUtc,
        string AccessToken = "",
        string QueueWindowId = "p7-primary-v1");
}
