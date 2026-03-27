namespace Game.Contracts.Protocol;

/// <summary>
/// Abstraction for broadcasting authoritative tick state to connected clients.
/// Implemented in Gateway (which owns sessions/sockets), consumed by Simulation's TickLoop.
/// This interface breaks the Gateway↔Simulation circular dependency.
/// </summary>
public interface ITickBroadcaster
{
    /// <summary>
    /// Broadcast authoritative state updates for all changed players in this tick.
    /// </summary>
    Task BroadcastTickAsync(
        IReadOnlyDictionary<string, Game.Contracts.Entities.Player> players,
        IReadOnlyDictionary<string, Game.Contracts.Entities.Region> regions,
        HashSet<string> changedPlayerIds,
        long tick,
        uint stateHash);
}
