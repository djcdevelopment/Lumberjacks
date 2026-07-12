using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Game.Contracts.Entities;
using Game.Contracts.Protocol;
using Game.Contracts.Protocol.Binary;
using Game.ServiceDefaults;
using Game.Simulation.Tick;
using Game.Simulation.World;

namespace Game.Gateway.WebSocket;

/// <summary>
/// Broadcasts authoritative tick state to connected clients.
/// Called by TickLoop (via ITickBroadcaster) after each simulation step for changed entities only.
///
/// Uses InterestManager for per-player AoI filtering, per the active ReplicationPolicy
/// (env Replication__Policy, default "tiered" — see ReplicationOptions):
///   Tiered (default) — Near (0–NearRadius) every tick, Mid (NearRadius–MidRadius) every
///                       MidTickInterval-th tick, Far dropped. 100/300/4 by default.
///   Full              — no filtering; every observer gets every changed entity every tick.
///   Radius            — hard cutoff at NearRadius; inside every tick, outside dropped.
///
/// Sends binary frames to binary-mode sessions, JSON to JSON-mode sessions.
///
/// Send-loop rework (phase 2): the per-region session list is split into
/// Replication:SendWorkers contiguous chunks (default 1 — exactly the original serial
/// foreach) and rotated by tick number first (Replication:SendWorkers-independent — see
/// <see cref="SendFanOut.RotateOffset"/>) so no session is systematically served last. A
/// session appears in exactly one chunk per tick, so per-socket send serialization
/// (WebSocket.SendAsync allows only one outstanding send per socket) is preserved even
/// though chunks run concurrently.
///
/// Deadline shedding (Replication:BroadcastDeadlineMs, default 0/off): one
/// CancellationTokenSource per broadcast call, shared by every send this tick. A session
/// whose send is still in flight (or hasn't started) when the deadline fires gets
/// OperationCanceledException, its socket is Abort()'d (a mid-frame cancel corrupts the WS
/// stream — never keep using it) and counted, and the loop moves on: the tick must end
/// within budget even if that means shedding the slowest sessions this tick. The existing
/// per-send try/catch + SessionManager's stale-session cleanup reaps the aborted sessions.
///
/// Adaptive degrade (Replication:AdaptiveDegrade, default false — ADR-0011 "reduce
/// frequency before dropping"): stateless beyond the PREVIOUS broadcast's wall-clock time
/// (tracked here, self-measured — the same interval TickLoop measures around this whole
/// call, since nothing else runs between them). If that exceeded the 50ms tick budget, this
/// tick suppresses the mid band (tiered policy, via InterestManager) or every other
/// session in rotated order (radius/full, which have no mid band) — a one-tick halving
/// that lifts the instant a broadcast fits again.
/// </summary>
public class TickBroadcaster : ITickBroadcaster
{
    private readonly SessionManager _sessions;
    private readonly InterestManager _interest;
    private readonly UdpTransport? _udpTransport;
    private readonly ILogger<TickBroadcaster> _logger;
    private readonly TickMetrics? _metrics;
    private readonly int _sendWorkers;
    private readonly int _deadlineMs;
    private readonly bool _adaptiveDegrade;

    // Mutable, but only ever touched from the tick loop's sequential flow — BroadcastTickAsync
    // calls never overlap (TickLoop awaits each one before starting the next), so no locking.
    private double _lastBroadcastWallMs;

    public TickBroadcaster(
        SessionManager sessions,
        WorldState world,
        IConfiguration config,
        ILogger<TickBroadcaster> logger,
        UdpTransport? udpTransport = null,
        TickMetrics? metrics = null)
    {
        _sessions = sessions;
        var replicationOptions = ReplicationOptions.FromConfiguration(
            config, warning => logger.LogWarning("Replication config: {Warning}", warning));
        _interest = new InterestManager(world.SpatialGrid, replicationOptions);
        _udpTransport = udpTransport;
        _logger = logger;
        _metrics = metrics;
        _metrics?.SetReplicationPolicy(replicationOptions.PolicyName);

        _sendWorkers = SendFanOut.ResolveWorkerCount(replicationOptions.SendWorkers, Environment.ProcessorCount);
        _deadlineMs = replicationOptions.BroadcastDeadlineMs;
        _adaptiveDegrade = replicationOptions.AdaptiveDegrade;
        _metrics?.SetSendWorkers(_sendWorkers);

        _logger.LogInformation(
            "Replication policy={Policy} nearRadius={NearRadius} midRadius={MidRadius} midTickInterval={MidTickInterval} sendWorkers={SendWorkers} deadlineMs={DeadlineMs} adaptive={Adaptive}",
            replicationOptions.PolicyName, replicationOptions.NearRadius, replicationOptions.MidRadius, replicationOptions.MidTickInterval,
            _sendWorkers, _deadlineMs, _adaptiveDegrade);
    }

