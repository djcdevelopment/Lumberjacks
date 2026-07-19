using Game.Gateway.Valheim;
using Xunit;

namespace Game.Gateway.Tests;

public sealed class ValheimRecipientIsolationTests
{
    private const string Window = "recipient-isolation";

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public void ValheimRecipientIsolation(int consumerCount)
    {
        var service = new ValheimZdoRedirectService();
        var telemetry = new ValheimZdoConsumerTelemetryService();
        var principals = Enumerable.Range(0, consumerCount)
            .Select(index => Principal("rcpt-" + index))
            .ToArray();

        foreach (var (principal, index) in principals.Select((principal, index) => (principal, index)))
        {
            var recipient = Resolve(principal);
            service.RecordEnvelopes(Window, "producer", [new ValheimZdoRedirectEnvelope
            {
                Seq = index + 1,
                RecipientId = recipient,
                BodyB64 = "AA==",
            }]);
        }

        foreach (var (principal, index) in principals.Select((principal, index) => (principal, index)))
        {
            var recipient = Resolve(principal);
            var ownPending = service.Pending(Window, recipient, 64);
            Assert.Equal([index + 1L], ownPending.Select(envelope => envelope.Seq).ToArray());

            var ownStatus = service.GetStatus(Window, recipient);
            Assert.Equal(recipient, ownStatus.RecipientId);
            Assert.Equal(1, ownStatus.Receipts);
            Assert.Equal(1, ownStatus.Pending);

            var forgedAck = service.Acknowledge(Window, Resolve(principals[(index + 1) % consumerCount]), [index + 1L]);
            Assert.Equal(0, forgedAck.Acknowledged);
            Assert.Equal(1, forgedAck.Unknown);
            Assert.Equal(1, service.GetStatus(Window, recipient).Pending);

            var ownAck = service.Acknowledge(Window, recipient, [index + 1L]);
            Assert.Equal(1, ownAck.Acknowledged);
            Assert.Equal(0, service.GetStatus(Window, recipient).Pending);
            Assert.Equal(1, service.GetStatus(Window, recipient).Acknowledged);
            telemetry.Record(new ValheimZdoConsumerHeartbeat
            {
                WindowId = Window,
                ConsumerId = recipient,
                Applied = 1,
                Acknowledged = 1,
            });

            var conservation = service.GetStatus(Window, recipient);
            var terminal = telemetry.GetRecipientStatus(Window, recipient);
            Assert.Equal(conservation.Eligible, conservation.Durable);
            Assert.Equal(conservation.Durable,
                terminal.Applied + terminal.Superseded + conservation.Pending);
        }

        var aggregate = service.GetStatus(Window);
        Assert.Equal(consumerCount, aggregate.Receipts);
        Assert.Equal(0, aggregate.Pending);
        Assert.Equal(consumerCount, aggregate.Acknowledged);
    }

    [Fact]
    public void LegacyUnscopedConsumer_StillDrainsItsOwnWindow()
    {
        var service = new ValheimZdoRedirectService();
        var scope = ValheimRecipientScopePolicy.Resolve("shared-client-key", null, "forged");
        Assert.Null(scope.Error);

        service.RecordEnvelopes(Window, "legacy", [new ValheimZdoRedirectEnvelope { Seq = 1, BodyB64 = "AA==" }]);
        Assert.Single(service.Pending(Window, scope.Resolved!, 64));
        Assert.Equal(1, service.Acknowledge(Window, scope.Resolved!, [1]).Acknowledged);
    }

    private static string Resolve(ValheimPrincipal principal)
    {
        var result = ValheimRecipientScopePolicy.Resolve(
            principal.Kind, principal.Enrollment?.RecipientId, requestedRecipient: "forged");
        Assert.Null(result.Error);
        return result.Resolved!;
    }

    private static ValheimPrincipal Principal(string recipient) => new(
        "enrollment",
        ValheimCapability.Consumer,
        new SteamEnrollmentService.EnrollmentView(
            "enrollment-" + recipient, "steam-" + recipient, recipient, "active",
            DateTimeOffset.UtcNow, null, Window));
}
