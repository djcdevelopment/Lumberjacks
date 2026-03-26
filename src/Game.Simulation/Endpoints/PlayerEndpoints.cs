using Game.Contracts.Entities;
using Game.Contracts.Events;
using Game.Simulation.World;

namespace Game.Simulation.Endpoints;

public static class PlayerEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/players", (WorldState world) =>
            Results.Ok(world.Players.Values.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                position = new { x = p.Position.X, y = p.Position.Y, z = p.Position.Z },
                region_id = p.RegionId,
                connected = p.Connected,
            })));

        // Called by Gateway when a player sends join_region
        app.MapPost("/players/join", (JoinRequest request, WorldState world, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            if (!world.Regions.TryGetValue(request.RegionId, out var region))
                return Results.BadRequest(new { error = "Region not found" });

            var spawnPos = new Vec3(0, 0, 0);

            var player = new Player
            {
                Id = request.PlayerId,
                Name = $"Player-{request.PlayerId[..8]}",
                GuildId = request.GuildId,
                Position = spawnPos,
                RegionId = request.RegionId,
                Connected = true,
                ConnectedAt = DateTimeOffset.UtcNow,
            };

            world.Players[player.Id] = player;

            // Update region player count
            world.Regions[request.RegionId] = region with
            {
                PlayerCount = world.Players.Values.Count(p => p.RegionId == request.RegionId && p.Connected),
            };

            // Build world snapshot: all players + structures in this region
            var playerEntities = world.Players.Values
                .Where(p => p.RegionId == request.RegionId && p.Connected)
                .Select(p => new Dictionary<string, object>
                {
                    ["entity_id"] = p.Id,
                    ["entity_type"] = "player",
                    ["name"] = p.Name,
                    ["position"] = new { x = p.Position.X, y = p.Position.Y, z = p.Position.Z },
                    ["connected"] = p.Connected,
                })
                .ToList();

            var structureEntities = world.Structures.Values
                .Where(s => s.RegionId == request.RegionId)
                .Select(s => new Dictionary<string, object>
                {
                    ["entity_id"] = s.Id,
                    ["entity_type"] = "structure",
                    ["type"] = s.Type,
                    ["position"] = new { x = s.Position.X, y = s.Position.Y, z = s.Position.Z },
                    ["rotation"] = s.Rotation,
                    ["owner_id"] = s.OwnerId,
                })
                .ToList();

            var allEntities = playerEntities.Concat(structureEntities).ToList();

            // Fire-and-forget: emit player_connected event
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = httpFactory.CreateClient();
                    var eventLogUrl = config["ServiceUrls:EventLog"] ?? "http://localhost:4002";
                    await client.PostAsJsonAsync($"{eventLogUrl}/events", new
                    {
                        event_id = Guid.NewGuid().ToString(),
                        event_type = EventType.PlayerConnected,
                        occurred_at = DateTimeOffset.UtcNow,
                        world_id = "world-default",
                        region_id = request.RegionId,
                        actor_id = request.PlayerId,
                        source_service = "simulation",
                        schema_version = 1,
                        payload = new { spawn_position = new { x = spawnPos.X, y = spawnPos.Y, z = spawnPos.Z } },
                    });
                }
                catch { }
            });

            return Results.Ok(new
            {
                region_id = request.RegionId,
                player_id = player.Id,
                entities = allEntities,
                tick = 0,
            });
        });

        // Called by Gateway when a player sends player_move
        app.MapPost("/players/move", (MoveRequest request, WorldState world) =>
        {
            if (!world.Players.TryGetValue(request.PlayerId, out var player))
                return Results.BadRequest(new { error = "Player not in world" });

            var updated = player with
            {
                Position = request.Position,
            };
            world.Players[request.PlayerId] = updated;

            return Results.Ok(new
            {
                player_id = request.PlayerId,
                position = new { x = request.Position.X, y = request.Position.Y, z = request.Position.Z },
                velocity = new { x = request.Velocity.X, y = request.Velocity.Y, z = request.Velocity.Z },
                region_id = player.RegionId,
            });
        });

        // Called by Gateway when a player leaves region or disconnects
        app.MapPost("/players/leave", (LeaveRequest request, WorldState world, IHttpClientFactory httpFactory, IConfiguration config) =>
        {
            if (!world.Players.TryRemove(request.PlayerId, out var player))
                return Results.Ok(new { removed = false });

            // Update region player count
            if (world.Regions.TryGetValue(player.RegionId, out var region))
            {
                world.Regions[player.RegionId] = region with
                {
                    PlayerCount = world.Players.Values.Count(p => p.RegionId == player.RegionId && p.Connected),
                };
            }

            // Fire-and-forget: emit player_disconnected event
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = httpFactory.CreateClient();
                    var eventLogUrl = config["ServiceUrls:EventLog"] ?? "http://localhost:4002";
                    await client.PostAsJsonAsync($"{eventLogUrl}/events", new
                    {
                        event_id = Guid.NewGuid().ToString(),
                        event_type = EventType.PlayerDisconnected,
                        occurred_at = DateTimeOffset.UtcNow,
                        world_id = "world-default",
                        region_id = player.RegionId,
                        actor_id = request.PlayerId,
                        source_service = "simulation",
                        schema_version = 1,
                        payload = new { },
                    });
                }
                catch { }
            });

            return Results.Ok(new
            {
                removed = true,
                player_id = request.PlayerId,
                region_id = player.RegionId,
            });
        });
    }
}

public record JoinRequest
{
    public required string PlayerId { get; init; }
    public required string RegionId { get; init; }
    public string? GuildId { get; init; }
}

public record MoveRequest
{
    public required string PlayerId { get; init; }
    public required Vec3 Position { get; init; }
    public Vec3 Velocity { get; init; }
}

public record LeaveRequest
{
    public required string PlayerId { get; init; }
}
