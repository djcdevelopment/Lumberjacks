using Game.Gateway.Valheim;
using Xunit;

namespace Game.Gateway.Tests;

public sealed class ValheimZdoAuthoritativeTelemetryTests
{
    private const string Window = "i7-test";

    [Fact]
    public void RedirectStatusTracksAcknowledgedAndPendingSequences()
    {
        var redirects = new ValheimZdoRedirectService();
        redirects.RecordEnvelopes(Window, "server", [Envelope(1), Envelope(2)]);

        var before = redirects.GetStatus(Window);
        Assert.Equal(0, before.Acknowledged);
        Assert.Equal(2, before.Pending);

        var ack = redirects.Acknowledge(Window, [1, 2, 99]);
        Assert.Equal(2, ack.Acknowledged);
        Assert.Equal(1, ack.Unknown);

        var after = redirects.GetStatus(Window);
        Assert.Equal(2, after.Acknowledged);
        Assert.Equal(0, after.Pending);
    }

    [Fact]
    public void PromotionRequiresFreshSuccessfulConsumerAndDrainedQueue()
    {
        var heartbeat = new ValheimTelemetryHeartbeatService();
        var redirects = new ValheimZdoRedirectService();
        var consumers = new ValheimZdoConsumerTelemetryService();
        redirects.RecordEnvelopes(Window, "server", [Envelope(1), Envelope(2)]);

        consumers.Record(Consumer(applied: 2, acknowledged: 2));
        Assert.False(heartbeat.IsAuthoritativeComplete(Window, redirects, consumers));

        redirects.Acknowledge(Window, [1, 2]);
        Assert.True(heartbeat.IsAuthoritativeComplete(Window, redirects, consumers));

        consumers.Record(Consumer(applied: 2, acknowledged: 2) with { Rejected = 1 });
        Assert.False(heartbeat.IsAuthoritativeComplete(Window, redirects, consumers));
    }

    private static ValheimZdoRedirectEnvelope Envelope(long seq) => new()
    {
        Seq = seq,
        UidUser = 1,
        UidId = seq,
        BodyB64 = "AA==",
    };

    private static ValheimZdoConsumerHeartbeat Consumer(long applied, long acknowledged) => new()
    {
        WindowId = Window,
        ConsumerId = "consumer-test",
        ModVersion = "0.5.22",
        TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
        Applied = applied,
        Acknowledged = acknowledged,
    };
}
