# Spatial Interest Management

Compression reduces the cost of one update. Interest management limits how many
updates a client receives and how often it receives them.

## Spatial index

`SpatialGrid` partitions the XZ world plane into cells and tracks entity positions.
It supports insertion, movement, removal, and proximity queries without scanning the
entire world for every observer.

## Fidelity tiers

`InterestManager.FilterForObserver` applies distance and tick cadence:

| Tier | Distance | Cadence | Meaning |
|---|---:|---:|---|
| Near | 0–100 units | Every tick (20 Hz) | Immediate interaction fidelity |
| Mid | 100–300 units | Every fourth tick (5 Hz) | Context with interpolation |
| Far | Over 300 units | Omitted | A newer transient state may replace it later |

Filtering is evaluated per observer. A changed entity can therefore be full-rate for
one client, throttled for another, and absent for a third.

## What it filters

The current broadcaster applies `InterestManager` to changed player updates. Changed
natural resources use a simpler region-wide JSON path, with a code comment reserving
binary resource updates for later. The architecture supports broader interest
management, but the implementation should not be described as uniformly applying
all tiers to every entity type.

## Scaling argument

Without interest management, total world population determines each client's
downstream traffic. With a spatial filter, downstream cost is driven primarily by
nearby changed entities. That is the link between the 100-player goal and the
bandwidth target: binary encoding alone cannot cap a crowded global broadcast.

## Failure risks

- stale grid membership can make entities vanish or appear in the wrong tier;
- hard distance boundaries can expose update-rate transitions;
- five-Hz mid-band motion requires interpolation;
- new entity types can bypass the intended policy if added only to the broadcaster;
- reliable mutations need separate delivery rules from transient positions.

## Evidence

- `src/Game.Simulation/World/SpatialGrid.cs`
- `src/Game.Simulation/World/InterestManager.cs`
- `src/Game.Gateway/WebSocket/TickBroadcaster.cs`
- `tests/Game.Simulation.Tests/SpatialGridTests.cs`
- `tests/Game.Simulation.Tests/InterestManagerTests.cs`
- [ADR 0015](../adrs/0015-spatial-interest-management.md)
