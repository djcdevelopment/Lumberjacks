# Public Telemetry API v0 Reference

The Public Telemetry API v0 (part of Phase 3 of the community telemetry strategy) provides a read-only, versioned surface exposing Gateway tick, replication, session, and delivery metrics to the public internet. This API is designed to empower community members to build their own custom dashboards, overlays, bots, and analytics tools, ensuring server telemetry is not restricted solely to the operator team.

> **⚠️ Stability Notice: EXPLICITLY UNSTABLE**
> This API is currently at `api_version` "v0" and is explicitly marked as "unstable". This status is reflected on every response both as JSON fields (`"api_version": "v0"`, `"stability": "unstable"`) and via an `X-API-Stability: unstable` HTTP response header. The shape of every endpoint may change without notice until the schema settles. Do not build production integrations against this API yet.

## Privacy

This API strictly adheres to a hard privacy rule: **no player IDs, names, or positions appear in ANY v0 response, ever.** Every endpoint returns only aggregated metrics or static/world-level facts. To ensure absolute compliance, this rule is enforced by an automated test suite that serializes every endpoint's response and asserts that connected players' identifiers never appear anywhere in the output.

## CORS Policy

Unlike the rest of the Gateway's explicitly origin-allowlisted CORS policy, the telemetry endpoints are designed as a deliberate public surface. All `/api/v0/*` endpoints (as well as the `/community` page) allow `GET` requests from any origin (`Access-Control-Allow-Origin: *`) with no credentials required. 

## Endpoints

### GET /api/v0/telemetry/server

