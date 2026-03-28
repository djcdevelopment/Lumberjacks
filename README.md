# Community Survival Platform

## Product Intent
Build a community-operated survival platform inspired by the charm of Valheim, but designed for 100+ player communities, Discord-native operations, event-driven progression, and robust operator tooling. The platform lets communities focus on memories and creativity instead of fragile mod stacks and manual bookkeeping.

## Overview
This repository contains the monorepo for the platform:
- `src/` — .NET 9 backend services (4 deployed: Gateway, EventLog, Progression, OperatorApi)
- `clients/godot/` — Godot 4.x thin client (vertical slice complete)
- `clients/admin-web/` — React + Vite operator console
- `infra/` — Docker, docker-compose, Azure deployment config
- `tests/` — 157 tests passing (106 Contracts + 51 Simulation)
- E2E scripts: `test-challenges.js`, `test-multiplayer.js`, `test-resume.js`, `test-input-broadcast.js`, `test-movement.js`, `test-vertical-slice.js`, `load-test-dual-channel.js` (validated with 50+ bots)
- **Dual-Channel Validation (2026-03-27):** Confirmed UDP offloading (99% of simulation traffic) with WebSocket fallback verified on Azure. [See report](docs/load-test-dual-channel-results.md).

A deterministic, authoritative backend (.NET 9 + PostgreSQL) emphasizing relevance management, data-driven content, and a thin-client architecture.

## Prerequisites
To build and run the platform locally, you must install:
- **.NET 9 SDK** (for the authoritative backend simulation & handlers)
- **Node.js v18+ & npm** (for the Admin Web UI, `concurrently` tooling, and smoke testing scripts)
- **Docker & Docker Compose** (for spinning up the local PostgreSQL database)

## Local Development & Smoke Testing
1. **Install dependencies:** `npm install`
2. **Boot the infrastructure (PostgreSQL on port 5433):** `npm run dev:infra` *(stop it via `npm run dev:stop-infra`)*
3. **Build the .NET services:** `npm run build:dotnet`
4. **Verify unit tests:** `npm run test:dotnet` *(Expected: 157+ passing tests, 0 failures).*
5. **Run the full stack natively:** `npm run dev`
   *This concurrently starts Gateway (port 4000 WS/HTTP + 4005 UDP, with in-process Simulation), EventLog, Progression, OperatorApi, and Admin UI.*
6. **End-to-End Smoke Testing:** With the stack running, execute `node scripts/test-vertical-slice.js` or `node scripts/test-multiplayer.js` in a new terminal to simulate clients connecting, dropping inputs, and validating server constraints.

> **Note:** PostgreSQL runs on port **5433** (not the default 5432) to avoid conflicts with other local Postgres instances. The `dev:infra` script starts only the Postgres container. To run the full stack in Docker instead, use `npm run dev:docker`.

### Stopping Services
```bash
# From PowerShell — kill any stale .NET processes before restarting:
taskkill /F /IM dotnet.exe /IM Game.Gateway.exe /IM Game.EventLog.exe /IM Game.Progression.exe /IM Game.OperatorApi.exe
npm run dev:stop-infra
```

## Architecture Decision Records (ADRs)
We document our core architecture decisions in `docs/adrs/`. Key ones include:
- [0001: Thin Client Platform](docs/adrs/0001-thin-client-platform.md)
- [0002: Edge Nodes Assist But Do Not Own Truth](docs/adrs/0002-edge-nodes-assist-but-do-not-own-truth.md)
- [0003: Multi-Lane Transport Strategy](docs/adrs/0003-websocket-transport.md)
- [0004: PostgreSQL Event Log](docs/adrs/0004-postgresql-event-log.md)
- [0005: .NET as Authoritative Backend Runtime](docs/adrs/0005-dotnet-authoritative-backend-runtime.md)
- [0012: Binary Payload Serialization](docs/adrs/0012-binary-payload-serialization.md)
- [0013: Dual-Channel UDP Transport](docs/adrs/0013-dual-channel-udp-transport.md)

## What We've Built So Far
- **Vertical Slice:** Proven end-to-end. Players can connect, join a region, place structures, trigger guild challenges, and update progression through server-authoritative .NET 9 services.
- **Network Refactor:** 5 phases fully completed!
  - Binary serialization (BitWriter/BitReader)
  - Input-driven simulation (InputQueue, TickBroadcaster)
  - Spatial Interest Management (AoI filtering on near/mid/far volume bands)
  - Server-side prediction & binary payload serializers (shrinking payload bandwidth 84-96%)
  - **Dual-Channel Transport Validation:** Successfully debugged and validated the dual-channel (WebSocket + UDP) transport. Fixed binary/text frame detection and bit-alignment issues in the Node.js load test client. [Read the full test results and charts here](docs/load-test-dual-channel-results.md).
