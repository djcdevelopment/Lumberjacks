using Game.Contracts.Entities;

namespace Game.Simulation.World;

/// <summary>
/// Per-player Area of Interest (AoI) filtering.
/// Determines which entity updates each player should receive, per the active
/// <see cref="ReplicationPolicy"/> (see <see cref="ReplicationOptions"/>):
///
///   Tiered (default) — Near (0–NearRadius) every tick, Mid (NearRadius–MidRadius)
///     every MidTickInterval-th tick, Far dropped. Reproduces the original
///     hardcoded 100/300/4 behavior when constructed with default options.
///   Full   — no filtering; every observer gets every changed entity every tick.
///   Radius — hard cutoff at NearRadius; inside every tick, outside dropped.
///
/// Reliable-lane messages (structure placed, entity removed, etc.) always go to the full region.
/// This class only filters datagram-lane tick broadcasts.
/// </summary>
public class InterestManager
{
    private readonly SpatialGrid _grid;
    private readonly ReplicationOptions _options;

    public InterestManager(SpatialGrid grid, ReplicationOptions? options = null)
    {
        _grid = grid;
        _options = options ?? new ReplicationOptions();
    }

    /// <summary>The active replication policy for this manager.</summary>
    public ReplicationPolicy Policy => _options.Policy;

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
        // Full replication: no interest filtering at all — short-circuit before any
        // grid lookup or distance math.
        if (_options.Policy == ReplicationPolicy.Full)
            return changedEntityIds;

        var observerPos = _grid.GetPosition(observerId);
        if (observerPos == null)
            return changedEntityIds; // Observer not in grid — fall back to sending everything

        var result = new HashSet<string>();
        var nearRadiusSq = _options.NearRadius * _options.NearRadius;
        var midRadiusSq = _options.MidRadius * _options.MidRadius;
        var useMidBand = _options.Policy == ReplicationPolicy.Tiered;
        var isMidTick = useMidBand && _options.MidTickInterval > 0 && tick % _options.MidTickInterval == 0;

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
            else if (useMidBand && distSq.Value <= midRadiusSq && isMidTick)
            {
                // Mid band (tiered policy only) — every Nth tick
                result.Add(entityId);
            }
            // Far band (tiered), or anything beyond NearRadius (radius policy) — skip
        }

        return result;
    }
}
