using Game.Contracts.Entities;
using Game.Contracts.Protocol;
using Game.Simulation.World;

namespace Game.Simulation.Tick;

/// <summary>
/// Executes one simulation step: drains input queue, applies physics,
/// updates world state. Called by TickLoop on every tick.
/// </summary>
public class SimulationStep
{
    /// <summary>Max player movement speed in units per tick (at 20Hz = 200 units/sec).</summary>
    public const double MaxSpeedPerTick = 10.0;

    /// <summary>Friction deceleration per tick when no input (units/tick²).</summary>
    public const double FrictionPerTick = 2.0;

    /// <summary>
    /// Process one tick: apply queued inputs → compute physics → return list of changed player IDs.
    /// </summary>
    public static HashSet<string> Execute(
        WorldState world,
        InputQueue inputQueue,
        long tick)
    {
        var changed = new HashSet<string>();

        // 1. Drain inputs for this tick
        var inputs = inputQueue.DrainForTick(tick);

        // 2. Apply inputs to players
        foreach (var (playerId, queuedInput) in inputs)
        {
            if (!world.Players.TryGetValue(playerId, out var player))
                continue;
            if (!player.Connected)
                continue;

            var input = queuedInput.Input;
            var speed = Math.Clamp(input.SpeedPercent, (byte)0, (byte)100) / 100.0 * MaxSpeedPerTick;

            // Convert direction byte (0-255) to radians
            var headingDeg = input.Direction / 255.0 * 360.0;
            var headingRad = headingDeg * Math.PI / 180.0;

            // Compute velocity from direction + speed
            var vx = Math.Sin(headingRad) * speed;
            var vz = Math.Cos(headingRad) * speed;
            var vy = 0.0; // Gravity/jumping can be added later

            var velocity = new Vec3(vx, vy, vz);

            // Compute new position
            var newPos = new Vec3(
                player.Position.X + vx,
                player.Position.Y + vy,
                player.Position.Z + vz);

            // Bounds clamping
            if (world.Regions.TryGetValue(player.RegionId, out var region))
            {
                newPos = new Vec3(
                    Math.Clamp(newPos.X, region.BoundsMin.X, region.BoundsMax.X),
                    Math.Clamp(newPos.Y, region.BoundsMin.Y, region.BoundsMax.Y),
                    Math.Clamp(newPos.Z, region.BoundsMin.Z, region.BoundsMax.Z));
            }

            world.Players[playerId] = player with
            {
                Position = newPos,
                Velocity = velocity,
                Heading = headingDeg,
                LastInputSeq = input.InputSeq,
                LastActivityAt = DateTimeOffset.UtcNow,
            };
            world.SpatialGrid.Update(playerId, newPos);

            changed.Add(playerId);
        }

        // 3. Apply friction to players who had NO input this tick
        foreach (var (playerId, player) in world.Players)
        {
            if (changed.Contains(playerId)) continue; // already updated
            if (!player.Connected) continue;
            if (player.Velocity.X == 0 && player.Velocity.Y == 0 && player.Velocity.Z == 0) continue;

            // Apply friction: decelerate toward zero
            var vel = player.Velocity;
            var speed = Math.Sqrt(vel.X * vel.X + vel.Y * vel.Y + vel.Z * vel.Z);

            if (speed <= FrictionPerTick)
            {
                // Fully stopped
                world.Players[playerId] = player with { Velocity = new Vec3(0, 0, 0) };
                changed.Add(playerId);
            }
            else
            {
                // Reduce speed by friction
                var scale = (speed - FrictionPerTick) / speed;
                var newVel = new Vec3(vel.X * scale, vel.Y * scale, vel.Z * scale);
                var newPos = new Vec3(
                    player.Position.X + newVel.X,
                    player.Position.Y + newVel.Y,
                    player.Position.Z + newVel.Z);

                // Bounds clamping
                if (world.Regions.TryGetValue(player.RegionId, out var region))
                {
                    newPos = new Vec3(
                        Math.Clamp(newPos.X, region.BoundsMin.X, region.BoundsMax.X),
                        Math.Clamp(newPos.Y, region.BoundsMin.Y, region.BoundsMax.Y),
                        Math.Clamp(newPos.Z, region.BoundsMin.Z, region.BoundsMax.Z));
                }

                world.Players[playerId] = player with
                {
                    Position = newPos,
                    Velocity = newVel,
                };
                world.SpatialGrid.Update(playerId, newPos);
                changed.Add(playerId);
            }
        }

        return changed;
    }
}
