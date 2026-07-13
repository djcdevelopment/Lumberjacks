# Viewing the Telemetry & Admin Surfaces (local runbook)

How to run the stack locally and view each surface — both the **player / community**
view and the **dev-admin operator** view. Every command below is verified against the
Docker Compose stack.

## The three "dashboards"

| Surface | Where | Who it's for |
|---|---|---|
| **Strategy ledger** — `docs/dashboard/index.html` | static file, open directly in a browser (no server) | you, planning — §03 signal grades, backlog, changelog. *Not* a live view. |
| **Community / telemetry pages** | Gateway **`:4000`** | players + community — the live "everyone is an alpha tester" surface |
| **Admin-web operator console** | Vite **`:5173`** → Operator API **`:4004`** | you, dev admin — full server overview + management |

## 1. Start the backend stack

Builds in-container, so the local .NET-SDK version doesn't matter (repo targets net9.0):

```bash
docker compose -f infra/docker/docker-compose.yml up --build -d
```

Services and ports:

| Service | Port | Role |
|---|---|---|
| postgres | 5435 | world + progression + event store |
| **gateway** | **4000** (HTTP), 4005/udp | unified host: simulation, tick loop, WS/UDP, **all community pages + v0 telemetry API** |
| eventlog | 4002 | authoritative event log (full events, with actor) |
| progression | 4003 | challenge / guild / reward engine (separate process — see D-19) |
| operatorapi | 4004 | admin API — proxies/fans out to the above |

Verify it's up:

```bash
curl -s http://localhost:4000/api/v0/telemetry/server        # tick counter, uptime, replication config
docker compose -f infra/docker/docker-compose.yml ps         # all services "running"
```

## 2. Player / community surface (Gateway `:4000`)

Open in a browser — no login, served straight from the game Gateway. All data is
**anonymized and aggregated** (no player id/name/position ever):

| URL | What it is |
|---|---|
| `http://localhost:4000/networksense` | **G3 NetworkSense HUD** — glanceable overlay: tick health vs 50 ms budget, sessions, delivery mix |
| `http://localhost:4000/events` | **G4 Gameplay Event feed** — live, anonymized event stream (structure/inventory/region/interest events) |
| `http://localhost:4000/community` | **Live Community View** — server overview: uptime, tick perf, sessions, delivery, regions |
| `http://localhost:4000/testing` | **G5 Local Testing Tools** — scenario cards (simulated) |

Raw API behind them (same-origin, `GET` from any origin, DB-less):
`http://localhost:4000/api/v0/telemetry/{server,tick,sessions,delivery,regions,events}`

The pages poll every 2 s. If a poll fails they show a "reconnecting / stale" chip and keep
the last good values — they never fabricate data.

## 2a. GCP P7 deployment

The current combined Valheim + Lumberjacks deployment is GCP P7:

```text
http://8.231.129.249:4000/community
http://8.231.129.249:4000/networksense
http://8.231.129.249:4000/events
http://8.231.129.249:4000/testing
```

These are the same pages as the local surfaces, but they fetch from the deployed
Gateway. Each page shows the environment and deployed revisions in its deployment
badge. Verify the identity directly before a session:

```bash
curl -s http://8.231.129.249:4000/api/v0/telemetry/deployment
```

The expected current identity is `environment=gcp-p7`, Lumberjacks
`686fea91d6a7a3ed214a2e0fbe9a43383e401409`, and
ComfyNetworkSense `0.5.18`. Keep `/valheim/*` and Operator API on the restricted P7
control surface; do not expose them through a public dashboard proxy.

For the admin console, forward Operator API through IAP and keep the Vite app local:

```bash
gcloud compute ssh comfy-lumberjacks-p7 --project lumberjacks-exp-20260711-djc \
  --zone us-west1-b --tunnel-through-iap -- -L 14004:127.0.0.1:4004
API_TARGET=http://127.0.0.1:14004 npm run dev -w @game/admin-web
```

## 3. Dev-admin operator console (`:5173`)

A separate Vite app (not in Compose). Start it alongside the stack:

```bash
cd clients/admin-web
npm install
npm run dev          # → http://localhost:5173, proxies /api/* to the Operator API on :4004
```

It surfaces player lookup, guild inspection, challenge setup, tick diagnostics,
transport/session live metrics, achievements history, and region create/delete — via
`operatorapi` (`:4004`), which fans out to gateway/eventlog/progression.

### The privacy split (why the two views differ)

- **Player view** (`/events`, Gateway): each event is `type + region + timestamp + non-identifying detail + provenance`. **No actor.** Optionally delayed (`Telemetry__PublicEventsDelaySeconds`, default `0`/live locally; set `30` for a public deployment).
- **Admin view** (console event log, `operatorapi → eventlog /api/events`): the **full** authoritative record, **including `actor_id`**.

Same events, two trust levels — this is the telemetry privacy invariant made concrete.

## Seeding activity (so the pages aren't empty)

The HTTP wire format is **snake_case** (`region_id`, not `regionId`). A few `curl`s
generate real events that show up on `/events`:

```bash
# structure_placed  (detail = structure type)
curl -s -X POST http://localhost:4000/structures/place -H "Content-Type: application/json" \
  -d '{"region_id":"region-spawn","player_id":"seed-demo","structure_type":"cabin","position":{"x":10,"y":0,"z":10},"rotation":0,"tags":["demo"]}'

# region_activated  (create a region)
curl -s -X POST http://localhost:4000/regions -H "Content-Type: application/json" \
  -d '{"id":"region-demo","name":"Demo Meadow","bounds_min":{"x":-100,"y":-10,"z":-100},"bounds_max":{"x":100,"y":100,"z":100},"tick_rate":20}'

# region_deactivated
curl -s -X DELETE http://localhost:4000/regions/region-demo

curl -s http://localhost:4000/api/v0/telemetry/events        # see them, newest-first
```

To generate **sessions, RTT, delivery, and `player_entered_region`** you need a real
WS/UDP client — the `tools/synthclient` harness (`SYNTH_TARGET`, `SYNTH_MODE=json|binary|udp`,
`SYNTH_CLIENTS`, `SYNTH_DURATION_S`). It targets net9.0, so run it via a `dotnet/sdk:9.0`
container attached to the Compose network (`--network` the compose net, target
`ws://gateway:4000`). Those sessions then populate `/community`, `/networksense`, and the
`/sessions` + `/delivery` (incl. the `udp_packets.reject_rate`) endpoints.

## Stopping

```bash
docker compose -f infra/docker/docker-compose.yml down          # keep the DB volume
docker compose -f infra/docker/docker-compose.yml down -v       # also wipe postgres data
```

## Notes / gotchas

- **No graphical game client.** The Godot client was dropped (PR #5); "in game as a player"
  today means a browser tab (the `/networksense` page is *styled* as an overlay but isn't
  embedded in a client) or simulated players via `synthclient`. A Valheim bridge exists
  (`Valheim*` endpoints on the Gateway) if that's the intended client.
- **No auth yet** (backlog D-09) — the Operator API / admin console isn't access-controlled;
  "admin" is by-network, not by-login.
- The community pages are **DB-less** and degrade gracefully, so `/community` etc. work even
  before Postgres is fully warm.
