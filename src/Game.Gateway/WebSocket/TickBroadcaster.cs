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
        HashSet<string> changedPlayerIds,
        long tick,
        uint stateHash)
    {
        // Pre-build per-player data needed for both JSON and binary paths
        var playerData = new Dictionary<string, (string RegionId, Player Player)>();

        foreach (var playerId in changedPlayerIds)
        {
            if (!players.TryGetValue(playerId, out var player))
                continue;
            playerData[playerId] = (player.RegionId, player);
        }

        // Group changed players by region (for session lookup)
        var regionIds = playerData.Values.Select(u => u.RegionId).Distinct();

        foreach (var regionId in regionIds)
        {
            var sessions = _sessions.GetByRegion(regionId);
            if (sessions.Count == 0) continue;

            // Changed players in this region
            var regionChanges = changedPlayerIds
                .Where(id => playerData.TryGetValue(id, out var u) && u.RegionId == regionId)
                .ToHashSet();

            foreach (var session in sessions)
            {
                if (session.Socket.State != WebSocketState.Open) continue;

                // AoI filter: which changed entities does this observer care about?
                var visible = _interest.FilterForObserver(
                    session.PlayerId, regionChanges, players, tick);

                var isBinary = session.Protocol == ProtocolMode.Binary;

                foreach (var entityId in visible)
                {
                    if (!playerData.TryGetValue(entityId, out var data))
                        continue;

                    try
                    {
                        if (isBinary)
                        {
                            // Try UDP first (datagram lane), fall back to WebSocket binary
                            if (!TrySendUdpEntityUpdate(session, entityId, data.Player, tick, stateHash))
                            {
                                await SendBinaryEntityUpdate(session, entityId, data.Player, tick, stateHash);
                            }
                        }
                        else
                        {
                            await SendJsonEntityUpdate(session, entityId, data.Player, tick, stateHash);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send tick update to session {SessionId}", session.SessionId);
                    }
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
}