    public async Task BroadcastTickAsync(
        IReadOnlyDictionary<string, Player> players,
        IReadOnlyDictionary<string, Region> regions,
        IReadOnlyDictionary<string, NaturalResource> resources,
        HashSet<string> changedPlayerIds,
        HashSet<string> changedResourceIds,
        long tick,
        uint stateHash)
    {
        var wallStart = Stopwatch.GetTimestamp();

        // 1. Prepare Player data
        var playerData = new Dictionary<string, (string RegionId, Player Player)>();
        foreach (var playerId in changedPlayerIds)
        {
            if (!players.TryGetValue(playerId, out var player))
                continue;
            playerData[playerId] = (player.RegionId, player);
        }

        // 2. Prepare Resource data
        var resourceData = new Dictionary<string, (string RegionId, NaturalResource Resource)>();
        foreach (var resourceId in changedResourceIds)
        {
            if (!resources.TryGetValue(resourceId, out var resource))
                continue;
            resourceData[resourceId] = (resource.RegionId, resource);
        }

        // 3. Group all changes by region
        var regionIds = playerData.Values.Select(u => u.RegionId)
            .Concat(resourceData.Values.Select(r => r.RegionId))
            .Distinct();

        // Raw Stopwatch tick accumulators for the interest and send sub-phases. Under
        // SendWorkers>1 these are SUMS across worker chunks (each chunk accumulates locally,
        // no Interlocked needed — one accumulator instance per task, summed at Task.WhenAll
        // join). Broadcast WALL time (measured by TickLoop around the whole
        // BroadcastTickAsync call) is the tick-budget truth; the gap between that wall time
        // and this interest+send SUM is the parallelism-efficiency signal (wide gap = good
        // overlap across workers, near-equal = no effective parallelism).
        // "send" includes per-entity serialization (stackalloc/JSON) — socket writes dominate.
        long interestElapsed = 0, sendElapsed = 0;

        // Replication counters: how many player-update candidates InterestManager evaluated
        // per observer (regionPlayerChanges.Count) vs. how many it let through. Resource
        // broadcasts are out of policy scope (region-wide, always sent) and excluded here.
        long entitiesSent = 0, entitiesCulled = 0;

        // Deadline shedding: off (the default) means no CTS at all, so every send below runs
        // with CancellationToken.None — zero behavior change from before this feature. UDP
        // sends are sync (TrySendUdpEntityUpdate) and unaffected either way.
        var deadlineAborts = 0;
        using var deadlineCts = BroadcastDeadline.IsEnabled(_deadlineMs) ? new CancellationTokenSource() : null;
        deadlineCts?.CancelAfter(_deadlineMs);
        var deadlineToken = deadlineCts?.Token ?? CancellationToken.None;

        // Adaptive degrade: decided ONCE for the whole tick, from the PREVIOUS broadcast's
        // wall time — stateless beyond that one number (see class doc). Off (the default)
        // is always false here, so suppressMidBand/suppressAlternating below are never true
        // and every session gets its normal update — zero behavior change.
        var degraded = AdaptiveDegrade.ShouldDegrade(_adaptiveDegrade, _lastBroadcastWallMs);
        var suppressMidBand = degraded && _interest.Policy == ReplicationPolicy.Tiered;
        var suppressAlternating = degraded && _interest.Policy != ReplicationPolicy.Tiered;

        foreach (var regionId in regionIds)
        {
            var sessions = _sessions.GetByRegion(regionId).ToList();
            if (sessions.Count == 0) continue;

            var regionPlayerChanges = changedPlayerIds
                .Where(id => playerData.TryGetValue(id, out var u) && u.RegionId == regionId)
                .ToHashSet();

            var regionResourceChanges = changedResourceIds
                .Where(id => resourceData.TryGetValue(id, out var r) && r.RegionId == regionId)
                .ToHashSet();

            // Fairness rotation: always on, independent of SendWorkers. Rotates the starting
            // point through the session list each tick so early-connected sessions don't
            // absorb the whole send-phase tail every single tick — session order was never a
            // guaranteed fairness contract. No list copy: RotatedIndex maps a chunk-local
            // position back to the real index below.
            var offset = SendFanOut.RotateOffset(tick, sessions.Count);
            var chunks = SendFanOut.Chunk(sessions.Count, _sendWorkers);

            if (_sendWorkers <= 1)
            {
                // Default: exactly today's serial behavior, no Task/array overhead.
                var acc = new SendAccumulator();
                var (start, length) = chunks[0];
                await SendChunkAsync(
                    sessions, offset, start, length,
                    regionPlayerChanges, regionResourceChanges, playerData, resourceData, players,
                    tick, stateHash, deadlineToken, suppressMidBand, suppressAlternating, acc);
                interestElapsed += acc.InterestTicks;
                sendElapsed += acc.SendTicks;
                entitiesSent += acc.Sent;
                entitiesCulled += acc.Culled;
                deadlineAborts += acc.Aborts;
            }
            else
            {
                var accumulators = new SendAccumulator[chunks.Count];
                var tasks = new Task[chunks.Count];
                for (var i = 0; i < chunks.Count; i++)
                {
                    var (start, length) = chunks[i];
                    accumulators[i] = new SendAccumulator();
                    tasks[i] = SendChunkAsync(
                        sessions, offset, start, length,
                        regionPlayerChanges, regionResourceChanges, playerData, resourceData, players,
                        tick, stateHash, deadlineToken, suppressMidBand, suppressAlternating, accumulators[i]);
                }

                await Task.WhenAll(tasks);

                foreach (var acc in accumulators)
                {
                    interestElapsed += acc.InterestTicks;
                    sendElapsed += acc.SendTicks;
                    entitiesSent += acc.Sent;
                    entitiesCulled += acc.Culled;
                    deadlineAborts += acc.Aborts;
                }
            }
        }

        // Broadcast WALL time — measured here around the whole call, NOT summed across worker
        // chunks — is the tick-budget truth (the same interval TickLoop measures) and feeds
        // the NEXT tick's adaptive-degrade decision. Deliberately recorded even when
        // AdaptiveDegrade is off, so flipping it on mid-run has a real previous value to work
        // from immediately rather than a cold-start 0.
        _lastBroadcastWallMs = Stopwatch.GetElapsedTime(wallStart).TotalMilliseconds;

        _metrics?.RecordBroadcastPhases(
            Stopwatch.GetElapsedTime(0, interestElapsed).TotalMilliseconds,
            Stopwatch.GetElapsedTime(0, sendElapsed).TotalMilliseconds);
        _metrics?.RecordReplication(entitiesSent, entitiesCulled);
        _metrics?.RecordDeadlineAborts(deadlineAborts);
        _metrics?.RecordDegraded(degraded);

        // Deadline aborts get an immediate debug-level breadcrumb too (in addition to the
        // windowed count above) since an abort is a live socket getting force-closed — worth
        // seeing as it happens, not just in the ~5s rollup. Adaptive degrade deliberately logs
        // nothing per tick (see class doc) — the windowed degradedTicks count is enough.
        if (deadlineAborts > 0)
            _logger.LogDebug("Broadcast deadline shed {Aborts} session(s) this tick (tick {Tick})", deadlineAborts, tick);
    }

