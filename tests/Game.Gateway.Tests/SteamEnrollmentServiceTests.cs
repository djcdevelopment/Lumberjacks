using System.Text.Json;
using Game.Gateway.Valheim;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Game.Gateway.Tests;

public sealed class SteamEnrollmentServiceTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), "lumberjacks-enrollment-" + Guid.NewGuid().ToString("N"));

    string StorePath => Path.Combine(_directory, "invites.json");

    [Fact]
    public void Redeem_BindsSteamIdentityToCredentialAndQueueWindow()
    {
        var service = CreateService();
        var invite = service.CreateInvite(TimeSpan.FromMinutes(5));

        const string syntheticSteamId = "76561198000000001";
        Assert.True(service.TryRedeem(invite.Token, syntheticSteamId, out var issued, out _));
        Assert.Equal(syntheticSteamId, issued.Enrollment.SteamId);
        Assert.Equal("p7-primary-v1", issued.Enrollment.QueueWindowId);
        Assert.NotEmpty(issued.Enrollment.EnrollmentId);
        Assert.NotEmpty(issued.Enrollment.RecipientId);
        Assert.NotEmpty(issued.AccessToken);
        Assert.True(service.IsCredentialValid(issued.Enrollment.EnrollmentId, issued.AccessToken));
        Assert.False(service.IsCredentialValid(issued.Enrollment.EnrollmentId, "wrong"));
    }

    [Fact]
    public void Redeem_IsSingleUse()
    {
        var service = CreateService();
        var invite = service.CreateInvite(TimeSpan.FromMinutes(5));

        Assert.True(service.TryRedeem(invite.Token, "76561198000000001", out _, out _));
        Assert.False(service.TryRedeem(invite.Token, "76561198000000000", out _, out var reason));
        Assert.Equal("invite_consumed", reason);
    }

    [Fact]
    public void Redeem_RejectsExpiredInvite()
    {
        var service = CreateService();
        var invite = service.CreateInvite(TimeSpan.FromMilliseconds(-1));

        Assert.False(service.TryRedeem(invite.Token, "76561198000000001", out _, out var reason));
        Assert.Equal("invite_expired", reason);
    }

    [Fact]
    public void Redeem_EnforcesOneActiveEnrollmentPerSteamId()
    {
        var service = CreateService();
        const string steamId = "76561198000000001";
        Assert.True(service.TryRedeem(service.CreateInvite(TimeSpan.FromMinutes(5)).Token, steamId, out var first, out _));

        Assert.False(service.TryRedeem(service.CreateInvite(TimeSpan.FromMinutes(5)).Token, steamId, out _, out var reason));
        Assert.Equal("steamid_already_enrolled", reason);

        // Admin replacement is explicit: revoke, then a fresh invite redeems.
        Assert.True(service.Revoke(first.Enrollment.EnrollmentId, "replaced in test"));
        Assert.True(service.TryRedeem(service.CreateInvite(TimeSpan.FromMinutes(5)).Token, steamId, out _, out _));
    }

    [Fact]
    public void Revoke_InvalidatesCredentialWithReason()
    {
        var service = CreateService();
        Assert.True(service.TryRedeem(service.CreateInvite(TimeSpan.FromMinutes(5)).Token, "76561198000000001", out var issued, out _));
        Assert.True(service.Revoke(issued.Enrollment.EnrollmentId, "test revocation"));

        Assert.False(service.Verify(issued.Enrollment.EnrollmentId, issued.AccessToken, out _, out var reason));
        Assert.Equal("enrollment_revoked", reason);
        Assert.False(service.Revoke(issued.Enrollment.EnrollmentId, "twice"));
    }

    [Fact]
    public void Store_NeverPersistsRawSecrets()
    {
        var service = CreateService();
        var invite = service.CreateInvite(TimeSpan.FromMinutes(5));
        Assert.True(service.TryRedeem(invite.Token, "76561198000000001", out var issued, out _));

        var storeText = File.ReadAllText(StorePath);
        Assert.DoesNotContain(invite.Token, storeText, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.AccessToken, storeText, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_UpdatesLastUsed()
    {
        var service = CreateService();
        Assert.True(service.TryRedeem(service.CreateInvite(TimeSpan.FromMinutes(5)).Token, "76561198000000001", out var issued, out _));
        Assert.Null(issued.Enrollment.LastUsedUtc);

        Assert.True(service.Verify(issued.Enrollment.EnrollmentId, issued.AccessToken, out var view, out _));
        Assert.NotNull(view.LastUsedUtc);
    }

    [Fact]
    public void Load_MigratesV1PlaintextStore()
    {
        Directory.CreateDirectory(_directory);
        const string rawInviteToken = "v1-raw-invite-token";
        const string rawAccessToken = "v1-raw-access-token";
        const string enrollmentId = "abcdef0123456789";
        var v1 = new Dictionary<string, object>
        {
            [rawInviteToken] = new
            {
                Token = rawInviteToken,
                CreatedUtc = DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1),
                Used = true,
                Enrollment = new
                {
                    SteamId = "76561198000000001",
                    ManifestId = enrollmentId,
                    EnrolledUtc = DateTimeOffset.UtcNow.AddDays(-1),
                    AccessToken = rawAccessToken,
                    QueueWindowId = "p7-primary-v1",
                },
            },
        };
        File.WriteAllText(StorePath, JsonSerializer.Serialize(v1));

        var service = CreateService();

        // The frozen mod keeps presenting the same enrollment id + token pair.
        Assert.True(service.Verify(enrollmentId, rawAccessToken, out var view, out _));
        Assert.Equal("76561198000000001", view.SteamId);
        Assert.NotEmpty(view.RecipientId);

        var migratedText = File.ReadAllText(StorePath);
        Assert.DoesNotContain(rawAccessToken, migratedText, StringComparison.Ordinal);
        Assert.DoesNotContain(rawInviteToken, migratedText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mirrors the shape the production P7 store is in: several redeemed v1
    /// invites for one player, deliberately not in EnrolledUtc order.
    /// </summary>
    const string DuplicateSteamId = "76561198088711642";

    void WriteV1StoreWithDuplicateSteamId()
    {
        Directory.CreateDirectory(_directory);
        var enrolled = new[]
        {
            DateTimeOffset.UtcNow.AddDays(-30),
            DateTimeOffset.UtcNow.AddDays(-2),
            DateTimeOffset.UtcNow.AddDays(-10),
        };
        var v1 = new Dictionary<string, object>();
        for (var i = 0; i < enrolled.Length; i++)
        {
            v1["v1-raw-invite-token-" + i] = new
            {
                Token = "v1-raw-invite-token-" + i,
                CreatedUtc = enrolled[i],
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1),
                Used = true,
                Enrollment = new
                {
                    SteamId = DuplicateSteamId,
                    ManifestId = "enrollment-" + i,
                    EnrolledUtc = enrolled[i],
                    AccessToken = "v1-raw-access-token-" + i,
                    QueueWindowId = "p7-primary-v1",
                },
            };
        }
        File.WriteAllText(StorePath, JsonSerializer.Serialize(v1));
    }

    [Fact]
    public void Load_CollapsesDuplicateSteamIdsToNewestActiveEnrollment()
    {
        WriteV1StoreWithDuplicateSteamId();
        const string steamId = DuplicateSteamId;

        var service = CreateService();

        // enrollment-1 is the newest (2 days ago), so it is the one that survives.
        var roster = service.List();
        Assert.Equal(3, roster.Count);
        var active = Assert.Single(roster, item => item.Status == "active");
        Assert.Equal("enrollment-1", active.EnrollmentId);
        Assert.Equal(steamId, active.SteamId);

        foreach (var superseded in new[] { "enrollment-0", "enrollment-2" })
        {
            Assert.Equal("revoked", service.Get(superseded)!.Status);
            // The revoked credential no longer verifies, reason included.
            var index = superseded["enrollment-".Length..];
            Assert.False(service.Verify(superseded, "v1-raw-access-token-" + index, out _, out var reason));
            Assert.Equal("enrollment_revoked", reason);
        }

        // The survivor's frozen-mod credential still works.
        Assert.True(service.Verify("enrollment-1", "v1-raw-access-token-1", out _, out _));

        var audit = File.ReadAllLines(Path.Combine(_directory, "enrollment-audit.jsonl"))
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToList();
        var collapses = audit
            .Where(entry => entry.GetProperty("event").GetString() == "enrollment_revoked")
            .Select(entry => entry.GetProperty("detail"))
            .ToList();
        Assert.Equal(2, collapses.Count);
        Assert.All(collapses, detail =>
        {
            Assert.Equal(SteamEnrollmentService.SupersededByMigration, detail.GetProperty("reason").GetString());
            Assert.Equal(steamId, detail.GetProperty("steam_id").GetString());
            Assert.Equal("enrollment-1", detail.GetProperty("superseded_by").GetString());
        });
        Assert.Equal(
            new[] { "enrollment-0", "enrollment-2" },
            collapses.Select(detail => detail.GetProperty("enrollment_id").GetString()).OrderBy(id => id, StringComparer.Ordinal));

        var migration = Assert.Single(audit, entry => entry.GetProperty("event").GetString() == "store_migrated_v1");
        Assert.Equal(2, migration.GetProperty("detail").GetProperty("collapsed").GetInt32());
    }

    [Fact]
    public void Load_CollapsedDuplicateSurvivesRoundTripAndHoldsTheInvariant()
    {
        WriteV1StoreWithDuplicateSteamId();
        CreateService();

        // Reload the migrated v2 store: the collapse must have been persisted, not
        // just applied in memory, and the surviving record still owns the SteamID.
        var reloaded = CreateService();
        var active = Assert.Single(reloaded.List(), item => item.Status == "active");
        Assert.Equal("enrollment-1", active.EnrollmentId);

        Assert.False(reloaded.TryRedeem(
            reloaded.CreateInvite(TimeSpan.FromMinutes(5)).Token, DuplicateSteamId, out _, out var reason));
        Assert.Equal("steamid_already_enrolled", reason);

        // And an admin revoking the survivor frees the SteamID exactly once.
        Assert.True(reloaded.Revoke("enrollment-1", "replaced in test"));
        Assert.True(reloaded.TryRedeem(
            reloaded.CreateInvite(TimeSpan.FromMinutes(5)).Token, DuplicateSteamId, out _, out _));
    }

    SteamEnrollmentService CreateService()
    {
        Directory.CreateDirectory(_directory);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LUMBERJACKS_ENROLLMENT_PATH"] = StorePath,
        }).Build();
        return new SteamEnrollmentService(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