- **Azure Deployment:** 4 services deployed to Azure Container Apps (eastus2). Gateway and OperatorApi are external; EventLog and Progression are internal. PostgreSQL Flexible Server. All smoke tests passing.
- **Godot Client:** Vertical slice complete. Connects via WebSocket, WASD movement, click-to-place structures, build mode, HUD overlay, remote player interpolation, reconnect with resume token.

## Retro Results
*(See [docs/simulation-retrospective-2026-03-26.md](docs/simulation-retrospective-2026-03-26.md) and [docs/retrospective-2026-03-27.md](docs/retrospective-2026-03-27.md) for full details)*
- **Testing:** 157 tests across `Contracts` and `Simulation` passing (0 failures, 0 errors).
- **Performance:** Massive bandwidth savings (`PlayerInput` reduced by 96%, `EntityUpdate` by 84%).
- **Thesis Compliance:** Scored 0.85 ("Era-Authentic") — see [audit](docs/thesis-compliance-audit-2026-03-27.md). Plan to reach 0.9+ via [delta compression and client prediction](docs/plan-thesis-gold.md).

## What's Planned Next
Currently focusing on:
1. **Thesis Gold (0.9+):** Delta compression (server-side bandwidth reduction) and client-side prediction with reconciliation. See [plan-thesis-gold.md](docs/plan-thesis-gold.md).
2. **Multi-User Testing:** Validating the Godot client with multiple concurrent players against the Azure deployment.

*(See `docs/current-focus.md` and `docs/90-day-roadmap.md` for deeper details on our upcoming objectives like advanced region management, Discord bot integration, and operator feedback loops).*

## External Testing & Validation Prompts
*The following prompts are used during external evaluations to ensure that generated code strictly adheres to the platform's core architecture and mission statement.*

### The Examination Prompts

**Prompt 1: Simulation Architecture Audit**
Identify the core networking loop. Determine if the simulation is "input-driven" (deterministic lockstep) or "state-synced." Locate where player inputs are timestamped and queued for execution at a specific future "Tick." If the system sends absolute positions instead of input deltas, flag it as a bandwidth-heavy architecture.

**Prompt 2: Channel Bifurcation Analysis**
Based on the network structures found, identify if there are two distinct logical pathways for data. Search for a "Deterministic Core" (high-priority, low-bandwidth input/state hashes) and a "Fidelity Enhancement" channel (low-priority, high-bandwidth assets/transforms). Document how the system distinguishes between "Game-Critical" and "Cosmetic" packets.

**Prompt 3: Interest Management & Spatial Partitioning**
Locate the logic responsible for filtering entity updates. Is there evidence of "Interest Circles," "Grid-based Partitioning," or "Spatial Hashing"? Determine if the server broadcasts all world updates to all clients, or if it utilizes a proximity-based "Area of Interest" (AoI) to maintain a sub-3.6 KB/s profile for the primary simulation stream.

**Prompt 4: Serialization & Bit-Packing Efficiency**
Examine the serialization layer. Search for "Bit-Packing," "Delta Compression," or custom "Bit-Streams" that avoid standard 32-bit alignment for small integers or enums. Analyze if the system sends a full state object or only the XOR/diff between the current state and the last acknowledged "Golden State" from the client.

**Prompt 5: Resilience & Progressive Enhancement Logic**
Evaluate the client-side reconciliation and prediction logic. How does the simulation behave if the "Fidelity" channel latency spikes? Look for evidence of "Dead Reckoning" or "Client-Side Prediction" that allows the game to remain playable on the "Deterministic Core" alone, even if the high-bandwidth visual updates are delayed or dropped.

### Scoring Matrix: Thesis Compliance
*I will evaluate the results using this $S$ (Score) matrix, where $S \in [0, 1]$:*

| Score | Category | Technical Justification |
| :--- | :--- | :--- |
| **0.0 - 0.2** | **"The Bloat"** | Uses high-level text protocols (JSON) or full-state synchronization. No evidence of bit-packing or input-driven determinism. |
| **0.3 - 0.5** | **"Modern Hybrid"** | Uses binary serialization (Protobuf/Flatbuffers) but lacks distinct Dual-Channel separation. High reliance on broadband for core simulation. |
| **0.6 - 0.8** | **"Era-Authentic"** | Strong implementation of Deterministic Lockstep and Delta Compression. Respects the 28.8k bandwidth constraint for vital gameplay. |
| **0.9 - 1.0** | **"Thesis Gold"** | Full Progressive Enhancement. The game is 100% playable on a 28.8k-style "Core" channel while using modern bandwidth only for visual fidelity. |

### Latest Audit Result
- [2026-03-27 - Score: 0.85 (Era-Authentic)](docs/thesis-compliance-audit-2026-03-27.md)
- [2026-03-26 - Score: 0.85 (Era-Authentic)](docs/simulation-audit-2026-03-26.md)

