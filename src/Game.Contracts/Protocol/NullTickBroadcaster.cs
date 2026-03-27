using Game.Contracts.Entities;

namespace Game.Contracts.Protocol;

/// <summary>
/// No-op ITickBroadcaster for standalone Simulation (no WebSocket clients) and unit tests.
/// </summary>
public class NullTickBroadcaster : ITickBroadcaster
{
    public Task BroadcastTickAsync(
        IReadOnlyDictionary<string, Player> players,
        IReadOnlyDictionary<string, Region> regions,
        HashSet<string> changedPlayerIds,
        long tick,
        uint stateHash) => Task.CompletedTask;
}
