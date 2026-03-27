# Repo Layout

Monorepo. The platform is too contract-heavy to scatter across unrelated repositories.

## Top-Level Layout

```text
/src                          # .NET 9 service projects (authoritative backend)
  /Game.Contracts             # Shared types: entities, protocol, events
  /Game.Persistence           # EF Core DbContext, entity configs (Npgsql)
  /Game.ServiceDefaults       # Health checks, CORS, JSON snake_case config
  /Game.Gateway               # Port 4000 — unified host: WebSocket, tick loop, simulation, broadcasting
  /Game.Simulation            # Port 4001 — standalone simulation (also used as library by Gateway)
  /Game.EventLog              # Port 4002 — append/query events via Postgres
  /Game.Progression           # Port 4003 — consume events, challenges, progress
  /Game.OperatorApi           # Port 4004 — proxies to services, admin endpoints
/tests
  /Game.Contracts.Tests       # Binary serialization, payload serializers, UDP packet format, envelope tests (106 tests)
  /Game.Simulation.Tests      # Simulation step, input queue, spatial grid, interest manager, player handler tests (51 tests)
/clients
  /admin-web                  # React + Vite operator console (TypeScript)
  /game-client                # Godot thin client (planned — ADR 0006)
/services                     # Legacy TS stubs (pre-migration scaffolding, not active)
/packages                     # Legacy TS stubs (pre-migration scaffolding, not active)
/infra
  /docker                     # Dockerfile, docker-compose.yml, docker-compose.dev.yml, init.sql
/scripts                      # Test and dev scripts
/docs                         # ADRs, architecture, roadmap
```

## Project Dependencies

```
Game.Contracts         (no deps — pure records, enums, constants)
    ↑
Game.Persistence       (→ Contracts, Npgsql.EF)
    ↑
Game.ServiceDefaults   (→ Contracts)
    ↑
All service projects   (→ Contracts, Persistence, ServiceDefaults)
```

## Module Intent

### Shared Libraries

`src/Game.Contracts`
- Shared types for all services: Vec3, CompactVec3, Player, Region, Structure, Guild, WorldItem.
- Protocol: Envelope, MessageType (string + byte ID), Messages, DeliveryLane classification.
- Binary protocol: BitWriter, BitReader, BinaryEnvelope (bit-packed binary wire format), PayloadSerializers (EntityUpdate, PlayerInput, EntityRemoved).
- Events: EventType constants (30 canonical types from docs/events.md).
- Interfaces: ITickBroadcaster, NullTickBroadcaster.

`src/Game.Persistence`
- EF Core DbContext mapping to existing Postgres tables.
- Entity configs with explicit column names (no migrations — tables managed via init.sql).
- Tables: events, player_progress, guild_progress, structures, world_items, player_inventories, containers, container_items, challenges, challenge_progress, regions.

`src/Game.ServiceDefaults`
- Shared setup for all services: CORS (configurable via `CORS_ORIGINS` env var), JSON snake_case, health checks, HttpClient registration.

### Services

`src/Game.Gateway` (port 4000 WebSocket/HTTP, port 4005 UDP)
- **Unified host**: runs WebSocket middleware, UDP transport, tick loop, simulation step, and broadcasting all in-process.
- WebSocket middleware: session lifecycle, resume with `?resume=TOKEN`, binary + JSON dual-mode.
- UdpTransport: UDP datagram channel (port 4005), session binding via token, fast-path player_input deserialization.
- MessageRouter: routes client messages to in-process handlers (PlayerHandler, PlaceStructureHandler, InventoryHandler) — no HTTP calls. Binary input fast-path skips JSON.
- SessionManager: tracks active/detached sessions, resume window (2min), UDP endpoint per session.
- TickLoop: 20Hz BackgroundService — drains InputQueue → SimulationStep → StateHasher → TickBroadcaster.
- TickBroadcaster: per-player AoI-filtered broadcasting using InterestManager. Sends via UDP for datagram-lane (binary sessions), WebSocket fallback.
- Graceful startup: DB loader failures caught, runs with in-memory defaults if Postgres unavailable.

`src/Game.Simulation` (port 4001)
- Authoritative world simulation library (also runs standalone for HTTP-only testing).
- WorldState: ConcurrentDictionary-backed state for regions, players, structures, items, plus SpatialGrid.
- Handlers: PlayerHandler (join/move/leave), PlaceStructureHandler, InventoryHandler — used by both Gateway's MessageRouter and Simulation's HTTP endpoints.
- Tick: InputQueue (per-player input buffer), SimulationStep (direction/speed physics, friction, bounds clamping), StateHasher (CRC32 determinism check).
- World: SpatialGrid (grid-based spatial hash, XZ-plane radius queries), InterestManager (near/mid/far AoI bands).
- Endpoints: thin Minimal API wrappers over handlers for HTTP access.
- Startup: RegionLoader + StructureLoader load persisted data from Postgres (graceful fallback).
- Standalone mode: uses NullTickBroadcaster (no broadcasting when running without Gateway).

`src/Game.EventLog` (port 4002)
- Append-only event stream: POST /events, GET /events with filters.
- Query layer over existing Postgres events table.

`src/Game.Progression` (port 4003)
- Consumes events via POST /process-event.
- ChallengeEngine: evaluates triggers, atomic progress updates (INSERT ON CONFLICT).
- Guild points, player ranks, challenge completion.

`src/Game.OperatorApi` (port 4004)
- Proxies GET/POST/DELETE to Simulation, EventLog, Progression.
- Serves admin-web via CORS.
- Tick diagnostics, region CRUD, guild/player progress queries.

### Clients

`clients/admin-web`
- React + Vite operator console.
- Tabs: Status (tick diagnostics, service health), Events, Regions (create/delete), Structures, Players, Guilds.
- Calls OperatorApi at /api/*.

`clients/game-client`
- Godot thin client (ADR 0006). Not yet built.
- Will connect via WebSocket, send player_input, render entity_update messages.

### Scripts

`scripts/`
- `test-multiplayer.js` — multi-player WebSocket join/move/broadcast test
- `test-input-broadcast.js` — 3-client input→tick→AoI broadcast pipeline test (15 assertions)
- `test-resume.js` — session disconnect/resume test
- `test-challenges.js` — guild challenge lifecycle test
- `test-vertical-slice.js` — end-to-end vertical slice test
- `test-movement.js` — movement validation test
- `start-dev.sh`, `start-all.ps1` — dev startup scripts
- `check-workspace.ps1` — workspace health check

### Not Yet Implemented

- `content-registry` — versioned content definitions (items, quests, rewards)
- `discord-bridge` — identity linking, role sync, announcements
- Plugin system (sdk-plugin, sdk-content)

## Repo Rules

- Every cross-service type lives in `Game.Contracts`.
- No client-only change can alter authoritative progression or inventory truth.
- Every new subsystem needs at least one ADR if it changes ownership boundaries or trust assumptions.
