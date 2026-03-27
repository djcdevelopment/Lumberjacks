using Game.Simulation.World;

namespace Game.Simulation.Endpoints;

public static class TickEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/tick", (WorldState world) =>
        {
            var uptime = DateTimeOffset.UtcNow - world.StartedAt;

            return Results.Ok(new
            {
                current_tick = world.CurrentTick,
                tick_rate_hz = 20,
                uptime_seconds = (int)uptime.TotalSeconds,
                total_players = world.Players.Count,
                connected_players = world.Players.Values.Count(p => p.Connected),
                regions = world.Regions.Values.Select(r => new
                {
                    id = r.Id,
                    player_count = r.PlayerCount,
                    active = r.Active,
                }),
            });
        });
    }
}
