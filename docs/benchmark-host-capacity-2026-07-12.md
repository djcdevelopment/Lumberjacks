# Host Capacity Benchmark — Cloud vs Local (2026-07-12)

Status: **Observed** (measured this date; updated same date with Follow-up A — the
GPU-contention probe — and Follow-up B — the off-host clean re-runs. Both former
next-steps are done).

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
   cloud box). A clean number needs an **off-host driver**. **Resolved same-date** by
   Follow-up B: the true knee is 400 bots on cloud and AM4 alike, and at 200 bots the
   cloud RTT drops from 165 ms (co-located) to 75 ms (off-host).
2. **OMEN's CPU is not apples-to-apples.** Its efficient kernel-bridge networking (vs
   host-net + docker-proxy on cloud/AM4) means part of its ~0.85-core-vs-~3-core advantage
   is a networking-method artifact, not pure CPU. The qualitative result (most headroom)
   is solid; a literal ~3× CPU win is not claimed.
3. **DB-less, in-memory.** The EventLog/persistence path was not exercised.
4. **Single region, effectively-full interest** → O(N²) broadcast fan-out is the real
   scaling wall (159k → 569k → 2.03M updates as bots doubled), not CPU. **Correction
   (post-recon, same date):** the spatial interest tiers were *not* "disengaged" — they
   are hardcoded always-on (`InterestManager`: near 100 u every tick, mid 300 u every
   4th tick, far dropped; no config switch exists) and were active in every run here
   (that's the measured `interest` phase). They were merely **geometrically
   ineffective**: the DB-less region is a 1000×1000 square, the 300 u mid radius covers
   ~28 % of it, every player spawns at the same point (0,0,0), and clamped random-walk
   movement pools bots at the walls. All baselines in this doc therefore measure the
   hardcoded *tiered* policy under worst-case clustering; a true `full` (unfiltered)
   baseline does not exist in any binary yet — Phase 1 of the telemetry strategy builds
   it, plus spawn-spread and loader-wander knobs to make radius experiments meaningful.
5. **AM4's higher idle-load RTT** (45 ms vs cloud's 22 ms at 50 bots) reproduced in
   Follow-up A's fresh baseline (43/61/56 ms) — but the probe **weakens** the
   memory-bandwidth suspicion: RTT got *better*, not worse, while inference actively ran.
   Current best hypothesis is **power management** — an idle desktop parks cores in deep
   idle states, while a loaded box (or a busy cloud host) keeps them awake — which also
   explains the counter-intuitive loaded-run speedup.

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

## Follow-up A — GPU-contention probe (AM4, same date)

Answers former next-step 1: can the authority server co-tenant on a GPU node *anytime*,
or only off-hours? Method: a fresh idle baseline sweep, then the identical sweep while a
real local inference job ran continuously — llama.cpp (SYCL) serving Qwen3-30B-A3B fully
resident on AM4's GPUs (**2× Intel Battlemage dGPU** — a hardware correction: the box is
not NVIDIA), ~80 tok/s in back-to-back generations for the whole sweep. Local execution
was verified by package power (RAPL: ~26 W idle → ~118–120 W plateau, back to ~26 W
within 20 s of killing the loop) — necessary because HEARTH can route inference to cloud
backends, which would silently invalidate the probe.

| Bots | Idle (fresh baseline) | GPU-loaded |
|---|---|---|
| 50 | 43 ms / ~0.19 core | 33 ms / ~0.26 core |
| 100 | 61 ms / ~0.5 core | 51 ms / ~0.6 core |
| 200 | 56 ms / ~1.8 core | 63 ms / ~1.8 core |

(avg RTT / gateway steady CPU; **0 errors in all six runs**; RSS 120–190 MiB throughout.)

**Reading: no contention regression at these levels — "anytime", not "off-hours-only".**
Loaded RTT was *lower* at 50/100 and inside the idle run's own interval spread at 200.
The likely mechanism for the counter-intuitive speedup (and for caveat 5) is power
management, not bandwidth. Scope caveats: the model sat fully in dGPU VRAM, so host-DRAM
bandwidth was not heavily exercised — shared-memory/iGPU inference and RAM-bandwidth-heavy
jobs remain untested; and because the bare bench container exports no telemetry (the tick
metrics exist but need an OTLP collector — see Next steps), RTT was the only tick-health
proxy available.

## Follow-up B — off-host clean re-run (cloud + AM4, same date)

Answers former next-step 2: drive each host from a separate machine so the driver never
shares CPU with the host under test. Cloud: driven from an in-region `e2-standard-8` VM
inside the same VPC (driver VM deleted after; the gateway VM was found stopped and was
restored to stopped). AM4: driven from OMEN over LAN (ufw opened for 4100/4105 during the
run, then reverted). Levels above 200 bots ran as parallel 200-bot loader shards — a
single node driver process is single-threaded — and driver CPU was sampled at every
level and never came close to bottlenecking. Ops note: overriding the gateway's listen
address requires a literal `Urls=` env var; the image's baked `appsettings.json` beats
`ASPNETCORE_URLS` in this app's config order.

