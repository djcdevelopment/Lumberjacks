# Decision log — autonomous build run, 2026-07-12 (evening)

Derek stepped away with: "build out everything — do not stop at phase 1, build as much
as you can; leverage HEARTH/mechnet Gemini Pro; be frugal; document decisions; go with
instinct." Phases 1–2 are already in master. This log records what was chosen, why, and
what was deliberately not done, for later review.

## Scope chosen (evidence order, my call)

1. **Track A — G1 Public Telemetry API v0 + G2 Live Community View** (strategy Phases
   3+4). Endpoints under `/api/v0/telemetry/*` on the gateway: `server`, `tick`
   (TickMetrics window + replication block), `sessions` (aggregates only), `delivery`
   (LumberjacksTelemetry snapshots), `regions`. Every response marked
   `stability: unstable` (v0 per strategy refinement #2). CORS: GET-only, any origin.
   The community view ships as a self-contained page served by the gateway at
   `/community`, polling the v0 API — the strategy's "dashboard consumes APIs, owns no
   state" principle made literal.
2. **Track B — Phase 3(a) UDP-socket experiment + adaptive-degrade v2** (benchmark
   next-steps 7a/7c). `Replication__UdpSockets` (default 1 = today's shared client;
   0 = auto = worker count); degrade v2 becomes burst-aligned (suppress the *next burst
   tick's* mid-band after a burst-tick overrun) — the measured fix for Follow-up E's
   miss.
3. **Track C — measurement** (after B): UDP-socket A/B at 500/600 (does the soft-fail
   band clear when the wall drops toward sum/workers?), adaptive-v2 check at tiered/400,
   and the `NearRadius` 100→200→300 dial sweep at 400 bots (next-step 8's gameplay
   table). Fairness quartile splits captured in every run; active code-level diagnosis
   of the inversion is DEFERRED (observation first, instrumentation only if it persists
   under the new socket layout).

**Not attempted tonight:** G3/G4/G5 (NetworkSense integration, quest telemetry, in-game
testing tab) — game-client/UX surface, partly in other codebases, wrong thing to build
unattended; per-client send budgets (needs a design conversation about what "budget"
means per connection quality — ADR-0011 leaves it open); serialize-once payload refactor
(gated on the UDP-socket result by Follow-up E's own logic).

## Standing decisions for this run

- **Nothing merges to master autonomously.** Every master merge today was Derek's
  explicit call; that stays his. Work lands on pushed feature branches + this branch;
  review happens when he's back.
- **Privacy rule enforced in code, not docs:** the v0 API exposes aggregates only — no
  player ids, names, or positions anywhere in a response. A test asserts it.
- **Gemini Pro authorship boundary:** Gemini (via HEARTH, `gcp-gemini-pro`) drafts
  self-contained artifacts — the community-view HTML/JS page, the API reference doc,
  summaries. **C# stays Claude-authored** (integration risk; the fleet rule "never let
  HEARTH-routed generation author C#" predates tonight and held up well). This is the
  frugality split: bulk text/markup → Gemini, judgment/code → Sonnet agents, decisions →
  orchestrator.
- **UDP-socket caveat accepted:** extra send sockets bind ephemeral ports, so replies no
  longer originate from :4105. Fine for the LAN bench (the loader doesn't filter by
  source) and documented as a NAT hazard for real clients — this is an experiment knob,
  not a production default.
- **The rig serializes:** AM4 hosts one measurement at a time; code tracks run parallel
  in isolated worktrees.
