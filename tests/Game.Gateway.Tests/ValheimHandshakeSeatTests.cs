using Game.Gateway.Valheim;
using Xunit;

namespace Game.Gateway.Tests;

/// <summary>
/// The M1 stage-2 seat gate: the one admission rule buildable Gateway-only, because a seat needs a
/// count rather than a name (plan §4). Every case here drives a fixed clock, so lease expiry is
/// asserted rather than slept on.
///
/// The gate's whole difficulty is that the Gateway is never told a player left (plan §5.4), so
/// these tests pin both directions of the tradeoff: a live consumer must hold its seat
/// indefinitely, and a holder that goes silent must release it inside the lease.
/// </summary>
public sealed class ValheimHandshakeSeatTests
{
    private const string Window = "i5-seat";
    private static readonly DateTime T0 = new(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);

    private const long HolderUid = 5_497_853_135_698;
    private const long RivalUid = 1_167_002_880;

    [Fact]
    public void SecondUid_IsRejected_WhileFirstHoldsTheSeat()
    {
        var now = T0;
        var service = new ValheimHandshakeService(nowUtc: () => now);

        Assert.True(service.SubmitPeerInfo(Window, Submission("c1", HolderUid)).Result!.Accept);

        now = T0.AddSeconds(5);
        var rival = service.SubmitPeerInfo(Window, Submission("c2", RivalUid)).Result!;

        Assert.False(rival.Accept);
        Assert.False(rival.EntersSteadyState);
        // ErrorFull is the only native code meaning "no room" — the player sees vanilla's
        // full-server screen and the operator gets the reason in the log (plan §6).
        Assert.Equal((int)ValheimConnectionStatus.ErrorFull, rival.ErrorCode);
        Assert.Equal("capacity_reserved", rival.FailedCheck);
    }

    [Fact]
    public void SameUid_RehandshakingDoesNotCompeteWithItself()
    {
        var now = T0;
        var service = new ValheimHandshakeService(nowUtc: () => now);
        Assert.True(service.SubmitPeerInfo(Window, Submission("c1", HolderUid)).Result!.Accept);

        // The duplicate gate (G) owns "you are already connected"; the seat gate must not shadow it
        // with a capacity answer, or the reason surfaced to the operator would be a lie.
        now = T0.AddSeconds(5);
        var again = service.SubmitPeerInfo(Window, Submission("c2", HolderUid)).Result!;

        Assert.Equal((int)ValheimConnectionStatus.ErrorAlreadyConnected, again.ErrorCode);
        Assert.Equal("duplicate", again.FailedCheck);
    }

    [Fact]
    public void SeatFrees_OnceTheLeaseLapsesWithNoSignOfLife()
    {
        var now = T0;
        var service = new ValheimHandshakeService(nowUtc: () => now);
        Assert.True(service.SubmitPeerInfo(Window, Submission("c1", HolderUid)).Result!.Accept);

        // A ghost accept looks exactly like this: granted, then never heard from again — vanilla
        // overturned it on the ticket check and the Gateway was never told (plan §5.5).
        now = T0.AddSeconds(61);
        var rival = service.SubmitPeerInfo(Window, Submission("c2", RivalUid)).Result!;

        Assert.True(rival.Accept);
        Assert.True(rival.EntersSteadyState);
    }

    [Fact]
    public void SeatHolds_PastTheGrant_WhileTheConsumerKeepsPolling()
    {
        var now = T0;
        var activity = new ValheimWindowActivityService();
        var service = new ValheimHandshakeService(activity, () => now);
        Assert.True(service.SubmitPeerInfo(Window, Submission("c1", HolderUid)).Result!.Accept);

        // Well past the grant, but the holder is demonstrably still there: without this the seat
        // would expire mid-session and a rival would be admitted onto the shared queue.
        now = T0.AddSeconds(600);
        activity.Touch(Window, now.AddSeconds(-5));

        var rival = service.SubmitPeerInfo(Window, Submission("c2", RivalUid)).Result!;
        Assert.False(rival.Accept);
        Assert.Equal("capacity_reserved", rival.FailedCheck);
    }

    [Fact]
    public void SeatFrees_WhenTheConsumerGoesSilent_SoACrashedVolunteerCanRejoin()
    {
        var now = T0;
        var activity = new ValheimWindowActivityService();
        var service = new ValheimHandshakeService(activity, () => now);
        Assert.True(service.SubmitPeerInfo(Window, Submission("c1", HolderUid)).Result!.Accept);

        now = T0.AddSeconds(300);
        activity.Touch(Window, now.AddSeconds(-5)); // playing happily...

        now = T0.AddSeconds(600); // ...then crashed: last poll is now 305s old.

        // The volunteer relaunches Valheim, which regenerates their session uid — so a rejoin is
        // indistinguishable from a stranger and MUST be let in on liveness alone (plan §5.4).
        var rejoin = service.SubmitPeerInfo(Window, Submission("c2", RivalUid)).Result!;
        Assert.True(rejoin.Accept);
    }

