# Current Focus

Use this file as the source of truth for what is actively in motion.
Keep the list short.

## Vertical Slice Status: PROVEN

The first vertical slice is complete and working end-to-end as of 2026-03-26.
A player can connect, join a region, place structures, trigger guild challenges,
have progression evaluated automatically, and see inventory flow — all through
server-authoritative .NET services with PostgreSQL persistence and canonical events.

## Network Refactor Status: ALL 5 PHASES COMPLETE (2026-03-26)

(See previous version of this file for full phase details.)

## Completed: Azure Deployment (2026-03-27)

Backend deployed to Azure Container Apps (eastus2). All smoke tests passing.

- Gateway: `wss://gateway.wittyplant-6c0ca715.eastus2.azurecontainerapps.io`
- OperatorApi: `https://operatorapi.wittyplant-6c0ca715.eastus2.azurecontainerapps.io`

## Active Workstream: Nature 2.0 Client (2026-03-29 → 2026-03-30)

**Location:** `clients/godot-cs/nature-2.0/`

**Status:** All 6 slices complete. Lab tooling built. World generation R&D active.

**What works today:**
- Godot 4.6.1 mono, C# builds, `Game.Contracts` referenced (net8.0+net9.0)
- Full E2E pipeline: connect → WebSocket → session → join → world_snapshot → entities
- Proven against both local and Azure (`wss://`) deployments
- Player capsule with WASD movement (server-authoritative, 20Hz binary input)
- Camera: WoW-style orbit (RMB), zoom (scroll), auto-run (LMB+RMB)
- Remote entity interpolation (ADR 0017)
- Trees: trunk+canopy meshes with visual variation from growth_history
- Terrain: heightmap from RegionProfile, smooth normals, slope/altitude shader
- Tree inspection: [F] Study mechanic shows age, wind, fire history
- Environment: volumetric fog, procedural sky, SSAO, ACES tonemap, fill light
- Reconnect overlay, ESC to disconnect

**Lab Tooling (local, no server):**
- **Atmosphere Lab** (`scenes/Lab.tscn`): 40x40 world, tree grove, campfire with smoke, tuning panel with collapsible slider sections for fog/lighting/terrain/trees
- **World Gen Lab** (`scenes/WorldGenLab.tscn`): 512x512 heightmap, hydraulic erosion (Sebastian Lague algorithm), flow accumulation rivers, orographic moisture, biome coloring. 5 visualization modes. Data-driven biome presets (Alpine, Rainforest, Desert, Rolling Hills, Wetlands) from 500-run parameter sweep
- **Parameter Sweep** (`scripts/Lab/ParameterSweep.cs`): batch terrain generation with randomized params → CSV output for analysis
- **TerrainSim** (`scripts/Lab/TerrainSim.cs`): standalone erosion library, no Godot dependency — portable to server

**Server-Side Changes (2026-03-29):**
- `Game.Contracts.csproj`: Multi-targeted `net8.0;net9.0` for Godot compatibility
- `MessageRouter.cs`: `region_profile` in world_snapshot (altitude grid + trade winds)
- Nature 2.0 systems: NaturalResource, RegionProfile, tree felling with lean vectors

## What's Next

1. **Port world gen to server** — TerrainSim algorithm → RegionProfileLoader with erosion
2. **Port camera/movement controls** back to main World scene
3. **Terrain shader** in main World (smooth normals + slope/altitude)
4. **Multi-region stitching** — 25x25 grid of regions with river continuity
5. **Character model** — replace capsule with low-poly humanoid
6. **Weather system** — wind visualization, rain particles, seasonal color shift

## Parked

- Community edge node alpha
- Advanced economy systems
- Combat zones and high-tick simulation
- Client prediction / reconciliation (server-side complete)
- Content registry service
- Discord bridge service
- Auth / player identity
- Thesis Gold push (delta compression + client prediction)
