using System.Net.WebSockets;
using System.Text;
using Game.Contracts.Entities;
using Game.Contracts.Protocol;
using Game.Contracts.Protocol.Binary;
using Game.Simulation.World;

namespace Game.Gateway.WebSocket;

/// <summary>
/// Broadcasts authoritative tick state to connected clients.
/// Called by TickLoop (via ITickBroadcaster) after each simulation step for changed entities only.
///
/// Uses InterestManager for per-player AoI filtering:
///   Near (0–100u)  → every tick
///   Mid  (100–300u) → every 4th tick
///   Far  (300+u)    → skipped for position updates
///
/// Sends binary frames to binary-mode sessions, JSON to JSON-mode sessions.
/// </summary>
public class TickBroadcaster : ITickBroadcaster
{
    private readonly SessionManager _sessions;
    private readonly InterestManager _interest;
    private readonly UdpTransport? _udpTransport;
    private readonly ILogger<TickBroadcaster> _logger;

    public TickBroadcaster(SessionManager sessions, WorldState world, ILogger<TickBroadcaster> logger, UdpTransport? udpTransport = null)
    {
        _sessions = sessions;
        _interest = new InterestManager(world.SpatialGrid);
        _udpTransport = udpTransport;
        _logger = logger;
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

        foreach (var regionId in regionIds)
        {
            var sessions = _sessions.GetByRegion(regionId);
            if (sessions.Count == 0) continue;

            var regionPlayerChanges = changedPlayerIds
                .Where(id => playerData.TryGetValue(id, out var u) && u.RegionId == regionId)
                .ToHashSet();

            var regionResourceChanges = changedResourceIds
                .Where(id => resourceData.TryGetValue(id, out var r) && r.RegionId == regionId)
                .ToHashSet();

            foreach (var session in sessions)
            {
                if (session.Socket.State != WebSocketState.Open) continue;

                var isBinary = session.Protocol == ProtocolMode.Binary;

                // --- Player Updates ---
                var visiblePlayers = _interest.FilterForObserver(session.PlayerId, regionPlayerChanges, players, tick);
                foreach (var playerId in visiblePlayers)
                {
                    if (!playerData.TryGetValue(playerId, out var data)) continue;
                    try
                    {
                        if (isBinary)
                        {
                            if (!TrySendUdpEntityUpdate(session, playerId, data.Player, tick, stateHash))
                                await SendBinaryEntityUpdate(session, playerId, data.Player, tick, stateHash);
                        }
                        else
                        {
                            await SendJsonEntityUpdate(session, playerId, data.Player, tick, stateHash);
                        }
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to send player update"); }
                }

                // --- Resource Updates (Nature 2.0) ---
                // For now, simpler AoI for resources: everyone in region gets them if they change (trees are big)
                foreach (var resourceId in regionResourceChanges)
                {
                    if (!resourceData.TryGetValue(resourceId, out var data)) continue;
                    try
                    {
                        if (!isBinary) // Binary path for resources can be added later if needed
                        {
                            await SendJsonNaturalResourceUpdate(session, resourceId, data.Resource, tick, stateHash);
                        }
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to send resource update"); }
                }
            }
        }
    }

    private bool TrySendUdpEntityUpdate(
        GameSession session, string entityId, Player player, long tick, uint stateHash)
    {
        if (_udpTransport == null || session.UdpEndpoint == null)
            return false;

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

        return _udpTransport.TrySend(session, frameBuf);
    }

    private static async Task SendBinaryEntityUpdate(
        GameSession session, string entityId, Player player, long tick, uint stateHash)
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
            CancellationToken.None);
    }

    private static async Task SendJsonEntityUpdate(
        GameSession session, string entityId, Player player, long tick, uint stateHash)
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
            CancellationToken.None);
    }

    private static async Task SendJsonNaturalResourceUpdate(
        GameSession session, string resourceId, NaturalResource resource, long tick, uint stateHash)
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
            CancellationToken.None);
    }
}