    [Fact]
    public void ActivityOnAnotherWindow_DoesNotHoldThisWindowsSeat()
    {
        var now = T0;
        var activity = new ValheimWindowActivityService();
        var service = new ValheimHandshakeService(activity, () => now);
        Assert.True(service.SubmitPeerInfo(Window, Submission("c1", HolderUid)).Result!.Accept);

        now = T0.AddSeconds(61);
        activity.Touch("some-other-window", now); // liveness must be window-scoped, not global

        Assert.True(service.SubmitPeerInfo(Window, Submission("c2", RivalUid)).Result!.Accept);
    }

    [Fact]
    public void SeatCapacityZero_DisablesTheGate()
    {
        var now = T0;
        var service = new ValheimHandshakeService(nowUtc: () => now);
        Assert.True(service.Configure(Window, new ValheimHandshakeServerContext
        {
            SeatCapacity = 0,
        }).Ok);

        Assert.True(service.SubmitPeerInfo(Window, Submission("c1", HolderUid)).Result!.Accept);
        Assert.True(service.SubmitPeerInfo(Window, Submission("c2", RivalUid)).Result!.Accept);
    }

    [Fact]
    public void Reconfiguring_ReleasesHeldSeats()
    {
        var now = T0;
        var service = new ValheimHandshakeService(nowUtc: () => now);
        Assert.True(service.SubmitPeerInfo(Window, Submission("c1", HolderUid)).Result!.Accept);

        // A new context is a new world; a seat reserved against the old one is meaningless.
        Assert.True(service.Configure(Window, new ValheimHandshakeServerContext()).Ok);

        Assert.True(service.SubmitPeerInfo(Window, Submission("c2", RivalUid)).Result!.Accept);
    }

    [Fact]
    public void Reset_ClearsLiveness_SoAStaleMarkCannotHoldASeatInAFreshWindow()
    {
        var now = T0;
        var activity = new ValheimWindowActivityService();
        var service = new ValheimHandshakeService(activity, () => now);
        Assert.True(service.SubmitPeerInfo(Window, Submission("c1", HolderUid)).Result!.Accept);
        activity.Touch(Window, now);

        Assert.True(service.Reset(Window));
        Assert.Null(activity.LastActivityUtc(Window));

        Assert.True(service.SubmitPeerInfo(Window, Submission("c2", RivalUid)).Result!.Accept);
    }

    [Fact]
    public void CustomLease_IsHonoured()
    {
        var now = T0;
        var service = new ValheimHandshakeService(nowUtc: () => now);
        Assert.True(service.Configure(Window, new ValheimHandshakeServerContext
        {
            SeatLeaseSeconds = 600,
        }).Ok);
        Assert.True(service.SubmitPeerInfo(Window, Submission("c1", HolderUid)).Result!.Accept);

        now = T0.AddSeconds(61); // past the default lease, inside the configured one
        Assert.False(service.SubmitPeerInfo(Window, Submission("c2", RivalUid)).Result!.Accept);

        now = T0.AddSeconds(601);
        Assert.True(service.SubmitPeerInfo(Window, Submission("c3", RivalUid)).Result!.Accept);
    }

    [Fact]
    public void NativeGatesStillWinFirst_SeatGateNeverShadowsAVanillaVerdict()
    {
        var now = T0;
        var service = new ValheimHandshakeService(nowUtc: () => now);
        Assert.True(service.SubmitPeerInfo(Window, Submission("c1", HolderUid)).Result!.Accept);

        // A rival who would fail vanilla's version check must get ErrorVersion, not capacity —
        // the seat gate runs last precisely so native emulation stays exact.
        var wrongVersion = Submission("c2", RivalUid) with { NetVersion = 35 };
        var result = service.SubmitPeerInfo(Window, wrongVersion).Result!;

        Assert.Equal((int)ValheimConnectionStatus.ErrorVersion, result.ErrorCode);
        Assert.Equal("version", result.FailedCheck);
    }

    private static ValheimPeerInfoSubmission Submission(string connectionId, long uid) => new()
    {
        WindowId = Window,
        ConnectionId = connectionId,
        Uid = uid,
        Version = "0.221.12",
        NetVersion = 36,
        RefPos = new double[] { 9376, 105, 544 },
        PlayerName = "floooooobcakes",
        HostName = "steam_76561198000000000",
        PasswordHash = string.Empty,
        TicketValid = true,
    };
}
