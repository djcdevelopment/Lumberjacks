using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Game.Gateway.Valheim;

/// <summary>
/// File-backed enrollment store for the volunteer pilot (M1 schema v2).
///
/// Invite and access tokens are 256-bit random values, so a single unsalted
/// SHA-256 at rest is cryptographically sufficient (no KDF stretching is needed
/// for high-entropy secrets) and cheap enough to verify on every authenticated
/// request. Raw secrets exist only in the issuance response; they are never
/// persisted and never returned again. A v1 plaintext store found on disk is
/// migrated in place on first load.
/// </summary>
public sealed class SteamEnrollmentService
{
    readonly object _gate = new();
    readonly string _path;
    readonly string _auditPath;
    Dictionary<string, Invite> _invites = new(StringComparer.Ordinal);
    Dictionary<string, Enrollment> _enrollments = new(StringComparer.Ordinal);
    readonly Dictionary<string, DateTimeOffset> _persistedLastUsed = new(StringComparer.Ordinal);
    static readonly TimeSpan LastUsedPersistInterval = TimeSpan.FromSeconds(60);

    public SteamEnrollmentService(IConfiguration configuration)
    {
        _path = configuration["LUMBERJACKS_ENROLLMENT_PATH"] ?? "/var/lib/lumberjacks/enrollment/invites.json";
        _auditPath = Path.Combine(Path.GetDirectoryName(_path) ?? ".", "enrollment-audit.jsonl");
        Load();
    }

    public InviteReceipt CreateInvite(TimeSpan lifetime)
    {
        var token = NewToken();
        var invite = new Invite(Hash(token), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.Add(lifetime));
        lock (_gate)
        {
            _invites[invite.TokenHash] = invite;
            Save();
        }
        Audit("invite_created", new { expires_utc = invite.ExpiresUtc });
        return new InviteReceipt(token, invite.ExpiresUtc);
    }

    public bool TryRedeem(string token, string steamId, out EnrollmentIssued issued, out string reason)
    {
        issued = null!;
        reason = RedeemLocked(token, steamId, out issued);
        if (reason != "ok")
        {
            Audit("redeem_rejected", new { reason });
            return false;
        }
        Audit("enrollment_redeemed", new { enrollment_id = issued.Enrollment.EnrollmentId, steam_id = steamId });
        return true;
    }

    string RedeemLocked(string token, string steamId, out EnrollmentIssued issued)
    {
        issued = null!;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(steamId)) return "invite_invalid";

