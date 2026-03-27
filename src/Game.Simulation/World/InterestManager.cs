using Game.Contracts.Entities;

namespace Game.Simulation.World;

/// <summary>
/// Per-player Area of Interest (AoI) filtering.
/// Determines which entity updates each player should receive based on distance.
///
/// Bands:
///   Near  (0–100 units)  → every tick
///   Mid   (100–300 units) → every 4th tick
///   Far   (300+ units)    → skipped for datagram-lane (position updates)
///
/// Reliable-lane messages (structure placed, entity removed, etc.) always go to the full region.
/// This class only filters datagram-lane tick broadcasts.
/// </summary>
public class InterestManager
{
    /// <summary>Near band radius — full-rate updates.</summary>
    public const double NearRadius = 100.0;

    /// <summary>Mid band radius — throttled updates.</summary>
    public const double MidRadius = 300.0;

    /// <summary>Tick interval for mid-band updates (every Nth tick).</summary>
    public const int MidBandTickInterval = 4;

    private readonly SpatialGrid _grid;

    public InterestManager(SpatialGrid grid)
    {
        _grid = grid;
    }

    /// <summary>
    /// Determine which changed entities a given observer should receive on this tick.
    /// </summary>
    /// <param name="observerId">The player receiving updates.</param>
    /// <param name="changedEntityIds">All entities that changed this tick.</param>
    /// <param name="players">Current player state (for position lookup).</param>
    /// <param name="tick">Current tick number (for mid-band throttling).</param>
    /// <returns>Filtered set of entity IDs the observer should receive.</returns>
    public HashSet<string> FilterForObserver(
        string observerId,
        HashSet<string> changedEntityIds,
        IReadOnlyDictionary<string, Player> players,
        long tick)
    {
        var observerPos = _grid.GetPosition(observerId);
        if (observerPos == null)
            return changedEntityIds; // Observer not in grid — fall back to sending everything

        var result = new HashSet<string>();
        var nearRadiusSq = NearRadius * NearRadius;
        var midRadiusSq = MidRadius * MidRadius;
        var isMidTick = tick % MidBandTickInterval == 0;

        foreach (var entityId in changedEntityIds)
        {
            if (entityId == observerId)
            {
                // Always send self-updates (for input seq echo / correction)
                result.Add(entityId);
                continue;
            }

            var distSq = _grid.DistanceSq(observerId, entityId);
            if (distSq == null)
            {
                // Entity not in grid — include it (safety fallback)
                result.Add(entityId);
                continue;
            }

            if (distSq.Value <= nearRadiusSq)
            {
                // Near band — every tick
                result.Add(entityId);
            }
            else if (distSq.Value <= midRadiusSq && isMidTick)
            {
                // Mid band — every Nth tick
                result.Add(entityId);
            }
            // Far band — skip (no position updates)
        }

        return result;
    }
}
