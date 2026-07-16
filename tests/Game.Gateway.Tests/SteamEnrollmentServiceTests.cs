using Game.Gateway.Valheim;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Game.Gateway.Tests;

public sealed class SteamEnrollmentServiceTests : IDisposable
{
    readonly string _directory = Path.Combine(Path.GetTempPath(), "lumberjacks-enrollment-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Redeem_BindsSteamIdentityToCredentialAndQueueWindow()
    {
        var service = CreateService();
        var invite = service.CreateInvite(TimeSpan.FromMinutes(5));

        const string syntheticSteamId = "76561198000000001";
        Assert.True(service.Redeem(invite.Token, syntheticSteamId, out var enrollment));
        Assert.Equal(syntheticSteamId, enrollment.SteamId);
        Assert.Equal("p7-primary-v1", enrollment.QueueWindowId);
        Assert.NotEmpty(enrollment.ManifestId);
        Assert.NotEmpty(enrollment.AccessToken);
        Assert.True(service.IsCredentialValid(enrollment.ManifestId, enrollment.AccessToken));
        Assert.False(service.IsCredentialValid(enrollment.ManifestId, "wrong"));
    }

    [Fact]
    public void Redeem_IsSingleUse()
    {
        var service = CreateService();
        var invite = service.CreateInvite(TimeSpan.FromMinutes(5));

        Assert.True(service.Redeem(invite.Token, "76561198000000001", out _));
        Assert.False(service.Redeem(invite.Token, "76561198000000000", out _));
    }

    SteamEnrollmentService CreateService()
    {
        Directory.CreateDirectory(_directory);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LUMBERJACKS_ENROLLMENT_PATH"] = Path.Combine(_directory, "invites.json"),
        }).Build();
        return new SteamEnrollmentService(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
