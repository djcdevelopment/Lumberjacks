# M4a stage-1 handoff

**Status:** Recipient-scoped queue, principal-derived consumer identity, legacy compatibility, recipient activity lease seam, v1/versioned WAL replay tests, N=2/N=10 isolation, and per-recipient conservation proof landed locally. No deployment or push was performed.

**What did not land:** The sibling-repository producer recipient emitter and producer outbox remain stage-3 work; M3 lifecycle states remain out of scope. The two pre-existing Windows roadmap path tests remain unchanged.

**Exact verification so far:** Baseline 504 total / 502 passing / 2 known failures. Gateway after implementation: 153 total / 151 passing / 2 known path failures. Canonical Docker run: 519/519 passing. Scope mutation: 4 named failures. Lease mutation: 2 named failures. WAL-version mutation: 1 named failure. Isolation filter matched 3 tests; v1 replay, corrupt WAL, and convergence filters each matched non-zero tests.

**Next step:** Run the roadmap staged check after the single journal note, then commit the coherent Gateway-only change.
