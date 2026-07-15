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

    [Fact]
    public void LatestModVersionTracksTheRunningValheimHeartbeat()
    {
        var service = new ValheimTelemetryHeartbeatService();
        Assert.Null(service.LatestModVersion());

        service.Record(new ValheimTelemetryHeartbeat
        {
            InstanceId = "server-test",
            ModVersion = "0.5.22",
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
        });

        Assert.Equal("0.5.22", service.LatestModVersion());
    }

    [Fact]
    public void PromotionAcceptsExplicitlySupersededOlderRevisions()
    {
        var heartbeat = new ValheimTelemetryHeartbeatService();
        var redirects = new ValheimZdoRedirectService();
        var consumers = new ValheimZdoConsumerTelemetryService();
        redirects.RecordEnvelopes(Window, "server", [Envelope(1), Envelope(2)]);
        redirects.Acknowledge(Window, [1, 2]);

        consumers.Record(Consumer(applied: 1, acknowledged: 2) with { Superseded = 1 });

        Assert.True(heartbeat.IsAuthoritativeComplete(Window, redirects, consumers));
    }

    [Fact]
    public void DurableQueueReplaysReceiptsAcksAndRepairsATruncatedTail()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lumberjacks-zdo-wal-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "redirect.wal");
        try
        {
            var writer = new ValheimZdoRedirectService(path);
            writer.RecordEnvelopes(Window, "server", [Envelope(1), Envelope(2)]);
            writer.Acknowledge(Window, [1]);
            var validBytes = new FileInfo(path).Length;
            using (var tail = new FileStream(path, FileMode.Append, FileAccess.Write))
                tail.Write([0x7f, 0x01]);

            var replayed = new ValheimZdoRedirectService(path);
            var status = replayed.GetStatus(Window);
            Assert.True(replayed.PersistenceEnabled);
            Assert.True(replayed.PersistenceHealthy);
            Assert.Equal(validBytes, replayed.WalBytes);
            Assert.Equal(2, status.DistinctSeq);
            Assert.Equal(1, status.Acknowledged);
            Assert.Equal(1, status.Pending);

            replayed.Reset(Window);
            Assert.Equal(0, new ValheimZdoRedirectService(path).GetStatus(Window).Receipts);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PromotionAllowsSuppressedAtLeastOnceTransportDuplicates()
    {
        var heartbeat = new ValheimTelemetryHeartbeatService();
        var redirects = new ValheimZdoRedirectService();
        var consumers = new ValheimZdoConsumerTelemetryService();
        redirects.RecordEnvelopes(Window, "server", [Envelope(1), Envelope(2)]);
        redirects.RecordEnvelopes(Window, "server-retry", [Envelope(1)]);
        redirects.Acknowledge(Window, [1, 2]);
        consumers.Record(Consumer(applied: 2, acknowledged: 2));

        Assert.Equal(1, redirects.GetStatus(Window).Duplicates);
        Assert.True(heartbeat.IsAuthoritativeComplete(Window, redirects, consumers));
    }

    [Fact]
    public void EmptyPrimaryServerKeepsHeartbeatLiveWithoutAConsumer()
    {
        var heartbeat = new ValheimTelemetryHeartbeatService();
        var redirects = new ValheimZdoRedirectService();
        var consumers = new ValheimZdoConsumerTelemetryService();
        var sample = new ValheimTelemetryHeartbeat
        {
            CutoverMode = "lumberjacks-primary",
            EnrollmentManifestId = Window,
            CoverageTotal = 10,
            CoverageLumberjacks = 10,
            CoverageNativeOnly = 0,
            PeerCount = 0,
        };

        Assert.True(heartbeat.CanAcceptPrimaryHeartbeat(sample, redirects, consumers));
        Assert.False(heartbeat.CanAcceptPrimaryHeartbeat(sample with { PeerCount = 1 }, redirects, consumers));
        Assert.False(heartbeat.CanAcceptPrimaryHeartbeat(sample with { CoverageNativeOnly = 1 }, redirects, consumers));
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
