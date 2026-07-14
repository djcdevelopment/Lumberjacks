namespace Game.Gateway.Valheim;

/// <summary>
/// Receive endpoint for Valheim ZDO payloads redirected by the Harmony mod
/// after it suppresses the original send. Exposes gate-math counters for the
/// test gate: receipt count == suppressed-send count, with sequence-gap loss
/// detection.
/// </summary>
public static class ValheimZdoRedirectEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/valheim/zdo-redirect");

        group.MapPost("/receipts", (
            ValheimZdoRedirectRequest request,
            ValheimZdoRedirectService redirects) =>
        {
            if (string.IsNullOrWhiteSpace(request.WindowId))
                return Results.BadRequest(new { error = "window_id is required" });

            if (request.Envelopes is null)
                return Results.BadRequest(new { error = "envelopes is required" });

            for (var i = 0; i < request.Envelopes.Count; i++)
            {
                if (request.Envelopes[i].Seq is null)
                    return Results.BadRequest(new { error = $"envelope at index {i} is missing seq" });
            }

            var source = string.IsNullOrWhiteSpace(request.Source) ? "unknown" : request.Source;
            var result = redirects.RecordEnvelopes(request.WindowId, source, request.Envelopes);

            return Results.Ok(new
            {
                ok = true,
                window_id = request.WindowId,
                received = result.Received,
                total = result.Total,
            });
        });

        group.MapGet("/status", (ValheimZdoRedirectService redirects) =>
        {
            return Results.Ok(new
            {
                windows = redirects.GetAllStatuses().Select(ToResponse),
            });
        });

        group.MapGet("/status/{windowId}", (string windowId, ValheimZdoRedirectService redirects) =>
        {
            var status = redirects.GetStatus(windowId);
            return Results.Ok(ToResponse(status));
        });

        group.MapGet("/pending/{windowId}", (string windowId, int? limit, ValheimZdoRedirectService redirects) =>
            Results.Ok(new { window_id = windowId, envelopes = redirects.Pending(windowId, limit ?? 64) }));

        group.MapPost("/ack/{windowId}", (string windowId, long[] sequences, ValheimZdoRedirectService redirects) =>
        {
            if (sequences is null || sequences.Length == 0)
                return Results.BadRequest(new { error = "sequences is required" });
            var result = redirects.Acknowledge(windowId, sequences);
            return Results.Ok(new { window_id = windowId, acknowledged = result.Acknowledged, unknown = result.Unknown });
        });

        group.MapPost("/reset/{windowId}", (string windowId, ValheimZdoRedirectService redirects) =>
        {
            var existed = redirects.Reset(windowId);
            return Results.Ok(new
            {
                ok = true,
                window_id = windowId,
                reset = existed,
            });
        });

        group.MapPost("/reset", (ValheimZdoRedirectService redirects) =>
        {
            var cleared = redirects.ResetAll();
            return Results.Ok(new
            {
                ok = true,
                reset_all = true,
                windows_cleared = cleared,
            });
        });
    }

    private static object ToResponse(ValheimZdoRedirectWindowStatus status) => new
    {
        window_id = status.WindowId,
        receipts = status.Receipts,
        distinct_seq = status.DistinctSeq,
        duplicates = status.Duplicates,
        min_seq = status.MinSeq,
        max_seq = status.MaxSeq,
        missing_seq = status.MissingSeq,
        seq_tracking_saturated = status.SeqTrackingSaturated,
        empty_body_count = status.EmptyBodyCount,
        first_utc = status.FirstUtc,
        last_utc = status.LastUtc,
        per_prefab = status.PerPrefab,
        per_source = status.PerSource,
    };
}
