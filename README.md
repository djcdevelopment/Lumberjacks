# Lumberjacks — Community Survival Platform

A multiplayer survival game platform built with .NET 9, Godot 4.6, and AI agents. Designed for 100+ player communities with server-authoritative physics, binary networking constrained to 28.8k dialup bandwidth, and enterprise-grade Azure deployment at ~$25/month.

**Open source.** Every architecture decision, test, load test result, physics simulation, and retrospective is in this repo.

> This project started as steward tooling for a 100+ player Valheim community. AI agents helped a .NET architect bridge into Java mod ecosystems, and the results led to building the game platform from scratch. The full story: [LinkedIn article](https://www.linkedin.com/feed/update/urn:li:activity:7445761633852993536/)

---

## Quick Start

### Prerequisites
- **.NET 9 SDK**
- **Node.js v18+** and npm
- **Docker & Docker Compose**
- **Godot 4.6.1 Mono** (for the client / physics labs)

### Run Locally
```bash
npm install
npm run dev:infra          # Start PostgreSQL on port 5433
npm run build:dotnet       # Build .NET services
npm run test:dotnet        # Run 157 unit tests (should all pass)
npm run dev                # Start full stack (Gateway + EventLog + Progression + OperatorApi + Admin UI)
```

### Smoke Test
```bash
node scripts/test-vertical-slice.js     # Single-player E2E
node scripts/test-multiplayer.js        # Multi-player E2E
```

### Load Test (50 bots, 30 seconds)
```bash
npm run test:load:50
```
Expected: 50/50 connected, ~150K+ UDP entity updates, 0 errors. [See results](docs/load-test-dual-channel-results.md).

### Stop Everything
```bash
npm run dev:stop-infra
# PowerShell: taskkill /F /IM dotnet.exe /IM Game.Gateway.exe
```

> **Port note:** PostgreSQL runs on **5433** (not 5432) to avoid conflicts. Gateway uses 4000 (TCP/WS) + 4005 (UDP).

---

## Architecture

Four .NET 9 microservices deployed on Azure Container Apps:

| Service | Port | Role | Scaling |
|---------|------|------|---------|
| **Gateway** | 4000 + 4005 | WebSocket/UDP transport, 20Hz simulation loop, session management | Always warm (stateful) |
| **EventLog** | 4002 | Append-only event store | Scale-to-zero |
| **Progression** | 4003 | Player/guild progress, challenge evaluation | Scale-to-zero |
| **OperatorApi** | 4004 | Admin dashboard, health aggregation, service proxy | Scale-to-zero |

**Shared libraries:** `Game.Contracts` (protocol, entities, zero deps), `Game.Persistence` (EF Core), `Game.ServiceDefaults` (health checks, CORS, JSON config).

**Database:** PostgreSQL 16 (Flexible Server on Azure, Docker locally).

**Total Azure cost:** ~$25/month (Container Apps + PostgreSQL B1ms + Basic ACR).

### Key Architecture Patterns
- **Server-authoritative:** Clients send 5-byte input (direction + speed + actions + seq). Server computes all physics.
- **Dual-channel transport:** UDP for transient data (entity updates), WebSocket for reliable data (session, structures). Three-tier degradation: UDP binary -> WS binary -> WS JSON.
- **Spatial interest management:** Grid-based AoI with Near (20Hz) / Mid (5Hz) / Far (dropped) tiers.
- **Binary serialization:** Custom bit-packing (84-96% bandwidth reduction). PlayerInput: 120B -> 5B. EntityUpdate: 200B -> 33B.
- **Event sourcing:** Append-only event log, progression materializes from events.

See [architecture principles](docs/architecture-principles.md) and [repo layout](docs/repo-layout.md) for deeper context.

---

## Physics Labs

The project includes standalone, interactive physics labs built in Godot. These validate simulation models in isolation before network integration — lab first, then wire it up.

### Built From Real Lumberjack Manuals

The physics in this game weren't invented — they were sourced from actual forestry field manuals and academic research. Rather than treating tree felling as a health bar that counts down, we modeled what real loggers do: read the lean, choose a notch type, control the hinge, and manage barber chair risk. The game mechanic is learning real techniques.

The USDA Forest Service manual taught us notch geometry and felling procedures. The Wisconsin DNR manual added bore cutting and barber chair risk criteria. Rod Cross's physics paper proved our pendulum swing model was wrong and gave us the driven circular arc model that produces realistic axe velocities. Pluta & Hryniewicz's cutting dynamics papers informed how energy transfers into wood.

Every physics parameter — Janka hardness by species, hinge strength via section modulus, penetration depth from kinetic energy — traces back to these sources. The reference material lives in [`tools/ideas/`](tools/ideas/) for anyone who wants to follow the same trail.

### Tree Felling Lab

**Location:** `clients/godot-cs/nature-2.0/scenes/TreeFellingLab.tscn`

A realistic tree felling simulator grounded in real forestry research:

| Source | What It Contributed |
|--------|-------------------|
| [USDA Forest Service — *An Ax to Grind*](tools/ideas/) | Notch types, back cut placement, hinge dimensions, felling procedures |
| [Wisconsin DNR Timber Felling Manual](tools/ideas/) | Bore cutting, barber chair risk criteria, open-face technique |
| [Rod Cross — Swing Physics (2009)](tools/ideas/) | Proved gravity is negligible mid-swing; centripetal force is 25x stronger |
| [Pluta & Hryniewicz — Cutting Dynamics](tools/ideas/cutting/) | Three forces in wood cutting: gravity, inertia, material reaction |

**What it simulates:**
- **Polar cross-section trunk model** — 36 angular sectors at 10-degree intervals, each storing remaining wood fraction. Handles any cut geometry with one unified model.
- **5 cut types** — conventional notch (45 deg), open-face notch (70 deg), bore cut, individual axe strikes, physics-driven strikes (Rod Cross model)
- **Hinge mechanics** — section modulus (sigma x w x t^2 / 6), progressive fiber stress, failure detection
- **Barber chair detection** — triggers when hinge < 8% DBH, back cut at/below notch floor, species prone to splitting, significant lean
- **5 fall phases** — Standing -> HingeBending -> FreeFall -> Ground (or BarberChair)
- **Species properties** — Oak, Pine, Ash, Birch with distinct density, hardness (Janka), and barber chair proneness

**Key files:**
- [`TreeFellingSim.cs`](clients/godot-cs/nature-2.0/scripts/Lab/TreeFellingSim.cs) — 710 lines, pure C#, zero Godot dependencies. Ports directly to server.
- [`TreeFellingLab.cs`](clients/godot-cs/nature-2.0/scripts/Lab/TreeFellingLab.cs) — 1,108 lines, Godot visualization with tuning panel and 5 presets.

**Network projection:** The full polar model (~2,880 bytes) compresses to `CompactTreeState` — **6 floats, 24 bytes**. Notch angle, notch depth, back cut depth, hinge width fraction, fall tilt, fall bearing. Fits within the 33-byte entity update budget ([ADR 0012](docs/adrs/0012-binary-payload-serialization.md)).

**Run it:** Open `clients/godot-cs/nature-2.0/` in Godot 4.6.1 Mono, load `scenes/TreeFellingLab.tscn`, run the scene.

**How to test:**
1. **Move around** — WASD to move player orb, RMB to orbit camera, scroll to zoom
2. **Try presets** — Tuning panel → Presets: Textbook Fell, Barber Chair, Hillside, Against Lean, Big Oak. Each demonstrates different real-world felling scenarios.
3. **Watch the HUD** — Fall phase, hinge dimensions, Cross physics values (head velocity, KE, centripetal force), and "Wire: 24 bytes" confirming network budget
4. **Tune parameters** — Species, DBH, lean, notch type/depth, back cut height, axe mass, swing radius, slope, wind
5. **Switch visualization** — Shaded, Cross-section, Force diagram, Stress map, Fall trajectory, Side profile

**What to look for:** Notch geometry matching manual descriptions, barber chair triggering under correct conditions, species-appropriate cutting difficulty, fall direction following notch face with lean/slope modifiers, and the 24-byte CompactTreeState proving the network architecture works.

**Design docs:** [ADR 0019](docs/adrs/0019-tree-felling-physics-lab-validated.md) | [Plan](docs/plan-tree-felling-lab.md) | [Physics Article](docs/article-tree-felling-physics.md) | [Swing Physics Article](docs/article-cross-swing-physics.md) | [Tech Debt](docs/tech-debt-tree-felling-2026-03-31.md)

### World Generation Lab

**Location:** `clients/godot-cs/nature-2.0/scenes/WorldGenLab.tscn`

Procedural terrain generation using hydraulic erosion simulation:

- **Hydraulic erosion** — droplet-based simulation (Sebastian Lague algorithm). Water carves channels, deposits sediment.
- **Wind-driven biomes** — wind direction affects moisture distribution, which drives biome placement (temperature x moisture grid).
- **5 data-driven presets** — Alpine, Rainforest, Desert, Rolling Hills, Wetlands. Discovered from a 500-run parameter sweep.
- **512x512 world scale** — generates in ~30 seconds with real-time tuning panel.
- **Visualization modes** — shaded, height, moisture, biome, erosion delta.

**Key files:**
- [`WorldGenLab.cs`](clients/godot-cs/nature-2.0/scripts/Lab/WorldGenLab.cs) — 665 lines, terrain lab with biome presets and parameter sweep tool.
- [`TerrainGenerator.cs`](clients/godot-cs/nature-2.0/scripts/Core/TerrainGenerator.cs) — mesh generation with slope/altitude shader.

**Run it:** Open `clients/godot-cs/nature-2.0/` in Godot 4.6.1 Mono, load `scenes/WorldGenLab.tscn`, run the scene.

**How to test:**
1. **Navigate** — WASD to move, RMB drag to orbit, scroll to zoom
2. **Generate** — Press R to regenerate terrain with a new seed
3. **Erode** — Press E to run 10K erosion iterations (repeat for more carved terrain)
4. **Try presets** — Tab to open tuning panel → Biome Presets → click Alpine, Rainforest, Desert, Rolling Hills, or Wetlands. Each applies data-driven parameters from the 500-run sweep and runs erosion automatically.
5. **Visualize** — Switch between 5 modes in the Display section: Shaded (0), Height (1), Moisture (2), Biome (3), Erosion Delta (4)
6. **Tune** — Adjust erosion rate, deposition, wind angle/strength, sea level. Most changes rebuild the mesh in real-time.

**What to look for:** Rivers carving through valleys, rain shadow effects on windward vs leeward slopes, biome distribution matching climate expectations, erosion delta showing where material was removed (red) vs deposited (blue).

**Design docs:** [WorldGen Plan](docs/plan-worldgen-lab.md) | [Parameter Sweep](docs/plan-worldgen-parameter-sweep.md) | [Terrain Rendering](docs/plan-terrain-rendering.md)

---

## Testing

**Philosophy:** Tests are the immune system for AI-assisted engineering. Every critical path is tested before features are built on top of it.

### Unit Tests (157 total)

| Suite | Tests | Coverage |
|-------|-------|----------|
| [Game.Contracts.Tests](tests/Game.Contracts.Tests/) | 106 | Binary envelope, BitWriter/BitReader, CompactVec3, message classification, payload serializers, UDP packet format |
| [Game.Simulation.Tests](tests/Game.Simulation.Tests/) | 51 | Input queue, spatial grid, simulation step, interest manager, state hasher, player handler |

```bash
npm run test:dotnet    # or: dotnet test tests/Game.Contracts.Tests
```

### End-to-End Scripts

| Script | What It Tests |
|--------|--------------|
| `test-vertical-slice.js` | Connect -> join -> snapshot -> move -> leave |
| `test-multiplayer.js` | N concurrent bots, entity broadcasting |
| `test-movement.js` | Direction/speed validation, region bounds |
| `test-input-broadcast.js` | Input propagation and acknowledgment |
| `test-challenges.js` | Event-based challenge mechanics |
| `test-resume.js` | Session recovery |
| `load-test-dual-channel.js` | 50-100 bots, dual-channel transport, bandwidth profiling |

### Load Test Results

Validated locally and on Azure. [Full results with metrics](docs/load-test-dual-channel-results.md).

| Environment | UDP Entity Updates | Errors | Channel Split |
|-------------|-------------------|--------|---------------|
| Local (50 bots, 30s) | 152,118 | 0 | 100% UDP |
| Azure (50 bots, 30s) | 0 (blocked) | 0 | 100% WS fallback |

Azure's load balancer silently blocked UDP. The system fell back to WebSocket binary automatically. Zero errors, zero disconnects, zero code changes.

---

## Network Refactoring

Five phases, each independently tested before integration. Took the system from 0.20 to 0.85 on the [thesis compliance matrix](docs/thesis-compliance-audit-2026-03-27.md).

| Phase | What | Result |
|-------|------|--------|
| 1. Binary Serialization | [BitWriter](src/Game.Contracts/Protocol/Binary/BitWriter.cs), [CompactVec3](src/Game.Contracts/Protocol/Binary/CompactVec3.cs), [BinaryEnvelope](src/Game.Contracts/Protocol/Binary/BinaryEnvelope.cs) | 6-byte envelope header (was ~100B JSON) |
| 2. Input-Driven Simulation | [InputQueue](src/Game.Simulation/Tick/InputQueue.cs), [SimulationStep](src/Game.Simulation/Tick/SimulationStep.cs), [StateHasher](src/Game.Simulation/Tick/StateHasher.cs) | Clients send 5B input, not positions |
| 3. Spatial Interest Management | [SpatialGrid](src/Game.Simulation/World/SpatialGrid.cs), [InterestManager](src/Game.Simulation/World/InterestManager.cs) | Near 20Hz / Mid 5Hz / Far dropped |
| 4. Payload Serializers | [PayloadSerializers](src/Game.Contracts/Protocol/Binary/PayloadSerializers.cs) | PlayerInput: 96% reduction. EntityUpdate: 84% |
| 5. Dual-Channel Transport | [UdpTransport](src/Game.Gateway/WebSocket/UdpTransport.cs), [TickBroadcaster](src/Game.Gateway/WebSocket/TickBroadcaster.cs) | UDP + WS with 3-tier degradation |

**Bandwidth profile (binary, 20Hz):**

| Scenario | Downstream |
|----------|-----------|
| Isolated player | 0.78 KB/s |
| Small group (5 near) | 4.7 KB/s |
| Crowded (10 near, 5 mid) | 8.8 KB/s |

---

## Godot Client (Nature 2.0)

**Location:** `clients/godot-cs/nature-2.0/`

C# Godot 4.6.1 client — thin rendering shell, no client-side physics.

**What works:**
- WebSocket connection with binary protocol
- WASD movement (server-authoritative)
- Procedural heightmap terrain with slope/altitude shader
- 297 trees with visual variation from growth_history hash
- WoW-style orbit camera
- Tree inspection mechanic ([E] Study)
- Scene switching (connect screen -> world -> back)
- Disconnect/reconnect overlay

**Requires:** Godot 4.6.1 **Mono** editor (not the standard build — C# needs .NET SDK integration). See [migration retrospective](docs/retrospective-godot-cs-migration-2026-03-29.md) for setup details.

---

## Architecture Decision Records

19 ADRs documenting every core decision, written before implementation:

| ADR | Decision |
|-----|----------|
| [0001](docs/adrs/0001-thin-client-platform.md) | Thin client, platform-owned authority |
| [0002](docs/adrs/0002-edge-nodes-assist-but-do-not-own-truth.md) | Edge nodes assist but don't own truth |
| [0003](docs/adrs/0003-websocket-transport.md) | Multi-lane transport strategy |
| [0004](docs/adrs/0004-postgresql-event-log.md) | PostgreSQL as event log |
| [0005](docs/adrs/0005-dotnet-authoritative-backend-runtime.md) | .NET as authoritative backend |
| [0006](docs/adrs/0006-godot-game-client.md) | Godot as game client |
| [0007](docs/adrs/0007-canonical-event-schema.md) | Canonical event schema |
| [0008](docs/adrs/0008-delivery-lane-classification.md) | Delivery lane classification |
| [0009](docs/adrs/0009-ef-core-query-layer.md) | EF Core without migrations |
| [0010](docs/adrs/0010-service-topology.md) | Monorepo service topology |
| [0011](docs/adrs/0011-graceful-degradation-combat-zones.md) | Graceful degradation & combat zones |
| [0012](docs/adrs/0012-binary-payload-serialization.md) | Binary payload serialization |
| [0013](docs/adrs/0013-dual-channel-udp-transport.md) | Dual-channel UDP transport |
| [0014](docs/adrs/0014-input-driven-deterministic-simulation.md) | Input-driven deterministic simulation |
| [0015](docs/adrs/0015-spatial-interest-management.md) | Spatial interest management |
| [0016](docs/adrs/0016-godot-client-json-protocol-debt.md) | JSON protocol debt |
| [0017](docs/adrs/0017-godot-client-interpolation-debt.md) | Interpolation debt |
| [0018](docs/adrs/0018-godot-client-coordinate-mapping.md) | Coordinate mapping |
| [0019](docs/adrs/0019-tree-felling-physics-lab-validated.md) | Tree felling physics (lab-validated) |

---

## Research & Analysis

| Document | What It Covers |
|----------|---------------|
| [Thesis Compliance Audit](docs/thesis-compliance-audit-2026-03-27.md) | Formal scoring against 5 examination prompts (0.85 "Era-Authentic") |
| [Path to Thesis Gold](docs/plan-thesis-gold.md) | Roadmap to 0.9+ via delta compression and client prediction |
| [Physics of Tree Felling](docs/article-tree-felling-physics.md) | Deep dive: USDA manuals -> polar cross-sections -> 24-byte network projection |
| [Swing Physics (Rod Cross)](docs/article-cross-swing-physics.md) | Why gravity doesn't drive the swing, and what that means for game design |
| [Load Test Results](docs/load-test-dual-channel-results.md) | 50-bot dual-channel validation, local vs Azure |
| [Godot Migration Retrospective](docs/retrospective-godot-cs-migration-2026-03-29.md) | What went wrong with the hybrid GDScript/C# approach and how Nature 2.0 succeeded |
| [32-Hour Retrospective](docs/retrospective-2026-03-27.md) | The full backend build story: 12,467 lines, 180 files, 13 commits |

---

## Deployment

**Azure Container Apps** with PostgreSQL Flexible Server. Full runbook: [azure-deployment-runbook.md](docs/azure-deployment-runbook.md).

Three of four services scale to zero. Gateway stays warm (stateful). Docker multi-stage build from single `Dockerfile` with per-service targets.

```bash
# Build and deploy (example for one service)
docker build --no-cache --target gateway -t game-gateway .
docker tag game-gateway $ACR/game-gateway:v$(date +%Y%m%d-%H%M%S)
docker push $ACR/game-gateway:$TAG
az containerapp update --resource-group game-rg --name gateway --image $ACR/game-gateway:$TAG
```

---

## Project Structure

```
src/
  Game.Gateway/          # WebSocket/UDP, simulation loop, session management
  Game.Simulation/       # Tick loop, physics, spatial grid, interest management
  Game.EventLog/         # Append-only event ingestion
  Game.Progression/      # Player/guild progress, challenges
  Game.OperatorApi/      # Admin dashboard backend
  Game.Contracts/        # Protocol, entities, binary serialization (zero deps)
  Game.Persistence/      # EF Core, database schema
  Game.ServiceDefaults/  # Health checks, CORS, shared config

clients/
  godot-cs/nature-2.0/   # Active Godot 4.6.1 C# client
    scripts/Lab/          # TreeFellingLab, WorldGenLab (standalone physics labs)
    scripts/Core/         # World, TerrainGenerator, GameState
    scripts/Networking/   # SimulationClient (WebSocket + binary)
    scripts/Player/       # PlayerController, CameraController
    scripts/UI/           # TreeInspector, ConnectScreen
  admin-web/              # React + Vite operator console

tests/
  Game.Contracts.Tests/   # 106 tests: serialization, protocol, bit-packing
  Game.Simulation.Tests/  # 51 tests: physics, spatial, input queuing

scripts/                  # E2E test scripts and load testing (Node.js)
infra/                    # Docker, docker-compose, Azure config
docs/                     # 48 documents: ADRs, plans, audits, retrospectives, articles
  adrs/                   # 19 architecture decision records
tools/ideas/              # Reference material: USDA manuals, physics papers, forestry PDFs
```

---

## Thesis Compliance

The platform is scored against a formal compliance matrix evaluating input-driven simulation, channel bifurcation, interest management, serialization efficiency, and progressive enhancement.

| Date | Score | Category |
|------|-------|----------|
| Pre-refactor | 0.20 | "The Bloat" |
| Post-refactor | **0.85** | "Era-Authentic" |
| Target | 0.90+ | "Thesis Gold" |

[Full audit](docs/thesis-compliance-audit-2026-03-27.md) | [Scoring matrix and examination prompts](#examination-prompts)

<details>
<summary><strong>Examination Prompts</strong></summary>

**Prompt 1: Simulation Architecture Audit** — Identify if simulation is input-driven or state-synced. Locate input timestamping and tick-aligned execution.

**Prompt 2: Channel Bifurcation** — Identify distinct pathways for "Deterministic Core" (inputs/hashes) vs "Fidelity Enhancement" (transforms/assets).

**Prompt 3: Interest Management** — Locate spatial partitioning. Determine if server uses proximity-based AoI to maintain sub-3.6 KB/s profile.

**Prompt 4: Serialization Efficiency** — Examine bit-packing, delta compression, custom bit-streams avoiding 32-bit alignment waste.

**Prompt 5: Resilience & Progressive Enhancement** — Evaluate behavior when fidelity channel degrades. Look for dead reckoning or client prediction.

| Score | Category | Meaning |
|-------|----------|---------|
| 0.0-0.2 | "The Bloat" | JSON, full-state sync, no bit-packing |
| 0.3-0.5 | "Modern Hybrid" | Binary but no dual-channel separation |
| 0.6-0.8 | "Era-Authentic" | Deterministic lockstep, respects 28.8k constraint |
| 0.9-1.0 | "Thesis Gold" | Full progressive enhancement, playable on 28.8k core channel |

</details>

---

*Built with .NET 9, Godot 4.6, Azure Container Apps, PostgreSQL, and AI agents. Every architectural decision documented. Every physics model sourced from real research. Every claim backed by tests and load test results.*
