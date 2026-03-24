# Proposed Repo Layout

Use a monorepo. The platform is too contract-heavy to scatter across unrelated repositories early.

## Top-Level Layout

```text
/clients
  /game-client
  /admin-web
/services
  /gateway
  /simulation
  /event-log
  /progression
  /content-registry
  /discord-bridge
  /operator-api
/packages
  /schemas
  /protocol
  /sdk-plugin
  /sdk-content
  /observability
  /dev-tools
/plugins
  /examples
  /core-community-pack
/infra
  /docker
  /k8s
  /terraform
/scripts
/docs
/tests
  /contracts
  /load
  /integration
```

## Module Intent

`clients/game-client`
- Thin engine client.
- Renders the world and handles local feel.
- Does not own authoritative gameplay rules.

`clients/admin-web`
- Staff console for content, events, players, guilds, and audits.

`services/gateway`
- Entry point for auth, session bootstrap, and service routing.

`services/simulation`
- Authoritative world simulation.
- Region ownership, interest management, placement, inventory, combat, and persistence coordination.

`services/event-log`
- Append-only event stream and query layer.

`services/progression`
- Consumes events and updates quests, ranks, achievements, and guild goals.

`services/content-registry`
- Stores versioned definitions for items, quests, rewards, ranks, and seasonal content.

`services/discord-bridge`
- Identity linking, role sync, announcements, and commands.

`services/operator-api`
- Read and write APIs for the admin web client and operational tooling.

`packages/schemas`
- Shared typed definitions for data contracts and validation.

`packages/protocol`
- Client/server message contracts, versioning, and serialization helpers.

`packages/sdk-plugin`
- Runtime plugin APIs and permissions model.

`packages/sdk-content`
- Authoring helpers for quests, ranks, rewards, and validation.

`packages/observability`
- Shared metrics, tracing, and logging helpers.

`packages/dev-tools`
- Local tools, seeds, fake clients, replay tools, and test fixtures.

`plugins/examples`
- Minimal example plugins and scripted behaviors.

`plugins/core-community-pack`
- Canonical sample content pack showing guild, rank, challenge, and event patterns.

## Ownership Model

Keep ownership clear even if one person owns more than one area:

- Backend Lead: `services/simulation`, `services/event-log`
- Netcode/Realtime Lead: `packages/protocol`, simulation replication path
- Client Lead: `clients/game-client`
- Tools Lead: `clients/admin-web`, `services/operator-api`, `services/discord-bridge`
- Content Systems Lead: `services/progression`, `packages/sdk-content`, `plugins/core-community-pack`
- Infra Lead: `infra`, `scripts`, CI/CD, environments

## Repo Rules

- Every cross-service payload must live in `packages/schemas` or `packages/protocol`.
- Content definitions must validate in CI.
- No client-only rule change should be able to alter authoritative progression or inventory truth.
- Plugins must target the SDK packages, never private internals.
- Every new subsystem needs at least one ADR if it changes ownership boundaries or trust assumptions.
