using Game.Simulation.World;

namespace Game.Simulation.Endpoints;

public static class RegionEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/regions", (WorldState world) =>
            Results.Ok(world.Regions.Values.Select(r => new
            {
                id = r.Id,
                name = r.Name,
                bounds_min = new { x = r.BoundsMin.X, y = r.BoundsMin.Y, z = r.BoundsMin.Z },
                bounds_max = new { x = r.BoundsMax.X, y = r.BoundsMax.Y, z = r.BoundsMax.Z },
                active = r.Active,
                player_count = r.PlayerCount,
                tick_rate = r.TickRate,
            })));

        app.MapGet("/regions/{id}", (string id, WorldState world) =>
        {
            if (!world.Regions.TryGetValue(id, out var region))
                return Results.NotFound(new { error = "Region not found" });

            return Results.Ok(new
            {
                id = region.Id,
                name = region.Name,
                bounds_min = new { x = region.BoundsMin.X, y = region.BoundsMin.Y, z = region.BoundsMin.Z },
                bounds_max = new { x = region.BoundsMax.X, y = region.BoundsMax.Y, z = region.BoundsMax.Z },
                active = region.Active,
                player_count = region.PlayerCount,
                tick_rate = region.TickRate,
            });
        });
    }
}
