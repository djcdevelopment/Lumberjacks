using Game.Contracts.Entities;
using Game.Simulation.World;
using Xunit;

namespace Game.Simulation.Tests;

public class InterestManagerTests
{
    private static Player MakePlayer(string id, Vec3 position) => new()
    {
        Id = id,
        Name = "Test",
        Position = position,
        RegionId = "region-spawn",
        Connected = true,
    };

    private static (InterestManager manager, SpatialGrid grid) CreateManager()
    {
        var grid = new SpatialGrid(cellSize: 50);
        var manager = new InterestManager(grid);
        return (manager, grid);
    }

    [Fact]
    public void NearEntityIncludedEveryTick()
    {
        var (manager, grid) = CreateManager();
        grid.Update("observer", new Vec3(0, 0, 0));
        grid.Update("near", new Vec3(50, 0, 0)); // 50 units away — within near band (100)

        var players = new Dictionary<string, Player>
        {
            ["observer"] = MakePlayer("observer", new Vec3(0, 0, 0)),
            ["near"] = MakePlayer("near", new Vec3(50, 0, 0)),
        };

        var changed = new HashSet<string> { "near" };

        // Should be visible on any tick (not just mid-band ticks)
        var visible1 = manager.FilterForObserver("observer", changed, players, tick: 1);
        var visible2 = manager.FilterForObserver("observer", changed, players, tick: 2);
        var visible3 = manager.FilterForObserver("observer", changed, players, tick: 3);

        Assert.Contains("near", visible1);
        Assert.Contains("near", visible2);
        Assert.Contains("near", visible3);
    }

    [Fact]
    public void MidEntityIncludedOnlyEveryNthTick()
    {
        var (manager, grid) = CreateManager();
        grid.Update("observer", new Vec3(0, 0, 0));
        grid.Update("mid", new Vec3(200, 0, 0)); // 200 units — in mid band (100-300)

        var players = new Dictionary<string, Player>
        {
            ["observer"] = MakePlayer("observer", new Vec3(0, 0, 0)),
            ["mid"] = MakePlayer("mid", new Vec3(200, 0, 0)),
        };

        var changed = new HashSet<string> { "mid" };

        // Mid band only included on ticks divisible by MidBandTickInterval (4)
        var visibleTick1 = manager.FilterForObserver("observer", changed, players, tick: 1);
        var visibleTick4 = manager.FilterForObserver("observer", changed, players, tick: 4);

        Assert.DoesNotContain("mid", visibleTick1);
        Assert.Contains("mid", visibleTick4);
    }

    [Fact]
    public void FarEntityExcluded()
    {
        var (manager, grid) = CreateManager();
        grid.Update("observer", new Vec3(0, 0, 0));
        grid.Update("far", new Vec3(400, 0, 0)); // 400 units — beyond mid band (300)

        var players = new Dictionary<string, Player>
        {
            ["observer"] = MakePlayer("observer", new Vec3(0, 0, 0)),
            ["far"] = MakePlayer("far", new Vec3(400, 0, 0)),
        };

        var changed = new HashSet<string> { "far" };

        // Should never be included (even on mid-band ticks)
        var visibleTick4 = manager.FilterForObserver("observer", changed, players, tick: 4);
        Assert.DoesNotContain("far", visibleTick4);
    }

    [Fact]
    public void SelfAlwaysIncluded()
    {
        var (manager, grid) = CreateManager();
        grid.Update("observer", new Vec3(0, 0, 0));

        var players = new Dictionary<string, Player>
        {
            ["observer"] = MakePlayer("observer", new Vec3(0, 0, 0)),
        };

        var changed = new HashSet<string> { "observer" };

        var visible = manager.FilterForObserver("observer", changed, players, tick: 1);
        Assert.Contains("observer", visible);
    }

    [Fact]
    public void ObserverNotInGridFallsBackToAll()
    {
        var (manager, grid) = CreateManager();
        // Don't add "ghost" to grid
        grid.Update("entity", new Vec3(0, 0, 0));

        var players = new Dictionary<string, Player>
        {
            ["entity"] = MakePlayer("entity", new Vec3(0, 0, 0)),
        };

        var changed = new HashSet<string> { "entity" };

        var visible = manager.FilterForObserver("ghost", changed, players, tick: 1);
        Assert.Contains("entity", visible); // fallback — send everything
    }

    [Fact]
    public void MixedDistancesFilterCorrectly()
    {
        var (manager, grid) = CreateManager();
        grid.Update("observer", new Vec3(0, 0, 0));
        grid.Update("near", new Vec3(50, 0, 0));     // near band
        grid.Update("mid", new Vec3(200, 0, 0));      // mid band
        grid.Update("far", new Vec3(400, 0, 0));      // far band

        var players = new Dictionary<string, Player>
        {
            ["observer"] = MakePlayer("observer", new Vec3(0, 0, 0)),
            ["near"] = MakePlayer("near", new Vec3(50, 0, 0)),
            ["mid"] = MakePlayer("mid", new Vec3(200, 0, 0)),
            ["far"] = MakePlayer("far", new Vec3(400, 0, 0)),
        };

        var changed = new HashSet<string> { "near", "mid", "far" };

        // Non-mid tick: only near
        var tick1 = manager.FilterForObserver("observer", changed, players, tick: 1);
        Assert.Contains("near", tick1);
        Assert.DoesNotContain("mid", tick1);
        Assert.DoesNotContain("far", tick1);

        // Mid tick: near + mid
        var tick4 = manager.FilterForObserver("observer", changed, players, tick: 4);
        Assert.Contains("near", tick4);
        Assert.Contains("mid", tick4);
        Assert.DoesNotContain("far", tick4);
    }

    [Fact]
    public void ExactlyAtNearBoundaryIncluded()
    {
        var (manager, grid) = CreateManager();
        grid.Update("observer", new Vec3(0, 0, 0));
        grid.Update("edge", new Vec3(InterestManager.NearRadius, 0, 0)); // exactly at boundary

        var players = new Dictionary<string, Player>
        {
            ["observer"] = MakePlayer("observer", new Vec3(0, 0, 0)),
            ["edge"] = MakePlayer("edge", new Vec3(InterestManager.NearRadius, 0, 0)),
        };

        var changed = new HashSet<string> { "edge" };
        var visible = manager.FilterForObserver("observer", changed, players, tick: 1);
        Assert.Contains("edge", visible);
    }
}
