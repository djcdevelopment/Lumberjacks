using Game.Contracts.Valheim;

namespace Game.Gateway.Valheim;

public static class ValheimPriorityManifestEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/valheim/priority-manifests");

        group.MapGet("/{manifestId}/delivery-plan", async (
            string manifestId,
            int? reliableBudget,
            int? datagramBudget,
            int? eventLimit,
            ValheimPriorityManifestService manifests,
            CancellationToken cancellationToken) =>
        {
            var result = await manifests.LoadDeliveryPlanAsync(
                manifestId,
                reliableBudget ?? 256,
                datagramBudget ?? 768,
                eventLimit ?? 500,
                cancellationToken);

            if (result.MatchedEventCount == 0)
            {
                return Results.NotFound(new
                {
                    manifest_id = manifestId,
                    message = "No valheim.priority_manifest.objects events matched the manifest id.",
                    eventlog_url = result.EventLogUrl,
                    source_event_count = result.SourceEventCount,
                });
            }

            return Results.Ok(ToResponse(result));
        });
    }

    private static object ToResponse(ValheimPriorityManifestResult result) => new
    {
        manifest_id = result.ManifestId,
        eventlog_url = result.EventLogUrl,
        source_event_count = result.SourceEventCount,
        matched_event_count = result.MatchedEventCount,
        total_input_objects = result.Plan.TotalInputObjects,
        unique_objects = result.Plan.UniqueObjects,
        duplicates_removed = result.Plan.DuplicatesRemoved,
        reliable_budget = result.Plan.ReliableBudget,
        datagram_budget = result.Plan.DatagramBudget,
        reliable = result.Plan.Reliable.Select(ToResponseItem),
        datagram = result.Plan.Datagram.Select(ToResponseItem),
        deferred = result.Plan.Deferred.Select(ToResponseItem),
    };

    private static object ToResponseItem(ValheimPriorityDeliveryItem item) => new
    {
        stable_key = item.StableKey,
        object_name = item.ObjectName,
        object_kind = item.ObjectKind,
        priority_tier = item.PriorityTier,
        priority_rank = item.PriorityRank,
        priority_order = item.PriorityOrder,
        distance_meters = item.DistanceMeters,
        lane = item.Lane.ToString().ToLowerInvariant(),
        delivery_order = item.DeliveryOrder,
        route_stop_id = item.RouteStopId,
        sample_id = item.SampleId,
        position = new
        {
            x = item.Position.X,
            y = item.Position.Y,
            z = item.Position.Z,
        },
    };
}
