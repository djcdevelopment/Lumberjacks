# Current Focus

Use this file as the source of truth for what is actively in motion.
Keep the list short.

## Vertical Slice Status: PROVEN

The first vertical slice is complete and working end-to-end as of 2026-03-26.
A player can connect, join a region, place structures, trigger guild challenges,
have progression evaluated automatically, and see inventory flow — all through
server-authoritative .NET services with PostgreSQL persistence and canonical events.

## Network Refactor Status: ALL 5 PHASES COMPLETE (2026-03-26)

All phases of the network infrastructure refactoring plan (`implementation_plan.md`) are done:

- **Phase 1 (Binary Serialization):** BitWriter/BitReader, CompactVec3, BinaryEnvelope
- **Phase 2 (Input-Driven Simulation):** InputQueue, SimulationStep (physics), StateHasher, TickBroadcaster, PlayerHandler extraction, MessageRouter converted from HTTP to direct handler calls
- **Phase 3 (Spatial Interest Management):** SpatialGrid, InterestManager (near/mid/far AoI bands), TickBroadcaster rewritten for per-player AoI filtering
- **Phase 4 (Client Prediction — server-side):** Binary payload serializers (EntityUpdate ~33B, PlayerInput 5B), outbound binary framing, inbound binary fast-path — JSON payloads fully replaced for hot-path messages
- **Phase 5 (Dual-Channel Transport):** UdpTransport BackgroundService (port 4005), session UDP binding via token, TickBroadcaster sends datagram-lane via UDP when available, WebSocket fallback. (Validated 2026-03-27 with high-load script).

## Completed: Azure Deployment (2026-03-27)

Backend deployed to Azure Container Apps (eastus2). 4 services: Gateway (external), OperatorApi (external), EventLog (internal), Progression (internal). PostgreSQL Flexible Server. All smoke tests passing including multiplayer (10 players) and resume. See `docs/azure-deployment-runbook.md` for deploy/update workflow.

- Gateway: `wss://gateway.wittyplant-6c0ca715.eastus2.azurecontainerapps.io`
- OperatorApi: `https://operatorapi.wittyplant-6c0ca715.eastus2.azurecontainerapps.io`

## Active Workstream: Godot C# Client — "Nature 2.0" (2026-03-29)

**Location:** `clients/godot-cs/nature-2.0/`

**Status:** Slices 1-4 complete, primitive Slice 5. Full E2E pipeline proven.

**What works today:**
- Fresh Godot 4.6.1 mono project with C# builds (Alt+B)
- `Game.Contracts` referenced via ProjectReference (multi-targeted net8.0+net9.0)
- Connect screen UI with URL input
- SimulationClient autoload: WebSocket + binary protocol + thread-safe message queue
- GameState autoload: entity parsing from world_snapshot, coordinate mapping (ADR 0018), RegionProfile terrain data parsing
- Main scene switching: connect screen → world → ESC back
- World scene: placeholder entity spawning from server snapshot (trees visible as boxes)
- Server change: MessageRouter includes `region_profile` in world_snapshot

**What's next (Slices 5-6):**
- Player capsule mesh + camera + WASD binary input (PlayerController)
- Remote entity interpolation (ADR 0017)
- Tree entity scenes with growth_history visual variation
- Terrain heightmap mesh from RegionProfile altitude grid
- Structure placement (build mode)

**Previous client (`clients/godot/`) is archived** — created with non-mono Godot editor, GDScript/C# hybrid, never compiled C#. See `docs/retrospective-godot-cs-migration-2026-03-29.md`.

## Server-Side Changes (2026-03-29)

- `Game.Contracts.csproj`: Multi-targeted `net8.0;net9.0` for Godot compatibility
- `MessageRouter.cs`: Added `BuildRegionProfilePayload()` — includes altitude grid + trade winds in world_snapshot for client terrain generation

## Parked

- Community edge node alpha
- Advanced economy systems
- Combat zones and high-tick simulation
- Phase 4: Client prediction / reconciliation (server-side complete, client-side needs Godot)
- Content registry service
- Discord bridge service
- Auth / player identity
- Thesis Gold push (delta compression + client prediction)
