# Host Capacity Benchmark — Cloud vs Local (2026-07-12)

Status: **Observed** (measured this date). Co-located-driver methodology; a cleaner
off-host re-run is listed under Next steps.

## Question

This is the community-compute build-order **step 5**: can local hardware host the
Lumberjacks authoritative workload as well as a rented cloud VM, and at what cost? The
answer decides whether "community-assisted hosting on volunteer nodes" rests on real
economics or wishful thinking.

## Method

- **System under test:** a bare `Game.Gateway`, run **DB-less** (it falls back to the
  in-memory `region-spawn` default when PostgreSQL is absent). The **exact same image
  binary** (`comfy-lumberjacks-p7-gateway:latest`) ran on all three hosts — built once,
  transferred by `docker save`/`docker load`, so no build-variance between hosts.
- **Driver:** [`scripts/load-test-dual-channel.js`](../scripts/load-test-dual-channel.js)
  — spawns N bots that each connect over binary WebSocket, bind a UDP channel, and send
  `player_input` at 20 Hz, receiving `entity_update` over both lanes. Run **co-located**
  on each host (patched so its UDP port is configurable).
- **Sweep:** 50 → 100 → 200 bots, 30 s per level.
- **Captured:** UDP `entity_update` delivered, average round-trip, peak gateway CPU and
  RSS (`docker stats`), and errors/disconnects.
- **Networking caveat:** cloud and AM4 drove the gateway over `--network host` + published
  ports; OMEN (Docker Desktop) used a user-defined bridge with container DNS, because
  Docker Desktop's host-networking/localhost-UDP path is unreliable. This makes OMEN's
  absolute CPU **not** perfectly comparable (see Caveats).

## Hosts

| Host | CPU | RAM | Role | Cost |
|---|---|---|---|---|
| Cloud | GCP `n2-highmem-8`, 8 vCPU | 64 GB | rented experiment VM (us-west1) | **$0.52/hr** list (~$380/mo), trial credits |
| AM4 | Ryzen 9 5900X, 12C/24T | 30 GB | dedicated-ish local worker | **sunk** (power) |
| OMEN | Core Ultra 9 285K, 24C | 128 GB | fleet **coordinator** (Ollama + HEARTH + Hyper-V host); measured via WSL2/Docker Desktop | **sunk** (power) |

## Results

Every level completed with **0 errors** on every host. Throughput is ~equal across hosts
because the load is driver-paced (20 Hz) and all three sustained the full offered load
with no drops. `entity_update` delivered / avg RTT / peak gateway CPU:

| Bots | ☁️ Cloud (8 vCPU) | 🖥️ AM4 (12C/24T) | ⚡ OMEN (24C) |
|---|---|---|---|
| 50 | 158,774 / 22 ms / ~0.22 core | 164,109 / 45 ms / ~0.21 core | 149,967 / 30 ms / ~0.22 core |
| 100 | 568,503 / 31 ms / ~0.5 core | 561,535 / 49 ms / ~0.62 core | 539,369 / 35 ms / ~0.44 core |
| 200 | 2,033,685 / **165 ms** / ~3.0 core (**38% of 8**) | 2,078,649 / **78 ms** / ~3.3 core (**14% of 24**) | 1,910,301 / **58 ms** / ~0.85 core (**3.5% of 24**) |

Gateway RSS stayed **~120–160 MiB** on every host at every level.

## Reading

- **Headroom / latency: OMEN ≥ AM4 > cloud.** The 8-vCPU cloud box is the only host that
  strains at 200 bots (165 ms RTT), because its cores get thread-starved by the
  co-located driver competing with the server and the O(N²) broadcast fan-out.
- **Cost-per-capacity is not close.** Both local boxes carry the full load at sunk cost;
  the cloud VM bills $0.52/hr for the least headroom.
- **The workload is featherweight.** ~1–3 cores and ~150 MiB RSS at 200 bots. Raw capacity
  is not the constraint on any of these hosts.

**Verdict:** the community-compute thesis is strongly supported. Local hardware is more
than adequate for authoritative hosting; the cloud's genuine advantages are elsewhere
(availability, geo-distribution, egress, managed ops) — **not** raw capacity.

## Caveats (do not over-read)

1. **Co-located driver** caps the true server knee on all three hosts — the driver and
   server share CPU, so absolute "max bots" is understated (most visibly on the 8-vCPU
   cloud box). A clean number needs an **off-host driver**.
2. **OMEN's CPU is not apples-to-apples.** Its efficient kernel-bridge networking (vs
   host-net + docker-proxy on cloud/AM4) means part of its ~0.85-core-vs-~3-core advantage
   is a networking-method artifact, not pure CPU. The qualitative result (most headroom)
   is solid; a literal ~3× CPU win is not claimed.
3. **DB-less, in-memory.** The EventLog/persistence path was not exercised.
4. **Single region, full interest** → O(N²) broadcast fan-out is the real scaling wall
   (159k → 569k → 2.03M updates as bots doubled), not CPU. Spatial interest tiers exist to
   cut exactly this and were not engaged here.
5. **AM4's higher idle-load RTT** (45 ms vs cloud's 22 ms at 50 bots) is unexplained —
   suspected memory-bandwidth pressure from resident local-inference models.

## Why this matters economically

The fleet's compute is overwhelmingly **GPU/RAM-bound** (inference, ComfyUI, local
models), not CPU-bound. Because the authority server is **CPU-light and RAM-light**
(~1–3 cores, ~150 MiB), it is a near-free **co-tenant** riding the idle CPU on GPU/RAM-bound
nodes — especially in **off-hours / low-GPU windows**. This is the economic engine for the
platform's placement/leasing layer: harvest spare CPU, place authority shards there, and
gracefully drain when GPU or memory-bandwidth demand spikes. The one real coupling to
respect is that this is a **20 Hz real-time** workload (50 ms tick budget): the live risks
under heavy GPU load are **memory-bandwidth contention** and **CPU scheduler jitter**, not
CPU or RAM capacity.

## Next steps

1. **Contention probe:** re-run the AM4 sweep *while a real inference/GPU job runs on the
   box*, and measure whether the tick and RTT hold. This answers "off-hours-only vs
   anytime" with a number.
2. **Off-host clean re-run:** drive each host from a separate machine (OMEN⇄AM4 over LAN;
   cloud from an in-region VM) to find each host's true server knee, comparing degradation
   curves rather than absolute WAN RTT.
