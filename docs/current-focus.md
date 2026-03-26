# Current Focus

Use this file as the source of truth for what is actively in motion.
Keep the list short.

## Vertical Slice Status: PROVEN

The first vertical slice is complete and working end-to-end as of 2026-03-26.
A player can connect, join a region, place structures, trigger guild challenges,
have progression evaluated automatically, and see inventory flow — all through
server-authoritative .NET services with PostgreSQL persistence and canonical events.

**What works today (all 5 services on ports 4000-4004):**
- WebSocket connection → session_started with server-assigned player_id
- join_region with guild_id → world_snapshot with entities
- place_structure → persisted to Postgres, event emitted, progression updated
- Guild challenges: create → trigger match → progress increment → auto-complete → guild points awarded
- Inventory: item spawn, pickup, store in container, quantity tracking
- Admin-web (React/Vite) shows service status, regions, events, players
- E2E test script: `node scripts/test-challenges.js`

## Active Workstream 1: Multi-User Network Testing

Objective:
Prove the platform works with multiple concurrent users across real networks.

Why now:
The vertical slice proves the loop works for one player locally. The next risk
to retire is whether it holds up with real latency, concurrent state mutations,
and distributed players. This is the core value prop — 100+ player communities.

Exit criteria:
- 2+ simultaneous WebSocket sessions see each other's actions in real time
- Structure placement by player A appears for player B
- Challenge progress accumulates correctly across multiple players in the same guild
- Works across the internet, not just localhost

### Testing paths (pick one or both):

**Option A — Distribute a test client .exe to friends:**
- Build a minimal CLI or Godot test client that connects to a public endpoint
- Deploy backend services to Azure (App Service or Container Apps)
- Friends run the .exe, connect to the Azure endpoint, place structures together
- Validates real-world latency, NAT traversal (none needed — WebSocket over HTTPS)

**Option B — Azure load test with simulated players:**
- Deploy backend to Azure Container Apps (or single VM with all 5 services)
- Write a Node.js script that spawns N concurrent WebSocket clients
- Each client joins, places structures, verifies world_snapshot updates
- Validates concurrent state mutations, broadcast fan-out, DB contention

### Deployment prep needed:
- Dockerfiles for each service (or a single multi-service Dockerfile)
- docker-compose.yml for local multi-service testing
- Azure deployment config (Container Apps or App Service)
- Connection strings for Azure PostgreSQL
- CORS/WebSocket origin config for non-localhost

Next actions:
- Write Dockerfiles for the 5 .NET services
- Create docker-compose.yml (services + Postgres)
- Write a multi-player test script (N concurrent WebSocket clients)
- Test locally with multiple concurrent sessions first
- Then deploy to Azure and test across the internet

## Active Workstream 2: Godot Client Prototype

Objective:
Build the thinnest possible Godot client shell that connects to the backend.

Why now:
Once multi-user networking is proven, the next step is proving a real game client
can consume the WebSocket protocol and render the authoritative world state.

Exit criteria:
- Godot client connects via WebSocket, receives session_started
- Joins region, renders player positions from world_snapshot
- Sends place_structure, sees result appear in the world
- Other players' actions appear in real time

## Parked

- Community edge node alpha
- Distant settlement rendering / interest management
- Advanced economy systems
- Combat zones and high-tick simulation
- UDP/QUIC datagram lane (ADR 0003 — designed for, not yet implemented)
