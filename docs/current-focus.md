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
- **Phase 5 (Dual-Channel Transport):** UdpTransport BackgroundService (port 4005), session UDP binding via token, TickBroadcaster sends datagram-lane via UDP when available, WebSocket fallback

**What works today:**

Gateway (port 4000) is the unified host — WebSocket, tick loop, simulation, and broadcasting all run in-process:
- WebSocket connection → session_started with server-assigned player_id
- Session resume: disconnect/reconnect with `?resume=TOKEN`, world re-sync
- join_region with guild_id → world_snapshot with entities
- Input-driven simulation: `player_input` → InputQueue → 20Hz TickLoop → SimulationStep (direction/speed physics, friction, bounds clamping) → StateHasher → TickBroadcaster
- Per-player AoI filtering: near (0–100u, every tick), mid (100–300u, every 4th tick), far (300+u, dropped)
- SpatialGrid: grid-based spatial hash for fast radius queries (XZ-plane, Y ignored)
- MessageRouter calls PlayerHandler/PlaceStructureHandler/InventoryHandler directly (no HTTP self-calls)
- Binary payload serializers: EntityUpdate (~33 bytes vs ~200+ JSON), PlayerInput (5 bytes vs ~120 JSON)
- Dual-channel transport: UDP (port 4005) for datagram-lane, WebSocket for reliable-lane, automatic fallback
- place_structure → persisted to Postgres, event emitted, progression updated
- Movement validation: server-authoritative physics, speed clamping, region bounds clamping
- Dynamic regions: create/delete via API, persisted to Postgres, bounds validation
- Guild challenges: create → trigger match → progress increment → auto-complete → guild points
- Inventory: item spawn, pickup, store in container, quantity tracking
- Admin-web: tick diagnostics (live), region create/delete, service health, events, structures, players, guilds
- CORS configurable via `CORS_ORIGINS` env var (deployment-ready)
- Docker: multi-stage Dockerfile, docker-compose.yml, docker-compose.dev.yml
- Graceful startup: DB loader failures caught, runs with in-memory defaults if Postgres unavailable
- 157 tests passing (106 Contracts + 51 Simulation)
- E2E scripts: `test-challenges.js`, `test-multiplayer.js`, `test-resume.js`, `test-input-broadcast.js`, `test-movement.js`, `test-vertical-slice.js`

Simulation (port 4001) can also run standalone with NullTickBroadcaster for HTTP-only testing.

## Completed: Azure Deployment (2026-03-27)

Backend deployed to Azure Container Apps (eastus2). 4 services: Gateway (external), OperatorApi (external), EventLog (internal), Progression (internal). PostgreSQL Flexible Server. All smoke tests passing including multiplayer (10 players) and resume. See `docs/azure-deployment-runbook.md` for deploy/update workflow.

- Gateway: `wss://gateway.wittyplant-6c0ca715.eastus2.azurecontainerapps.io`
- OperatorApi: `https://operatorapi.wittyplant-6c0ca715.eastus2.azurecontainerapps.io`

## Active Workstream: Godot Client Vertical Slice

Objective:
Build the thinnest possible Godot 4.x client that connects to the backend, renders the world, and lets a player walk around and place structures. See `docs/godot-client-plan.md` for the full plan.

Why now:
Backend is deployed and proven (Azure + local). The next risk to retire is whether a real game client can consume the WebSocket protocol and render the authoritative world state with acceptable feel.

Exit criteria:
- Godot client connects via WebSocket, receives session_started
- Joins region, renders world from world_snapshot (ground, players, structures)
- WASD movement via player_input → server-authoritative position
- Other players visible and moving in real time (interpolated)
- Click-to-place structures
- Disconnect/reconnect with resume token
- Works against both local and Azure backends

## Parked

- Community edge node alpha
- Advanced economy systems
- Combat zones and high-tick simulation
- Phase 4: Client prediction / reconciliation (server-side complete, client-side needs Godot)
- Content registry service
- Discord bridge service
- Auth / player identity