        lock (_gate)
        {
            if (!_invites.TryGetValue(Hash(token), out var invite)) return "invite_invalid";
            if (invite.Used) return "invite_consumed";
            if (invite.ExpiresUtc <= DateTimeOffset.UtcNow) return "invite_expired";
            // One active enrollment per SteamID; an admin replaces it by revoking
            // the existing record first (explicit, audited).
            if (_enrollments.Values.Any(item =>
                item.Status == EnrollmentStatus.Active &&
                string.Equals(item.SteamId, steamId, StringComparison.Ordinal)))
            {
                return "steamid_already_enrolled";
            }

            var accessToken = NewToken();
            var enrollment = new Enrollment(
                EnrollmentId: Guid.NewGuid().ToString("N"),
                SteamId: steamId,
                RecipientId: Guid.NewGuid().ToString("N"),
                TokenHash: Hash(accessToken),
                Status: EnrollmentStatus.Active,
                EnrolledUtc: DateTimeOffset.UtcNow,
                LastUsedUtc: null,
                RevokedUtc: null,
                RevokedReason: null,
                QueueWindowId: Environment.GetEnvironmentVariable("LUMBERJACKS_AUTHORITATIVE_WINDOW_ID") ?? "p7-primary-v1");
            _invites[invite.TokenHash] = invite with { Used = true, EnrollmentId = enrollment.EnrollmentId };
            _enrollments[enrollment.EnrollmentId] = enrollment;
            Save();
            issued = new EnrollmentIssued(View(enrollment), accessToken);
        }
        return "ok";
    }

    public bool Verify(string enrollmentId, string accessToken, out EnrollmentView view, out string reason)
    {
        view = null!;
        if (string.IsNullOrWhiteSpace(enrollmentId) || string.IsNullOrWhiteSpace(accessToken))
        {
            reason = "credentials_required";
            return false;
        }
        lock (_gate)
        {
            if (!_enrollments.TryGetValue(enrollmentId, out var enrollment))
            {
                reason = "enrollment_unknown";
                return false;
            }
            if (!FixedTimeEquals(enrollment.TokenHash, Hash(accessToken)))
            {
                reason = "token_invalid";
                return false;
            }
            if (enrollment.Status != EnrollmentStatus.Active)
            {
                reason = "enrollment_revoked";
                return false;
            }
            var now = DateTimeOffset.UtcNow;
            enrollment = enrollment with { LastUsedUtc = now };
            _enrollments[enrollmentId] = enrollment;
            // Persisting on every verified request would thrash the store at the
            // consumer poll rate; the on-disk last-used value trails by ≤60 s.
            if (!_persistedLastUsed.TryGetValue(enrollmentId, out var persisted) ||
                now - persisted >= LastUsedPersistInterval)
            {
                _persistedLastUsed[enrollmentId] = now;
                Save();
            }
            view = View(enrollment);
        }
        reason = "ok";
        return true;
    }

    /// <summary>Compatibility shim for callers that only need a boolean.</summary>
    public bool IsCredentialValid(string enrollmentId, string accessToken) =>
        Verify(enrollmentId, accessToken, out _, out _);

    public IReadOnlyList<EnrollmentView> List()
    {
        lock (_gate) return _enrollments.Values.Select(View).OrderBy(item => item.EnrolledUtc).ToList();
    }

    public EnrollmentView? Get(string enrollmentId)
    {
        lock (_gate) return _enrollments.TryGetValue(enrollmentId, out var item) ? View(item) : null;
    }

    public bool Revoke(string enrollmentId, string reason)
    {
        lock (_gate)
        {
            if (!_enrollments.TryGetValue(enrollmentId, out var enrollment) ||
                enrollment.Status != EnrollmentStatus.Active) return false;
            _enrollments[enrollmentId] = enrollment with
            {
                Status = EnrollmentStatus.Revoked,
                RevokedUtc = DateTimeOffset.UtcNow,
                RevokedReason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason,
            };
            Save();
        }
        Audit("enrollment_revoked", new { enrollment_id = enrollmentId, reason });
        return true;
    }

    static EnrollmentView View(Enrollment enrollment) => new(
        enrollment.EnrollmentId,
        enrollment.SteamId,
        enrollment.RecipientId,
        enrollment.Status.ToString().ToLowerInvariant(),
        enrollment.EnrolledUtc,
        enrollment.LastUsedUtc,
        enrollment.QueueWindowId);

    static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    static bool FixedTimeEquals(string left, string right)
    {
        var a = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    void Audit(string eventName, object detail)
    {
        try
        {
            var line = JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, @event = eventName, detail });
            lock (_gate) File.AppendAllText(_auditPath, line + "\n");
        }
        catch
        {
            // The audit trail must never take the control plane down.
        }
    }

    void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var text = File.ReadAllText(_path);
            using var probe = JsonDocument.Parse(text);
            if (probe.RootElement.TryGetProperty("schema_version", out _))
            {
                var store = JsonSerializer.Deserialize<StoreFile>(text, SerializerOptions);
                if (store is not null)
                {
                    _invites = store.Invites ?? _invites;
                    _enrollments = store.Enrollments ?? _enrollments;
                }
                return;
            }
            MigrateV1(text);
        }
        catch
        {
            // An unreadable store yields an empty roster: fail closed (nobody
            // verifies) rather than failing open.
        }
    }

    /// <summary>v1 stored raw invite tokens as keys and raw access tokens inline.</summary>
    void MigrateV1(string text)
    {
        var v1 = JsonSerializer.Deserialize<Dictionary<string, V1Invite>>(text);
        if (v1 is null) return;
        foreach (var (rawToken, invite) in v1)
        {
            string? enrollmentId = null;
            if (invite.Enrollment is not null)
            {
                enrollmentId = invite.Enrollment.ManifestId;
                _enrollments[enrollmentId] = new Enrollment(
                    EnrollmentId: enrollmentId,
                    SteamId: invite.Enrollment.SteamId,
                    RecipientId: Guid.NewGuid().ToString("N"),
                    TokenHash: Hash(invite.Enrollment.AccessToken),
                    Status: EnrollmentStatus.Active,
                    EnrolledUtc: invite.Enrollment.EnrolledUtc,
                    LastUsedUtc: null,
                    RevokedUtc: null,
                    RevokedReason: null,
                    QueueWindowId: invite.Enrollment.QueueWindowId);
            }
            _invites[Hash(rawToken)] = new Invite(Hash(rawToken), invite.CreatedUtc, invite.ExpiresUtc, invite.Used, enrollmentId);
        }
        var collapsed = CollapseDuplicateSteamIds();
        Save();
        // Audited only once the collapse is on disk, matching Revoke.
        foreach (var (enrollment, survivorId) in collapsed)
        {
            Audit("enrollment_revoked", new
            {
                enrollment_id = enrollment.EnrollmentId,
                steam_id = enrollment.SteamId,
                reason = SupersededByMigration,
                superseded_by = survivorId,
            });
        }
        Audit("store_migrated_v1", new
        {
            invites = _invites.Count,
            enrollments = _enrollments.Count,
            collapsed = collapsed.Count,
        });
    }

    /// <summary>
    /// v1 had no one-active-enrollment-per-SteamID rule, so its store can hold
    /// several redeemed invites for the same player. Migrating them all as active
    /// would seed a v2 roster that violates the invariant <see cref="RedeemLocked"/>
    /// enforces on every redeem. The newest enrollment wins: it is the one the
    /// player most recently proved they wanted, and it matches what an admin doing
    /// the replacement by hand would have done.
    /// </summary>
    List<(Enrollment Superseded, string SurvivorId)> CollapseDuplicateSteamIds()
    {
        var collapsed = new List<(Enrollment, string)>();
        // Materialized before the loop mutates _enrollments.
        var duplicates = _enrollments.Values
            .Where(item => item.Status == EnrollmentStatus.Active)
            .GroupBy(item => item.SteamId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToList();
        foreach (var group in duplicates)
        {
            // Ties on EnrolledUtc break on id so the survivor never depends on
            // the order the v1 dictionary happened to deserialize in.
            var ordered = group
                .OrderByDescending(item => item.EnrolledUtc)
                .ThenByDescending(item => item.EnrollmentId, StringComparer.Ordinal)
                .ToList();
            var survivor = ordered[0];
            foreach (var enrollment in ordered.Skip(1))
            {
                _enrollments[enrollment.EnrollmentId] = enrollment with
                {
                    Status = EnrollmentStatus.Revoked,
                    RevokedUtc = DateTimeOffset.UtcNow,
                    RevokedReason = SupersededByMigration,
                };
                collapsed.Add((enrollment, survivor.EnrollmentId));
            }
        }
        return collapsed;
    }

    public const string SupersededByMigration = "superseded_by_migration";

    static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var store = new StoreFile(2, _invites, _enrollments);
        File.WriteAllText(_path, JsonSerializer.Serialize(store, SerializerOptions));
    }

    public enum EnrollmentStatus { Active, Revoked }

    sealed record StoreFile(
        [property: System.Text.Json.Serialization.JsonPropertyName("schema_version")] int SchemaVersion,
        Dictionary<string, Invite> Invites,
        Dictionary<string, Enrollment> Enrollments);

    public sealed record Invite(string TokenHash, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, bool Used = false, string? EnrollmentId = null);
    public sealed record InviteReceipt(string Token, DateTimeOffset ExpiresUtc);
    public sealed record EnrollmentIssued(EnrollmentView Enrollment, string AccessToken);

    public sealed record Enrollment(
        string EnrollmentId,
        string SteamId,
        string RecipientId,
        string TokenHash,
        EnrollmentStatus Status,
        DateTimeOffset EnrolledUtc,
        DateTimeOffset? LastUsedUtc,
        DateTimeOffset? RevokedUtc,
        string? RevokedReason,
        string QueueWindowId);

    /// <summary>Secret-free projection served to admins and to the enrollee itself.</summary>
    public sealed record EnrollmentView(
        string EnrollmentId,
        string SteamId,
        string RecipientId,
        string Status,
        DateTimeOffset EnrolledUtc,
        DateTimeOffset? LastUsedUtc,
        string QueueWindowId);

    sealed record V1Invite(string Token, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, bool Used = false, V1Enrollment? Enrollment = null);
    sealed record V1Enrollment(
        string SteamId,
        string ManifestId,
        DateTimeOffset EnrolledUtc,
        string AccessToken = "",
        string QueueWindowId = "p7-primary-v1");
}
