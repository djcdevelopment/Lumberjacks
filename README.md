# Community Survival Platform

## Product Intent
Build a community-operated survival platform inspired by the charm of Valheim, but designed for 100+ player communities, Discord-native operations, event-driven progression, and robust operator tooling. The platform lets communities focus on memories and creativity instead of fragile mod stacks and manual bookkeeping.

## Overview
This repository contains the monorepo for the platform, scaled for a clean separation of concerns:
- `clients`, `services`, `shared` packages, `plugins`, `infra`, `tests`, and `docs`.
- A deterministic, authoritative backend (.NET + PostgreSQL) emphasizing relevance management, data-driven content, and a thin-client architecture.

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
- **Vertical Slice:** Proven end-to-end. Players can connect, join a region, place structures, trigger guild challenges, and update progression through server-authoritative .NET services.
- **Network Refactor:** 5 phases fully completed!
  - Binary serialization (BitWriter/BitReader)
  - Input-driven simulation (InputQueue, TickBroadcaster)
  - Spatial Interest Management (AoI filtering on near/mid/far volume bands)
  - Server-side prediction & binary payload serializers (shrinking payload bandwidth 84-96%)
  - Dual-Channel transport (WebSocket reliable lane + UDP datagram lane on port 4005)

## Retro Results (2026-03-26)
- The network infrastructure refactor was completed smoothly.
- **Testing:** 157 tests across `Contracts` and `Simulation` are passing perfectly (0 failures, 0 errors).
- **Performance:** Massive bandwidth savings achieved (`PlayerInput` reduced by 96%, `EntityUpdate` by 84%). 

## Lessons Learned
- **What Worked Well:** Incremental phasing pays off—each phase built cleanly on the previous without massive structural breaks. Adding an optional `UdpTransport` preserved backwards compatibility for existing JSON clients and tests. Fast-path binary message processing mapping to zero extra allocations on the hot path. Crypto-random UDP tokens effectively prevent session spoofing.
- **What Was Awkward:** Adding properties to `session_started` required anonymous objects to avoid breaking shared records; a proper session handshake message needs designing. We also lack a automated E2E test for the new UDP dual-channel path (currently WebSocket path is validated via JS scripts).

## What's Planned Next
Currently, we are focusing on two main active workstreams:
1. **Azure Deployment:** Deploying the Dockerized backend to Azure Container Apps and validating the movement and simulation with real users over the internet.
2. **Godot Client Prototype:** Building a thin Godot client to connect to the backend, handle the incoming `session_started` data, bind to UDP, send binary `player_input`, and render the authoritative `entity_update`.

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
- [2026-03-26 - Score: 0.85 (Era-Authentic / Thesis Gold)](docs/simulation-audit-2026-03-26.md)

