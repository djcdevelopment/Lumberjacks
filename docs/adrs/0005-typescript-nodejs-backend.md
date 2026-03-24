# ADR 0005: TypeScript on Node.js for Backend Services

## Status

Accepted

## Context

The platform needs a runtime for its backend services: gateway, simulation, event-log, progression, content-registry, operator-api, and discord-bridge. ADR 0001 established that the backend owns all authoritative truth, so this choice affects every service.

Key constraints:

- Solo founder operating across all roles — cognitive overhead matters
- Monorepo with npm workspaces already scaffolded
- Shared type contracts between services, admin-web, and eventually client SDKs
- Rapid iteration speed more valuable than raw compute performance at this stage
- Game client engine is deliberately deferred and engine-agnostic (ADR 0001)

The simulation service is the most performance-sensitive component (20Hz tick loop, entity processing, interest management), but the v0 scope is a single region with moderate density, not a production-scale distributed simulation.

## Decision

Use TypeScript on Node.js for all backend services.

- Strict TypeScript with shared `tsconfig.base.json`
- Zod for runtime validation at service boundaries (`packages/schemas`)
- `tsx` for development with hot reload
- Express for HTTP APIs, `ws` for WebSocket connections
- Shared type definitions flow from `packages/schemas` and `packages/protocol` into every service

## Consequences

Positive:
- one language across the entire monorepo (services, packages, admin-web, test harnesses, scripts)
- shared types between services eliminate contract drift
- fast iteration with `tsx watch` and no compile step during development
- large ecosystem for HTTP, WebSocket, PostgreSQL, Discord, and testing
- low barrier for contributors familiar with the JavaScript ecosystem
- admin-web (React) and backend share the same type definitions natively

Negative:
- single-threaded event loop limits CPU-bound simulation throughput
- garbage collection pauses can cause tick jitter under high entity counts
- no native advantage for the compute-heavy simulation path compared to Go, Rust, or C#
- if Unity is chosen as client engine, no shared-language benefit with client code (C#)

## Migration Path

The service architecture is designed so individual services can be rewritten independently:

1. **Simulation in Go or Rust**: If tick performance becomes a bottleneck, the simulation service can be rewritten behind its existing HTTP/WebSocket API contract. Other services remain TypeScript.
2. **Gateway in Go**: If WebSocket connection density exceeds Node.js limits (~10K concurrent connections), the gateway can be rewritten while keeping the same protocol envelope format.
3. **Gradual migration**: Services communicate via HTTP and events, not direct function calls. Any service can be replaced without touching others.

The shared type contracts in `packages/schemas` can generate types for other languages (Zod → JSON Schema → Go structs, C# classes, Rust types) when cross-language services are introduced.

## Alternatives Considered

- **C# / .NET**: Natural pairing if Unity is the client engine. Strong game server ecosystem. Rejected because the client engine choice is deferred, and C# would require restructuring the existing npm workspace scaffold and splitting the monorepo.
- **Go**: Excellent concurrency model, simple deployment, proven in game infrastructure. Rejected because it eliminates shared types with admin-web and adds a second language to a solo-founder workflow.
- **Rust**: Maximum simulation performance. Rejected because iteration speed matters more than throughput at MVP scale, and the learning curve is steep for a solo founder covering all roles.

## Follow-Up Work

- Benchmark simulation tick loop under 40-player entity load to establish performance baseline
- Profile garbage collection impact on tick consistency
- Evaluate worker_threads for CPU-bound simulation work if single-thread becomes a bottleneck
- Define JSON Schema export from Zod types for future cross-language contract generation
