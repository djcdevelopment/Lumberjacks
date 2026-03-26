using System.Collections.Concurrent;
using Game.Contracts.Entities;

namespace Game.Simulation.World;

public class WorldState
{
    public ConcurrentDictionary<string, Region> Regions { get; } = new();
    public ConcurrentDictionary<string, Player> Players { get; } = new();
    public ConcurrentDictionary<string, Structure> Structures { get; } = new();
    public ConcurrentDictionary<string, WorldItem> WorldItems { get; } = new();

    public WorldState()
    {
        // Seed with default spawn region matching TS service
        Regions["region-spawn"] = new Region
        {
            Id = "region-spawn",
            Name = "Spawn Island",
            BoundsMin = new Vec3(-500, -10, -500),
            BoundsMax = new Vec3(500, 200, 500),
            Active = true,
            PlayerCount = 0,
            TickRate = 20,
        };
    }
}
