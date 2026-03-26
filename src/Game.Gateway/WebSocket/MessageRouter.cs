using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Game.Contracts.Protocol;

namespace Game.Gateway.WebSocket;

public class MessageRouter
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly SessionManager _sessions;
    private readonly IConfiguration _config;
    private readonly ILogger<MessageRouter> _logger;

    public MessageRouter(
        IHttpClientFactory httpFactory,
        SessionManager sessions,
        IConfiguration config,
        ILogger<MessageRouter> logger)
    {
        _httpFactory = httpFactory;
        _sessions = sessions;
        _config = config;
        _logger = logger;
    }

    private string SimUrl => _config["ServiceUrls:Simulation"] ?? "http://localhost:4001";

    public async Task RouteAsync(GameSession session, Envelope envelope)
    {
        switch (envelope.Type)
        {
            case MessageType.JoinRegion:
                await HandleJoinRegionAsync(session, envelope);
                break;

            case MessageType.LeaveRegion:
                await HandleLeaveRegionAsync(session);
                break;

            case MessageType.PlayerMove:
                await HandlePlayerMoveAsync(session, envelope);
                break;

            case MessageType.PlaceStructure:
                await HandlePlaceStructureAsync(session, envelope);
                break;

            case MessageType.Interact:
                await HandleInteractAsync(session, envelope);
                break;

            default:
                _logger.LogDebug("No route for message type {Type}", envelope.Type);
                break;
        }
    }

    public async Task HandleDisconnectAsync(GameSession session)
    {
        await HandleLeaveRegionAsync(session);
    }

    private async Task HandleJoinRegionAsync(GameSession session, Envelope envelope)
    {
        var client = _httpFactory.CreateClient();
        var payload = envelope.Payload;

        var regionId = payload.GetProperty("region_id").GetString() ?? "region-spawn";
        var guildId = payload.TryGetProperty("guild_id", out var gidEl) ? gidEl.GetString() : null;

        // Store guild_id on the session so downstream actions can use it
        if (!string.IsNullOrEmpty(guildId))
            session.GuildId = guildId;

        var response = await client.PostAsJsonAsync($"{SimUrl}/players/join", new
        {
            player_id = session.PlayerId,
            region_id = regionId,
            guild_id = session.GuildId,
        });

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(body).RootElement;

            // Send world_snapshot to the joining player
            var entities = new List<Dictionary<string, object>>();
            if (result.TryGetProperty("entities", out var entArray))
            {
                foreach (var ent in entArray.EnumerateArray())
                {
                    entities.Add(JsonSerializer.Deserialize<Dictionary<string, object>>(ent.GetRawText())!);
                }
            }

            var snapshot = new
            {
                region_id = regionId,
                entities,
                tick = 0,
            };

            var snapshotEnvelope = EnvelopeFactory.Create(MessageType.WorldSnapshot, snapshot);
            await SendToSessionAsync(session, snapshotEnvelope);

            // Broadcast entity_update for the new player to everyone else in the region
            var playerUpdate = new
            {
                entity_id = session.PlayerId,
                entity_type = "player",
                data = new Dictionary<string, object>
                {
                    ["player_id"] = session.PlayerId,
                    ["name"] = $"Player-{session.PlayerId[..8]}",
                    ["position"] = new { x = 0, y = 0, z = 0 },
                    ["connected"] = true,
                },
                tick = 0,
            };

            var updateEnvelope = EnvelopeFactory.Create(MessageType.EntityUpdate, playerUpdate);
            await BroadcastAsync(session.SessionId, updateEnvelope);

            _logger.LogInformation("Player {PlayerId} joined {RegionId}", session.PlayerId, regionId);
        }
        else
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            await SendErrorAsync(session, "JOIN_FAILED", $"Join failed: {errorBody}");
        }
    }

    private async Task HandlePlayerMoveAsync(GameSession session, Envelope envelope)
    {
        var client = _httpFactory.CreateClient();
        var payload = envelope.Payload;

        var position = payload.GetProperty("position");
        var velocity = payload.TryGetProperty("velocity", out var vel) ? vel : default;

        var posObj = new
        {
            x = position.GetProperty("x").GetDouble(),
            y = position.GetProperty("y").GetDouble(),
            z = position.GetProperty("z").GetDouble(),
        };

        var velObj = new
        {
            x = velocity.ValueKind != JsonValueKind.Undefined ? velocity.GetProperty("x").GetDouble() : 0.0,
            y = velocity.ValueKind != JsonValueKind.Undefined ? velocity.GetProperty("y").GetDouble() : 0.0,
            z = velocity.ValueKind != JsonValueKind.Undefined ? velocity.GetProperty("z").GetDouble() : 0.0,
        };

        var response = await client.PostAsJsonAsync($"{SimUrl}/players/move", new
        {
            player_id = session.PlayerId,
            position = posObj,
            velocity = velObj,
        });

        if (response.IsSuccessStatusCode)
        {
            // Broadcast position to all other players
            var moveUpdate = new
            {
                entity_id = session.PlayerId,
                entity_type = "player",
                data = new Dictionary<string, object>
                {
                    ["player_id"] = session.PlayerId,
                    ["position"] = posObj,
                    ["velocity"] = velObj,
                },
                tick = 0,
            };

            var updateEnvelope = EnvelopeFactory.Create(MessageType.EntityUpdate, moveUpdate);
            await BroadcastAsync(session.SessionId, updateEnvelope);
        }
    }

    private async Task HandleLeaveRegionAsync(GameSession session)
    {
        var client = _httpFactory.CreateClient();

        var response = await client.PostAsJsonAsync($"{SimUrl}/players/leave", new
        {
            player_id = session.PlayerId,
        });

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(body).RootElement;

            if (result.TryGetProperty("removed", out var removed) && removed.GetBoolean())
            {
                // Broadcast entity_removed to all remaining players
                var removeEnvelope = EnvelopeFactory.Create(MessageType.EntityRemoved, new
                {
                    entity_id = session.PlayerId,
                    tick = 0,
                });
                await BroadcastAsync(session.SessionId, removeEnvelope);

                _logger.LogInformation("Player {PlayerId} left region", session.PlayerId);
            }
        }
    }

    private async Task HandlePlaceStructureAsync(GameSession session, Envelope envelope)
    {
        var client = _httpFactory.CreateClient();

        var payload = envelope.Payload;
        var structureType = payload.GetProperty("structure_type").GetString() ?? "unknown";
        var position = payload.GetProperty("position");
        var rotation = payload.TryGetProperty("rotation", out var rotEl) ? rotEl.GetDouble() : 0.0;

        var request = new
        {
            player_id = session.PlayerId,
            region_id = "region-spawn",
            structure_type = structureType,
            position = new
            {
                x = position.GetProperty("x").GetDouble(),
                y = position.GetProperty("y").GetDouble(),
                z = position.GetProperty("z").GetDouble(),
            },
            rotation,
            guild_id = session.GuildId,
        };

        var response = await client.PostAsJsonAsync($"{SimUrl}/structures/place", request);

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(body).RootElement;

            var entityUpdate = new
            {
                entity_id = result.GetProperty("structure_id").GetString(),
                entity_type = "structure",
                data = JsonSerializer.Deserialize<Dictionary<string, object>>(body),
                tick = 0,
            };

            var updateEnvelope = EnvelopeFactory.Create(MessageType.EntityUpdate, entityUpdate);
            await SendToSessionAsync(session, updateEnvelope);
            await BroadcastAsync(session.SessionId, updateEnvelope);

            _logger.LogInformation("Structure placed by {PlayerId}: {StructureId}",
                session.PlayerId, result.GetProperty("structure_id").GetString());
        }
        else
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            await SendErrorAsync(session, "PLACEMENT_FAILED", $"Structure placement failed: {errorBody}");
        }
    }

    private async Task HandleInteractAsync(GameSession session, Envelope envelope)
    {
        var client = _httpFactory.CreateClient();
        var payload = envelope.Payload;
        var action = payload.GetProperty("action").GetString();

        switch (action)
        {
            case "pickup":
            {
                var itemId = payload.GetProperty("item_id").GetString()!;
                var response = await client.PostAsJsonAsync($"{SimUrl}/items/pickup", new
                {
                    player_id = session.PlayerId,
                    item_id = itemId,
                });

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    var result = JsonDocument.Parse(body).RootElement;

                    // Tell the picker their inventory changed
                    var pickupEnv = EnvelopeFactory.Create(MessageType.EventEmitted, new
                    {
                        event_type = "item_picked_up",
                        item_id = itemId,
                        item_type = result.GetProperty("item_type").GetString(),
                        quantity = result.GetProperty("quantity").GetInt32(),
                    });
                    await SendToSessionAsync(session, pickupEnv);

                    // Broadcast entity_removed for the item to all players
                    var removeEnv = EnvelopeFactory.Create(MessageType.EntityRemoved, new
                    {
                        entity_id = itemId,
                        tick = 0,
                    });
                    await BroadcastAsync(null, removeEnv);

                    _logger.LogInformation("Player {PlayerId} picked up item {ItemId}", session.PlayerId, itemId);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    await SendErrorAsync(session, "PICKUP_FAILED", errorBody);
                }
                break;
            }

            case "store":
            {
                var containerId = payload.GetProperty("container_id").GetString()!;
                var itemType = payload.GetProperty("item_type").GetString()!;
                var quantity = payload.TryGetProperty("quantity", out var qtyEl) ? qtyEl.GetInt32() : 1;

                var response = await client.PostAsJsonAsync($"{SimUrl}/items/store", new
                {
                    player_id = session.PlayerId,
                    container_id = containerId,
                    item_type = itemType,
                    quantity,
                });

                if (response.IsSuccessStatusCode)
                {
                    var storeEnv = EnvelopeFactory.Create(MessageType.EventEmitted, new
                    {
                        event_type = "item_stored",
                        container_id = containerId,
                        item_type = itemType,
                        quantity,
                    });
                    await SendToSessionAsync(session, storeEnv);

                    _logger.LogInformation("Player {PlayerId} stored {ItemType} x{Qty} in {ContainerId}",
                        session.PlayerId, itemType, quantity, containerId);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    await SendErrorAsync(session, "STORE_FAILED", errorBody);
                }
                break;
            }

            default:
                _logger.LogDebug("Unknown interact action: {Action}", action);
                break;
        }
    }

    private async Task SendToSessionAsync(GameSession session, Envelope envelope)
    {
        if (session.Socket.State != WebSocketState.Open) return;

        var json = EnvelopeFactory.Serialize(envelope);
        await session.Socket.SendAsync(
            Encoding.UTF8.GetBytes(json),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);
    }

    private async Task SendErrorAsync(GameSession session, string code, string message)
    {
        var errEnvelope = EnvelopeFactory.Create(MessageType.Error, new ErrorMessage(code, message));
        await SendToSessionAsync(session, errEnvelope);
    }

    private async Task BroadcastAsync(string? excludeSessionId, Envelope envelope)
    {
        var json = EnvelopeFactory.Serialize(envelope);
        var bytes = Encoding.UTF8.GetBytes(json);

        foreach (var s in _sessions.GetAll())
        {
            if (excludeSessionId != null && s.SessionId == excludeSessionId) continue;
            if (s.Socket.State != WebSocketState.Open) continue;

            try
            {
                await s.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast to session {SessionId}", s.SessionId);
            }
        }
    }
}