Returns server identity and uptime along with the active replication policy and its configured knobs (including `policy`, `near_radius`, `mid_radius`, `mid_tick_interval`, the effective post-auto-resolve `send_workers` count, `deadline_ms`, and `adaptive`). Note that `tick_rate_hz` is currently a fixed 20 (the simulation's fixed tick rate, which is not yet configurable).

```json
{
  "api_version": "v0",
  "stability": "unstable",
  "current_tick": 986,
  "tick_rate_hz": 20,
  "uptime_seconds": 49,
  "started_at": "2026-07-12T16:39:00.1462837+00:00",
  "replication": {
    "policy": "tiered",
    "near_radius": 100,
    "mid_radius": 300,
    "mid_tick_interval": 4,
    "send_workers": 1,
    "deadline_ms": 0,
    "adaptive": false
  }
}
```

### GET /api/v0/telemetry/tick

Returns timing and replication data for the most recent ~5-second tick-timing window. This includes p50/p99/max durations per phase (total, interval, sim, hash, broadcast, interest, send, housekeeping), the overrun count against the 50ms tick budget, and replication counters (sent/culled entity updates, effective `send_workers`, `deadline_aborts`, and `degraded_ticks`) for that window. Clients should note that `tick_timing` will be `null` (which is not an error) until the first ~5-second window closes after server startup—a legitimate "warming up" state that integrations must handle gracefully.

```json
{
  "api_version": "v0",
  "stability": "unstable",
  "tick_timing": {
    "window_end_tick": 900,
    "sample_count": 100,
    "overruns": 0,
    "budget_ms": 50,
    "phases": {
      "total": { "p50_ms": 0.0133, "p99_ms": 0.0877, "max_ms": 0.0905 },
      "interval": { "p50_ms": 49.9483, "p99_ms": 55.4382, "max_ms": 55.5047 },
      "sim": { "p50_ms": 0.0051, "p99_ms": 0.0411, "max_ms": 0.065 },
      "hash": { "p50_ms": 0.0077, "p99_ms": 0.0598, "max_ms": 0.0621 },
      "broadcast": { "p50_ms": 0, "p99_ms": 0, "max_ms": 0.0001 },
      "interest": { "p50_ms": 0, "p99_ms": 0, "max_ms": 0 },
      "send": { "p50_ms": 0, "p99_ms": 0, "max_ms": 0 },
      "housekeeping": { "p50_ms": 0, "p99_ms": 0.0176, "max_ms": 0.0701 }
    },
    "replication": {
      "policy": "tiered",
      "sent": 0,
      "culled": 0,
      "send_workers": 1,
      "deadline_aborts": 0,
      "degraded_ticks": 0
    },
    "captured_at": "2026-07-12T16:39:45.1420153+00:00"
  }
}
```

### GET /api/v0/telemetry/sessions

Returns session aggregates only: the total connected session count, broken down by wire protocol (json/binary) and by region ID. It never includes a session ID, player ID, or per-session breakdown. *Note: The captured JSON sample below reflects an idle local server with no connected clients; on an active server, `by_protocol` and `by_region` would contain string-keyed counts (e.g., `"by_protocol": {"json": 30, "binary": 12}`).*

```json
{
  "api_version": "v0",
  "stability": "unstable",
  "total": 0,
  "by_protocol": {},
  "by_region": {}
}
```

### GET /api/v0/telemetry/delivery

Returns cumulative networking counters since server start. The `delivery` map contains message delivery-path outcomes (e.g., udp, binary_ws, json_ws), where keys appear once at least one message has taken that path. The `transitions` map counts session lifecycle events (created, resumed, detached). *Note: As with the sessions endpoint, the captured JSON sample below is from an idle local run where no clients have connected since startup, hence the empty objects.*

```json
{
  "api_version": "v0",
  "stability": "unstable",
  "delivery": {},
  "transitions": {}
}
```

### GET /api/v0/telemetry/regions

Returns static and world-level facts about active regions, including their ID, name, live `player_count`, spatial bounds (min/max for x, y, and z), and `tick_rate`. This is safe to expose publicly as it contains strictly world-level data and no per-player information.

```json
{
  "api_version": "v0",
  "stability": "unstable",
  "regions": [
    {
      "id": "region-spawn",
      "name": "Spawn Island",
      "player_count": 0,
      "bounds": {
        "min": { "x": -500, "y": -10, "z": -500 },
        "max": { "x": 500, "y": 200, "z": 500 }
      },
      "tick_rate": 20
    }
  ]
}
```

## Live Community View

As part of Phase 4 of the telemetry strategy, a Live Community View is available at `GET /community`. This is a single, self-contained HTML page served directly by the Gateway that polls the API endpoints listed above every 2 seconds to provide a real-time, out-of-the-box telemetry dashboard for community members.

## Versioning

Future breaking changes to this telemetry surface will ship under new versioned paths (e.g., `/api/v1/...`). When this happens, the `/api/v0/...` endpoints will either be left in place or removed with notice. However, because the v0 API is explicitly unstable, it carries no deprecation SLA.

## Availability

Every v0 endpoint works without a database connection — they read `WorldState`, `TickMetrics`,
`SessionManager`, and the in-process `LumberjacksTelemetry` tallies, none of which touch
Postgres. The samples above were captured from exactly that: a `docker build --target gateway`
image run with no `ConnectionStrings__GameDb` set, verifying the Gateway's existing
graceful-degrade behavior (falls back to in-memory defaults, logs a warning) extends cleanly to
this API.

## Source

- Server / tick / delivery / regions: `src/Game.Simulation/Endpoints/TelemetryV0Endpoints.cs`
- Sessions (needs Gateway-only `SessionManager`): `src/Game.Gateway/Endpoints/TelemetryV0SessionsEndpoints.cs`
- Shared envelope, CORS policy, `/api/v0/telemetry` route group: `src/Game.ServiceDefaults/PublicTelemetryV0.cs`
- Community view: `src/Game.Gateway/Endpoints/CommunityViewEndpoints.cs`, `src/Game.Gateway/Community/community.html`
- Privacy tests: `tests/Game.Simulation.Tests/TelemetryV0EndpointsTests.cs`, `tests/Game.Gateway.Tests/TelemetryV0SessionsEndpointsTests.cs`