    /// <summary>
    /// Per-worker accumulator for one chunk's send loop. Each concurrent chunk task gets its
    /// own instance (see the fan-out loop above), so no Interlocked/locking is needed — the
    /// caller sums them after Task.WhenAll.
    /// </summary>
    private sealed class SendAccumulator
    {
        public long InterestTicks;
        public long SendTicks;
        public long Sent;
        public long Culled;
        public int Aborts;
    }

    /// <summary>
    /// Sends the full per-session body (player entity updates + resource updates) for a
    /// contiguous, rotated slice of <paramref name="sessions"/>. Safe to run concurrently
    /// against other chunks of the SAME sessions list because rotation+chunking guarantees a
    /// session appears in exactly one chunk per tick — WebSocket.SendAsync allows only one
    /// outstanding send per socket, but concurrent sends to DIFFERENT sockets are safe.
    /// </summary>
    private async Task SendChunkAsync(
        IReadOnlyList<GameSession> sessions,
        int rotationOffset,
        int chunkStart,
        int chunkLength,
        HashSet<string> regionPlayerChanges,
        HashSet<string> regionResourceChanges,
        Dictionary<string, (string RegionId, Player Player)> playerData,
        Dictionary<string, (string RegionId, NaturalResource Resource)> resourceData,
        IReadOnlyDictionary<string, Player> players,
        long tick,
        uint stateHash,
        CancellationToken deadlineToken,
        bool suppressMidBand,
        bool suppressAlternating,
        SendAccumulator acc)
    {
        for (var i = 0; i < chunkLength; i++)
        {
            var rotatedPos = chunkStart + i;
            var index = SendFanOut.RotatedIndex(rotatedPos, rotationOffset, sessions.Count);
            var session = sessions[index];

            if (session.Socket.State != WebSocketState.Open) continue;

            // Adaptive degrade halving for radius/full (no mid band to suppress instead):
            // skip this session's ENTIRE update for this tick. Position is in rotated order,
            // so which physical sessions get skipped shifts every degraded tick along with
            // the fairness rotation above — no session is skipped every time.
            if (suppressAlternating && AdaptiveDegrade.ShouldSkipAlternating(rotatedPos)) continue;

            var isBinary = session.Protocol == ProtocolMode.Binary;

            // --- Player Updates ---
            var tInterest = Stopwatch.GetTimestamp();
            var visiblePlayers = _interest.FilterForObserver(session.PlayerId, regionPlayerChanges, players, tick, suppressMidBand);
            acc.InterestTicks += Stopwatch.GetTimestamp() - tInterest;

            acc.Sent += visiblePlayers.Count;
            acc.Culled += regionPlayerChanges.Count - visiblePlayers.Count;

            var tSend = Stopwatch.GetTimestamp();
            var aborted = false;
            foreach (var playerId in visiblePlayers)
            {
                if (!playerData.TryGetValue(playerId, out var data)) continue;
                try
                {
                    if (isBinary)
                    {
                        if (!TrySendUdpEntityUpdate(session, playerId, data.Player, tick, stateHash))
                            await SendBinaryEntityUpdate(session, playerId, data.Player, tick, stateHash, deadlineToken);
                    }
                    else
                    {
                        await SendJsonEntityUpdate(session, playerId, data.Player, tick, stateHash, deadlineToken);
                    }
                }
                catch (OperationCanceledException) when (deadlineToken.IsCancellationRequested)
                {
                    // Broadcast deadline fired mid-send — a canceled WS send corrupts the
                    // stream, so this socket can never be used again this tick (or ever).
                    // Abort it, count it, and move on: the tick must end.
                    AbortSession(session, acc);
                    aborted = true;
                    break;
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to send player update"); }
            }

            // --- Resource Updates (Nature 2.0) ---
            // For now, simpler AoI for resources: everyone in region gets them if they change (trees are big)
            if (!aborted)
            {
                foreach (var resourceId in regionResourceChanges)
                {
                    if (!resourceData.TryGetValue(resourceId, out var data)) continue;
                    try
                    {
                        if (!isBinary) // Binary path for resources can be added later if needed
                        {
                            await SendJsonNaturalResourceUpdate(session, resourceId, data.Resource, tick, stateHash, deadlineToken);
                        }
                    }
                    catch (OperationCanceledException) when (deadlineToken.IsCancellationRequested)
                    {
                        AbortSession(session, acc);
                        break;
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to send resource update"); }
                }
            }
            acc.SendTicks += Stopwatch.GetTimestamp() - tSend;
        }
    }

    /// <summary>
    /// Broadcast deadline exceeded mid-send for this session: the WS stream is corrupted
    /// (SendAsync doesn't support a clean retry after cancellation), so abort the socket
    /// outright rather than try to keep using it. The existing session cleanup path (stale
    /// socket state checks) reaps it; the client reconnects.
    /// </summary>
    private void AbortSession(GameSession session, SendAccumulator acc)
    {
        try { session.Socket.Abort(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Abort() on an already-faulted socket — ignoring"); }
        acc.Aborts++;
        _logger.LogDebug("Aborted session {SessionId} — broadcast deadline exceeded mid-send", session.SessionId);
    }

    private bool TrySendUdpEntityUpdate(
        GameSession session, string entityId, Player player, long tick, uint stateHash)
    {
        if (_udpTransport == null || session.UdpEndpoint == null)
        {
            // No UDP channel bound — caller falls back to a WebSocket send,
            // which records the actual delivery path (binary_ws / json_ws).
            return false;
        }

        Span<byte> payloadBuf = stackalloc byte[128];
        var payloadLen = PayloadSerializers.WriteEntityUpdate(
            payloadBuf, entityId,
            player.Position, player.Velocity,
            player.Heading, player.LastInputSeq,
            (uint)tick, stateHash);

        Span<byte> frameBuf = stackalloc byte[BinaryEnvelope.HeaderBytes + payloadLen];
        BinaryEnvelope.Write(
            frameBuf,
            version: 1,
            MessageTypeId.EntityUpdate,
            DeliveryLane.Datagram,
            seq: 0,
            payloadBuf[..payloadLen]);

        // On success TrySend records RecordDelivery("udp"); on failure the caller
        // falls back to a WebSocket send which records its own delivery path.
        return _udpTransport.TrySend(session, frameBuf);
    }

    private static async Task SendBinaryEntityUpdate(
        GameSession session, string entityId, Player player, long tick, uint stateHash, CancellationToken ct)
    {
        // Serialize payload
        Span<byte> payloadBuf = stackalloc byte[128];
        var payloadLen = PayloadSerializers.WriteEntityUpdate(
            payloadBuf, entityId,
            player.Position, player.Velocity,
            player.Heading, player.LastInputSeq,
            (uint)tick, stateHash);

        // Wrap in binary envelope
        Span<byte> frameBuf = stackalloc byte[BinaryEnvelope.HeaderBytes + payloadLen];
        var frameLen = BinaryEnvelope.Write(
            frameBuf,
            version: 1,
            MessageTypeId.EntityUpdate,
            DeliveryLane.Datagram,
            seq: 0,
            payloadBuf[..payloadLen]);

        await session.Socket.SendAsync(
            frameBuf[..frameLen].ToArray(),
            WebSocketMessageType.Binary,
            true,
            ct);

        LumberjacksTelemetry.RecordDelivery("binary_ws");
    }

    private static async Task SendJsonEntityUpdate(
        GameSession session, string entityId, Player player, long tick, uint stateHash, CancellationToken ct)
    {
        var updateData = new
        {
            entity_id = entityId,
            entity_type = "player",
            data = new Dictionary<string, object>
            {
                ["player_id"] = entityId,
                ["position"] = new { x = player.Position.X, y = player.Position.Y, z = player.Position.Z },
                ["velocity"] = new { x = player.Velocity.X, y = player.Velocity.Y, z = player.Velocity.Z },
                ["heading"] = player.Heading,
                ["last_input_seq"] = player.LastInputSeq,
            },
            tick,
            state_hash = stateHash,
        };

        var env = EnvelopeFactory.Create(MessageType.EntityUpdate, updateData);
        var json = EnvelopeFactory.Serialize(env);
        await session.Socket.SendAsync(
            Encoding.UTF8.GetBytes(json),
            WebSocketMessageType.Text,
            true,
            ct);

        LumberjacksTelemetry.RecordDelivery("json_ws");
    }

    private static async Task SendJsonNaturalResourceUpdate(
        GameSession session, string resourceId, NaturalResource resource, long tick, uint stateHash, CancellationToken ct)
    {
        var updateData = new
        {
            entity_id = resourceId,
            entity_type = resource.Type, // e.g. "oak_tree"
            data = new Dictionary<string, object>
            {
                ["position"] = new { x = resource.Position.X, y = resource.Position.Y, z = resource.Position.Z },
                ["health"] = resource.Health,
                ["stump_health"] = resource.StumpHealth,
                ["regrowth_progress"] = resource.RegrowthProgress,
                ["lean_x"] = resource.LeanX,
                ["lean_z"] = resource.LeanZ,
                ["growth_history"] = resource.GrowthHistory
            },
            tick,
            state_hash = stateHash,
        };

        var env = EnvelopeFactory.Create(MessageType.EntityUpdate, updateData);
        var json = EnvelopeFactory.Serialize(env);
        await session.Socket.SendAsync(
            Encoding.UTF8.GetBytes(json),
            WebSocketMessageType.Text,
            true,
            ct);
    }
}
