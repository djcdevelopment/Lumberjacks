using Game.Contracts.Entities;
using Game.Simulation.Handlers;
using Game.Simulation.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Game.Simulation.Tests;

public class PlayerHandlerTests
{
    private static (PlayerHandler handler, WorldState world) CreateHandler()
    {
        var world = new WorldState();
        // PlayerHandler needs IHttpClientFactory and IConfiguration for fire-and-forget events.
        // For tests we use a stub HttpClientFactory and empty config — events will silently fail, which is fine.
        var config = new ConfigurationBuilder().Build();
        var handler = new PlayerHandler(
            world,
            new StubHttpClientFactory(),
            config,
            NullLogger<PlayerHandler>.Instance);
        return (handler, world);
    }

    [Fact]
    public void JoinAddsPlayerToWorld()
    {
        var (handler, world) = CreateHandler();

        var result = handler.Join(new JoinRequest
        {
            PlayerId = "player-1",
            RegionId = "region-spawn",
        });

        Assert.True(result.Success);
        Assert.True(world.Players.ContainsKey("player-1"));
        Assert.Equal("region-spawn", world.Players["player-1"].RegionId);
        Assert.True(world.Players["player-1"].Connected);
    }

    [Fact]
    public void JoinReturnsEntitiesSnapshot()
    {
        var (handler, world) = CreateHandler();

        var result = handler.Join(new JoinRequest
        {
            PlayerId = "player-1",
            RegionId = "region-spawn",
        });

        Assert.NotNull(result.Entities);
        Assert.True(result.Entities!.Count > 0, "Should return at least the joining player");
    }

    [Fact]
    public void JoinFailsForInvalidRegion()
    {
        var (handler, _) = CreateHandler();

        var result = handler.Join(new JoinRequest
        {
            PlayerId = "player-1",
            RegionId = "nonexistent-region",
        });

        Assert.False(result.Success);
        Assert.Equal("Region not found", result.Error);
    }

    [Fact]
    public void LeaveRemovesPlayerFromWorld()
    {
        var (handler, world) = CreateHandler();

        handler.Join(new JoinRequest { PlayerId = "player-1", RegionId = "region-spawn" });
        var result = handler.Leave(new LeaveRequest { PlayerId = "player-1" });

        Assert.True(result.Removed);
        Assert.False(world.Players.ContainsKey("player-1"));
    }

    [Fact]
    public void LeaveNonexistentPlayerReturnsNotRemoved()
    {
        var (handler, _) = CreateHandler();

        var result = handler.Leave(new LeaveRequest { PlayerId = "ghost" });

        Assert.False(result.Removed);
    }

    [Fact]
    public void MoveUpdatesPosition()
    {
        var (handler, world) = CreateHandler();
        handler.Join(new JoinRequest { PlayerId = "player-1", RegionId = "region-spawn" });

        var result = handler.Move(new MoveRequest
        {
            PlayerId = "player-1",
            Position = new Vec3(10, 0, 20),
        });

        Assert.True(result.Success);
        Assert.False(result.Corrected);
        Assert.Equal(10, world.Players["player-1"].Position.X);
        Assert.Equal(20, world.Players["player-1"].Position.Z);
    }

    [Fact]
    public void MoveClampsToRegionBounds()
    {
        var (handler, world) = CreateHandler();
        handler.Join(new JoinRequest { PlayerId = "player-1", RegionId = "region-spawn" });

        // Place player near the edge by directly setting position
        world.Players["player-1"] = world.Players["player-1"] with { Position = new Vec3(490, 0, 0) };

        // Now try to move past the boundary (within 50-unit max distance)
        var result = handler.Move(new MoveRequest
        {
            PlayerId = "player-1",
            Position = new Vec3(520, 0, 0), // 30 units from current pos, but past 500 boundary
        });

        Assert.True(result.Success);
        Assert.True(result.Corrected);
        Assert.Equal(500, world.Players["player-1"].Position.X);
    }

    [Fact]
    public void MoveClampsMaxDistance()
    {
        var (handler, world) = CreateHandler();
        handler.Join(new JoinRequest { PlayerId = "player-1", RegionId = "region-spawn" });

        // Player at origin, try to move 100 units (max is 50)
        var result = handler.Move(new MoveRequest
        {
            PlayerId = "player-1",
            Position = new Vec3(100, 0, 0),
        });

        Assert.True(result.Success);
        Assert.True(result.Corrected);
        Assert.Equal(50, world.Players["player-1"].Position.X, precision: 1);
    }

    [Fact]
    public void MoveFailsForUnknownPlayer()
    {
        var (handler, _) = CreateHandler();

        var result = handler.Move(new MoveRequest
        {
            PlayerId = "ghost",
            Position = new Vec3(10, 0, 20),
        });

        Assert.False(result.Success);
    }

    [Fact]
    public void JoinUpdatesRegionPlayerCount()
    {
        var (handler, world) = CreateHandler();

        handler.Join(new JoinRequest { PlayerId = "player-01", RegionId = "region-spawn" });
        handler.Join(new JoinRequest { PlayerId = "player-02", RegionId = "region-spawn" });

        Assert.Equal(2, world.Regions["region-spawn"].PlayerCount);
    }

    [Fact]
    public void LeaveUpdatesRegionPlayerCount()
    {
        var (handler, world) = CreateHandler();

        handler.Join(new JoinRequest { PlayerId = "player-01", RegionId = "region-spawn" });
        handler.Join(new JoinRequest { PlayerId = "player-02", RegionId = "region-spawn" });
        handler.Leave(new LeaveRequest { PlayerId = "player-01" });

        Assert.Equal(1, world.Regions["region-spawn"].PlayerCount);
    }

    /// <summary>Stub IHttpClientFactory that returns a default HttpClient (fire-and-forget calls will fail silently).</summary>
    private class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
