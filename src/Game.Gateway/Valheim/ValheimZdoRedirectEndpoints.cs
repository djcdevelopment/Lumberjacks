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
                durable_queue = redirects.PersistenceEnabled,
                persistence_healthy = redirects.PersistenceHealthy,
                wal_bytes = redirects.WalBytes,
                windows = redirects.GetAllStatuses().Select(ToResponse),
            });
        });

        group.MapGet("/status/{windowId}", (string windowId, ValheimZdoRedirectService redirects) =>
        {
            var status = redirects.GetStatus(windowId);
            return Results.Ok(ToResponse(status));
        });

        group.MapPost("/compact", (ValheimZdoRedirectService redirects) =>
        {
            var before = redirects.WalBytes;
            var started = System.Diagnostics.Stopwatch.StartNew();
            var after = redirects.Compact();
            started.Stop();
            return Results.Ok(new
            {
                ok = true,
                before_bytes = before,
                after_bytes = after,
                reduction_bytes = before - after,
                reduction_percent = before == 0 ? 0 : 100d * (before - after) / before,
                duration_ms = started.Elapsed.TotalMilliseconds,
            });
        });

        // A consumer poll is the seat gate's sign of life for this window — see
        // ValheimWindowActivityService. Recorded on the request, not inside the ZDO service, so the
        // hot path is untouched.
        group.MapGet("/pending/{windowId}", (string windowId, int? limit,
            ValheimZdoRedirectService redirects, ValheimWindowActivityService activity) =>
        {
            activity.Touch(windowId, DateTime.UtcNow);
            return Results.Ok(new { schema_version = 1, window_id = windowId, envelopes = redirects.Pending(windowId, limit ?? 64) });
        }).RequireRateLimiting("consumer");

        // Who a consumer IS is the server's to say, not the caller's. Where the caller presented an
        // enrollment, the server-derived RecipientId is what gets recorded — the client cannot
        // select which recipient it is filed as.
        //
        // `consumer_id` is therefore OPTIONAL as of the stage-3 mod cut, which stops the client
        // naming itself at all (ZdoAuthoritativeConsumerRunner no longer has a _consumerId). It
        // stays *accepted* rather than refused because the frozen 0.5.31 mod always sends a GUID it
        // invented, so a mismatch is the normal case and not an attack; refusing it would break
        // every heartbeat from a rolled-back mod. The mod never reads the value back — it ignores
        // this response body entirely — so substituting is invisible to it.
        //
        // Required only when there is no enrollment to derive from: the legacy shared-key path has
        // no server-side identity, so a caller-supplied label is the only one available.
        //
        // Tightening this to a 403 on mismatch is deliberately NOT done here. It becomes correct
        // once no supported mod sends the field, and doing it while a rollback to 0.5.31 must keep
        // working would trade a real outage for a property the override already provides.
        group.MapPost("/consumer", (ValheimZdoConsumerHeartbeat heartbeat, HttpContext context,
            ValheimZdoConsumerTelemetryService consumers) =>
        {
            // The rule lives in ValheimConsumerHeartbeatPolicy, not here, and that is the point:
            // nothing in this lambda is reachable from a test (Game.Gateway.Tests is service-level
            // throughout, and a WebApplicationFactory here means standing up Postgres), so a
            // decision left inside it is a decision nobody can check. This method now only plumbs.
            var recipientId = ValheimPrincipal.From(context)?.Enrollment?.RecipientId;
            var resolved = ValheimConsumerHeartbeatPolicy.Resolve(heartbeat, recipientId);
            if (resolved.Error is not null)
            {
                return Results.BadRequest(new { error = resolved.Error });
            }

            consumers.Record(resolved.Recorded!);
            return Results.Ok(new { ok = true, received_at = DateTimeOffset.UtcNow });
        }).RequireRateLimiting("telemetry");

        app.MapGet("/api/v0/valheim/zdo-consumers/{windowId}", (string windowId,
            ValheimZdoConsumerTelemetryService consumers) => Results.Ok(consumers.Snapshot(windowId)))
            .RequireCors(Game.ServiceDefaults.PublicTelemetryV0.CorsPolicyName);

        group.MapPost("/ack/{windowId}", (string windowId, long[] sequences,
            ValheimZdoRedirectService redirects, ValheimWindowActivityService activity) =>
        {
            if (sequences is null || sequences.Length == 0)
                return Results.BadRequest(new { error = "sequences is required" });
            activity.Touch(windowId, DateTime.UtcNow);
            var result = redirects.Acknowledge(windowId, sequences);
            return Results.Ok(new { window_id = windowId, acknowledged = result.Acknowledged, unknown = result.Unknown });
        }).RequireRateLimiting("consumer");

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
        acknowledged = status.Acknowledged,
        pending = status.Pending,
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