| Bots | ☁️ Cloud (8 vCPU) | 🖥️ AM4 (12C/24T) |
|---|---|---|
| 100 | 30 ms / ~20.0 Hz / ~0.61 core | 32 ms / ~0.5 core |
| 200 | 75 ms / ~19.2 Hz / ~2.9 core | 49 ms / ~1.3 core |
| 400 | **752 / 958 ms — tick ~12 Hz** / ~3.75 core (47 % of 8) | **1571 / 2115 ms, climbing** / ~3 core (~12 % of 24) |

(avg RTT per 200-bot shard / observed tick rate where measured / gateway steady CPU.
Cloud tick rate came from polling the gateway's `GET /tick` counter; AM4's was not
polled. **0 errors at every level on both hosts** — degradation is pure latency/tick-lag,
never failures.)

**Reading: both hosts knee at the same level — 400 bots — with CPU nowhere near
saturated.** That is the signature of the **single-threaded O(N²) broadcast fan-out**
blowing the 50 ms tick budget: an architectural wall, independent of host hardware. Two
corollaries:

- Caveat 1 is confirmed and quantified: at 200 bots the cloud box measured 165 ms with a
  co-located driver vs **75 ms** off-host (AM4: 56 → 49 ms).
- Hardware is not the capacity lever. A single full-interest region tops out between 200
  and 400 bots on an 8-vCPU cloud VM and a 24-thread desktop alike; the lever is
  **interest management / parallelizing the fan-out** (build-order steps 3–4).

## Follow-up C — tick-phase attribution (same date, new instrumentation)

Commit `6f1670d` (branch `claude/optimistic-mclean-435ef7`) landed per-tick/per-phase
timing: `game.tick.duration` histograms plus a DB-less ~5 s rolling window emitted as a
"Tick timing" log line and as `tick_timing` on `GET /tick`. A fresh image built from that
commit (`lumberjacks-gateway:tick`, left staged on AM4) re-ran the off-host AM4 levels —
this converts Follow-up B's RTT-inferred knee into a measured tick p99 with phase
attribution (steady-state windows; 60 s levels; driver on OMEN):

| Bots | tick total p50 / p99 | overruns per 100 ticks | interest p99 | send p99 |
|---|---|---|---|---|
| 0 | 0.01 / 0.03 ms | 0 | 0 | 0 |
| 200 | ~8 / 39–44 ms | 0–1 | ~2 ms | 37–42 ms |
| 400 | ~30 / **160–175 ms** | **25** | ~8 ms | **152–168 ms** |

Findings, in order of importance:

1. **The wall is the `send` phase.** At 400 bots, broadcast is ~100 % of tick time and
   `send` outweighs `interest` ~20:1 (152–168 ms vs ~8 ms). Serializing/writing updates
   to N clients in a single-threaded loop is the whole knee; sim (~0.4 ms) and hash
   (~0.2 ms) are irrelevant at these scales.
2. **The full-interest safe ceiling is ~200 bots/region.** 200 bots fits the 50 ms
   budget but consumes ~80–88 % of it at p99 (one window grazed 49.8 ms with 1 overrun);
   400 blows it 3.3× with a quarter of all ticks overrunning.
3. **Overrun delays are self-inflicted, not host jitter.** `interval` p99 (tick-start
   spacing) sits at the nominal ~50–51 ms at idle and 200 bots, and tracks total p99
   exactly at 400 — ticks start late because the *previous* tick overran, not because
   the scheduler starved the loop. This also retro-validates Follow-up A: co-tenant GPU
   load never disturbed tick spacing.
4. **Ticks are bimodal under load** (400 bots: p50 30 ms vs p99 166 ms) — the cost
   concentrates in the ~25 % of ticks doing heavy broadcast work.
5. **The serial fan-out is unfair to late joiners.** At 400 bots the first-connected
   shard averaged 49 ms RTT while the second averaged 1798 ms (and finished 4 bots shy
   of clean disconnect, 0 errors) — early sessions get served first in the send loop and
   late sessions absorb nearly the entire send-phase delay. Re-test fairness after the
   fan-out rework.
6. **An idle region is essentially free** (total p50 0.01 ms) — reinforcing the
   co-tenancy economics from Follow-up A.

## Next steps

1. ~~Contention probe~~ — **done** (Follow-up A): no regression under local dGPU
   inference; "anytime" co-tenancy holds at these load levels.
2. ~~Off-host clean re-run~~ — **done** (Follow-up B): true knee = 400 bots on both
   hosts, and it is architectural, not hardware.
3. ~~Make tick health observable in the bench harness~~ — **done** (Follow-up C):
   commit `6f1670d` added the DB-less tick-timing window (log line + `GET /tick`), and
   the confirmation run measured the knee directly. Merge that branch.
4. **Move the knee — specifically the `send` phase:** parallelize the broadcast
   fan-out and/or engage spatial interest tiers, then repeat Follow-up C's run as the
   after-snapshot (before: 400 bots → tick p99 160–175 ms, send p99 152–168 ms,
   25 % overruns). Also re-check late-joiner fairness (finding 5).
5. **Contention probe, hard mode (optional):** repeat Follow-up A with a host-DRAM-heavy
   job (shared-memory/iGPU or CPU inference) to close the remaining bandwidth scenario.
   The `interval` phase metric now makes scheduler jitter directly visible.
