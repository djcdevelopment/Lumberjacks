# M1 strict admission — kickoff plan

2026-07-16. M1 is the active milestone. Authoritative scope lives in
[valheim-volunteer-platform-plan.md](network/valheim-volunteer-platform-plan.md)
(Milestone 1: identity model, deliverables, Gate M1); this document maps that scope
onto the current sources and fixes an implementation order. Survey drafted via a
HEARTH-routed large-context pass over the Gateway and mod sources; every
load-bearing claim below was re-verified against the code by hand.

Standing constraint from M0: the running release is frozen and identity-pinned.
Any Gateway or mod change ships as a new release through the reproducible
bundle + promotion-drill pipeline, so stages are grouped to minimize release
cuts — especially mod cuts.

## 1. Current state

- **Enrollment storage** — `SteamEnrollmentService` persists a plaintext JSON
  file (`invites.json`); `AccessToken` is stored and compared in cleartext
  (fixed-time compare at `SteamEnrollmentService.cs:52`). No uniqueness per
  SteamID, no revocation/expiry/last-used state on the enrollment itself, no
  audit events, no release-compatibility record.
- **Credential echo** — `SteamEnrollmentEndpoints.cs:57,66` returns the raw
  `AccessToken` into the browser response as `lumberjacksClientAccessKey`
  (config-snippet handoff). Reusable secret over plain HTTP today.
- **Authorization** — `ValheimClientAccessMiddleware` accepts either the shared
  `X-Lumberjacks-Client-Key` env credential or an enrollment id+token pair, and
  grants global access; `SteamEnrollmentEndpoints` hand-checks
  `X-Lumberjacks-Admin-Key`. No admin/producer/consumer/telemetry/public split.
- **Join decision (Gateway)** — `ValheimHandshakeService.Evaluate` emulates
  native checks (banlist, password, duplicate uid, capacity
  `MaxPlayers = 10` const at `ValheimHandshakeService.cs:149`). It does **not**
  consult the enrollment roster, release identity, readiness lease, or a
  one-seat reservation.
- **Join decision (mod)** — `HandshakeResponderRunner` is deliberately
  fail-OPEN (comment at line 28): any endpoint error or unparseable verdict
  returns `HandshakeDecision.PassThrough` (`endpoint_error` at line 185,
  `unparseable_verdict` at line 212). Strict admission requires fail-closed.
- **Transport** — both the handshake responder and
  `ZdoAuthoritativeConsumerRunner` use a raw-socket HTTP client and throw
  `NotSupportedException` for any non-`http` scheme
  (`HandshakeResponderRunner.cs:233`). No TLS path exists in the mod.
- **Recipient identity** — the consumer self-assigns
  `_consumerId = Guid.NewGuid()...` (`ZdoAuthoritativeConsumerRunner.cs:80`)
  and the Gateway trusts it; the plan requires a credential-derived opaque
  `recipient_id` the client cannot choose.
- **Rate limiting** — none (`Program.cs` registers no limiter).

## 2. Gap analysis

| M1 deliverable / gate bullet | Current state | Gap |
| --- | --- | --- |
| One active enrollment per SteamID | Blind append to JSON dictionary | Uniqueness rule + explicit admin replacement |
| List/revoke/expiry/last-used/audit/compat | Only `EnrolledUtc` tracked | Expanded record + admin endpoints + audit events |
| Secrets hashed, never re-returned | Plaintext storage; token echoed to browser | Hash at rest; one-time issuance; drop the echo |
| Capability split (admin/producer/consumer/telemetry/public) | Two global keys | Policy-based authorization, capability-scoped credentials |
| Join hook asks Gateway about actual joining SteamID | Native emulation only; the wire `uid` is a session id, **not** a SteamID64 (§5.3) | Mod must decode the SteamID64 from the session ticket and send it (stage 3); then roster lookup keyed on it |
| Release/protocol compatibility gate | Protocol checked (`net_version != 36`, check A); release **not** checked — the mod sends no release identity of its own (§5.9) | Mod must send its own release identity (stage 3) + a manifest-pinned hash check, with the hash source decided |
| Fresh readiness lease | Not implemented | Lease issue/verify, expiry, single active lease — identity-bound, so dark until stage 3 |
| One-seat capacity reservation | `MaxPlayers = 10` const, and `CurrentPlayers` is operator-configured, not observed | Gateway-enforced reservation of exactly one seat, with a passive TTL to survive the absent disconnect signal (§5.4) |
| Strict admission fails closed | Mod fail-open on any fault | Cutover-mode-aware reject on endpoint error |
| Certificate-validating TLS on public traffic | Mod throws on `https` | TLS client in mod + TLS termination at the VM |
| No credential-selected recipient | Client-chosen `consumer_id` trusted | Server-derived opaque `recipient_id` |
| Rate limits (invite/readiness/poll/ACK/telemetry) | None | Independent per-surface limiters |

## 3. Implementation stages

TLS termination: **Caddy sidecar** in the existing compose stack — automated
Let's Encrypt issuance/renewal, HTTP→HTTPS at the edge, reverse proxy to
Kestrel; container-to-container producer traffic stays on the private Docker
network. Prerequisite: a stable public DNS name for the volunteer endpoint
(ACME will not issue for a bare IP) and ACME state on a persistent mount so
drills/rebuilds don't burn issuance rate limits.

1. **Identity schema, capability split, rate limits** (Gateway only) —
   hashed-at-rest enrollment store with unique-active-SteamID, revoke/expiry/
   last-used/audit; replace the middleware with capability policies; add
   per-surface rate limiters; derive and persist `recipient_id` per enrollment.
   Verify with unit/integration tests (hashing, policy rejection matrix,
   limiter exhaustion). No mod involvement.
2. **One-seat reservation live; identity gates dark** (Gateway only) —
   re-scoped 2026-07-16: the roster gate is **not buildable Gateway-only**, because
   the frozen mod sends no Steam identity (§5.3). Ships live: the one-seat
   reservation as pure capacity (a seat needs a count, not a name), actionable
   reason codes, retained rejection records, seat lease + passive TTL — all landed
   `cd78296`, see §7. Intended to be built behind a flag, unreachable live: the
   roster lookup and readiness lease. **The dark gates are not built** — §7 records
   why the matrix has to be re-homed to its real surfaces first, and that nothing
   in the system issues a readiness lease to gate on. The release-compatibility
   gate is **not** built dark either — it is a string compare whose expected-value
   source is itself deferred (§5.9), so it rides stage 3 whole.
   **Stage 2 delivers no live identity gating: strict admission does not exist
   until stage 3.**
3. **Mod cut: authenticated identity + fail-closed strict mode + TLS +
   server-derived recipient** (mod + Gateway, one release cut) — decode the
   SteamID64 **from the session ticket** inside the mod and send it (the mod is the
   only caller with Steamworks access, §5.5; a client-asserted field would be
   forgeable), send the mod's own release identity, and flip the stage-2 flag on;
   TLS-capable client with certificate validation; in strict cutover mode any
   endpoint fault becomes a reject (labeled native recovery stays an explicit
   operator mode); stop sending a client-chosen `consumer_id`. Verify live: Gateway
   stopped ⇒ strict mode refuses admission; the §4 `2-dark` rows re-verified on the
   wire; https endpoint exercised end-to-end; no reusable credential on a plaintext
   public link (capture check).
4. **Bootstrap handoff hardening** (Gateway only) — replace the plaintext
   config echo with a one-use bootstrap consumed by the installer (M2
   consumes this; M1 only guarantees no reusable secret crosses the public
   link in cleartext). Verify replay of a used bootstrap fails.

Release cuts: stages 1–2 can ship as one Gateway release; stage 3 is the
single mod+Gateway cut; stage 4 rides the next Gateway cut. Every cut goes
through bundle validation and the promotion drill per the M0 pipeline.

## 4. Admission acceptance matrix

`Stage` records where each row is **verifiable**: `1` already landed (b842bd3); `2-live`
enforced live once stage 2 ships; `2-dark` logic built and synthetically tested in stage 2 but
unreachable on the wire until the stage-3 mod cut feeds it an identity; `3` needs the mod cut.
`recipient_forbidden` is stage 3 despite stage 1 landing it on the telemetry heartbeat
(`ValheimTelemetryHeartbeatEndpoints.cs:76`): the row's scenario is poll/ACK, where the
Gateway still trusts a client-supplied `consumer_id` (`ValheimZdoRedirectEndpoints.cs:85`)
until the mod stops self-assigning one.

| Case | Setup | Expected decision | Reason surface | Test | Stage |
| --- | --- | --- | --- | --- | --- |
| Enrolled + compatible + fresh lease + seat free | happy path | accept | — | auto + live | 2-dark |
| Uninvited SteamID | no enrollment record | reject | not_enrolled | auto + live | 2-dark |
| Revoked / expired enrollment | admin revoke; expiry passed | reject | enrollment_revoked / expired | auto | 2-dark |
| Wrong Steam account | enrollment for different SteamID64 | reject | steamid_mismatch | auto + live | 2-dark |
| Wrong mod/release | stale hash in handshake | reject | release_incompatible | auto | 3 |
| Stale readiness lease | lease older than TTL | reject | lease_stale | auto | 2-dark |
| Seat taken | second concurrent join | reject | capacity_reserved | auto | 2-live |
| Replayed invite / duplicate enrollment | reuse consumed invite | reject | invite_consumed | auto | 2-dark |
| Consumer token on producer/admin/reset/compaction/handshake ops | scoped credential | deny (403) | capability_denied | auto | 1 |
| Consumer names another recipient_id | forged recipient in poll/ACK | deny | recipient_forbidden | auto | 3 |
| Gateway down, strict mode | stop gateway container | reject at join | strict_authority_unavailable | live | 3 |
| Gateway down, native recovery mode | operator-labeled mode | native decision | labeled pass-through | live | 3 |
| Plaintext public link | http:// probe of volunteer surface | no reusable credential observable | redirect/refuse | live | 3 |

**Not buildable live in stage 2.** Every `2-dark` row keys on an identity the frozen mod never
sends: the handshake `uid` is `ZDOMan.GetSessionID()`, a per-session long, not a SteamID64
(`NETCODE-HANDSHAKE-CONTRACT.md:69`; live capture shows SteamID `76561198088711642` decoding as
`uid=1167002880`). `release_incompatible` fails for the same class of reason — the mod sends the
*joining client's* `version`/`net_version`, never its own release identity
(`HandshakeResponderPatches.cs:34,39`) — and rides stage 3 whole rather than dark, since its
expected-value source is deferred too (§5.9). Only `capacity_reserved` survives as live stage-2
work, because a one-seat gate needs a count, not a name.

## 5. Risks and open questions

1. Mono/Unity TLS: the raw-socket client may need `SslStream` with explicit
   validation callback or a `UnityWebRequest` rewrite; cipher support on
   Valheim's Mono runtime must be proven early in stage 3.
2. Public DNS name for the volunteer endpoint is an operator decision and a
   stage-3 prerequisite (ACME needs it).
3. **Resolved — the `uid` is not a SteamID64.** PeerInfo field 1 is
   `GetUID()` = `ZDOMan.GetSessionID()` (`NETCODE-HANDSHAKE-CONTRACT.md:69`,
   `:1787`), a per-game-session long. Proven live: client SteamID
   `76561198088711642` decoded as `uid=1167002880`
   (`fieldlab/evidence/i5-handshake-live/ANALYSIS.md`). The only Steam identity in
   the packet is the opaque `steamSessionTicket` byte[], which the mod reads past
   and never decodes (`HandshakeResponderPatches.cs:45`). The roster key does not
   exist on the wire; identity admission moves to stage 3 wholesale.
4. **Resolved in stage 2 — consumer poll/ack is the liveness signal.** Nothing
   reports a disconnect, and `_connectedUids` only ever grows
   (`ValheimHandshakeService.cs:299`), so a seat on a fixed timer is wrong in both
   directions: too short frees it mid-session, too long locks the sole volunteer
   out after a crash and lets a ghost accept (§5.5) hold the seat for the full
   lease. An earlier revision of this entry claimed the only live signal was the
   telemetry heartbeat's `peer_count`, which carries no `window_id` (only a
   per-boot `instance_id`, `TelemetryCoordinator.cs:487`) and so would have needed
   a mod cut. **That was wrong.** The authoritative consumer's own poll/ack traffic
   is already window-keyed — `/valheim/zdo-redirect/pending/{windowId}` and
   `/ack/{windowId}` (`ValheimZdoRedirectService.cs:118,125`) — and the frozen
   0.5.31 mod already sends it. `ValheimWindowActivityService` records it; a seat
   is live while its grant OR the window's consumer traffic is inside
   `SeatLeaseSeconds` (default 60). A holder who is really there refreshes it
   continuously; one who crashed or never landed stops instantly. Gateway-only, no
   mod cut. Remaining limits: liveness is window-scoped, so it cannot say *which*
   holder is alive (fine at one seat, the reason `SeatCapacity > 1` counts but is
   not yet meaningful), and a holder who runs no consumer is invisible to it. The
   separate `_connectedUids` growth bug stands — a client that crashes and
   reconnects *without restarting Valheim* keeps its session `uid` and is rejected
   `ErrorAlreadyConnected` (gate G) until the window is reset.
5. **A Gateway accept is overturnable, and the Gateway never learns.** The mod
   hardcodes `ticketValid: true` (`HandshakeResponderPatches.cs:50`) — it never
   verifies the ticket; vanilla runs the real `VerifySessionTicket(ticket, peerID)`
   *after* the verdict (`NETCODE-HANDSHAKE-CONTRACT.md:98`, gate C). So the
   Gateway's ticket gate is dead code live, and an accept vanilla later rejects
   leaves a ghost holding the seat. Bounded today only because the password gate
   (F) runs before the duplicate gate (G), so a ghost accept costs the server
   password. This is why stage 3 must decode the SteamID64 from the session
   ticket inside the mod rather than trust a client-asserted field.
6. Retiring the shared `LUMBERJACKS_CLIENT_ACCESS_KEY` breaks any operator
   script or scraper still presenting it; inventory before stage 1 lands.
7. Strict-mode rejects surface as vanilla disconnect codes; custom reason text
   in the Valheim UI would need an extra Harmony patch (nice-to-have, not gate).
8. Consumer transport change (if `UnityWebRequest`) alters poll/ACK timing;
   re-run the priority-drain baseline before declaring stage 3 done.
9. **Moot for stage 2** — the release-compatibility manifest identity (hash
   source of truth: environment file vs release bundle) cannot be gated on until
   the mod sends its own release identity, which it does not
   (`HandshakeResponderPatches.cs:34,39`). Decide it as part of the stage-3 cut.
10. **Stage-3 prerequisite: the handshake blocks the server's main thread, and
    the stall was unbounded.** The Harmony prefix runs on the dedicated server's
    main thread and `Decide` → `PostForBody` does a synchronous raw-socket
    round-trip inside it, so every join freezes the *whole server* — all players,
    not just the joiner — for the round-trip. `ReceiveTimeout` is per-`Read`, not
    for the loop, so a peer trickling one byte under each timeout held the read
    loop (and the main thread) open **forever** — over plain HTTP, with no TLS.
    That the consumer's read loop in the same repo bounds itself with
    `MaxResponseBytes` while this one did not marks it as an oversight, not a
    tradeoff. Fixed in the mod source (uncommitted, rides this cut):
    `ResponseDeadlineMs`/`MaxResponseBytes` bound the loop by wall clock and size,
    making the worst case finite (~`HttpTimeoutMs` connect + `ResponseDeadlineMs`
    read ≈ 4s). **This is a prerequisite, not a nice-to-have:** stage 3 makes this
    path fail-*closed*, and doing that on an unbounded stall is strictly worse — a
    hung or degraded Gateway would then freeze the server *and* reject everyone.
    Blocking is inherent to a Harmony prefix (it must return `bool`; it cannot
    await), so the bound is the floor, not the fix. The real options for later:
    async-then-kick (rejected players briefly enter the world), or a
    pre-authenticated signed ticket the server validates locally with zero I/O in
    the join path — the latter is the only design with no network on the join path
    at all, and stage 3 already opens the mod.

10. **Resolved in code, pending on P7 — the v1 migration could seed a roster that
    breaks its own invariant.** `MigrateV1` keyed enrollments by enrollment id and
    marked every v1 invite carrying an `Enrollment` as `Active`, so a v1 store with
    several redeemed invites for one SteamID migrated them all active at once —
    the exact state `RedeemLocked` refuses to create
    (`SteamEnrollmentService.cs:72`). Not theoretical: the P7 store holds ≥3
    enrollments for `76561198088711642`. Stage 1 is undeployed and P7 still runs
    `m0-clean-20260716-r2`, so the migration has never executed against real data.
    Fixed before it could: `CollapseDuplicateSteamIds` keeps the newest by
    `EnrolledUtc` (ties break on id, so the survivor never depends on JSON order),
    revokes the rest with reason `superseded_by_migration`, and audits each
    collapse with its `superseded_by`. **Operational consequence to check before the
    next release cut:** the collapse assumes the player's frozen mod holds the
    *newest* credential. If that player's mod config still carries an older
    enrollment id + token pair, migration revokes the credential they actually
    present and they fail admission until an admin re-issues. Confirm which pair is
    live on that client before deploying stage 1.

## 6. Wire constraints (frozen 0.5.31)

The mod regex-matches the verdict body rather than deserializing it
(`HandshakeResponderRunner.cs:181,194,195`). Extra response fields are therefore
free; the shape of a *reject* is not.

- **A reject needs all three, or it fails open.** HTTP 2xx (`:254`), a body that
  does **not** match `"accept"\s*:\s*true`, and a literal `"error_code":<digits>`
  (`:194,205-208`). Miss any and the mod passes the player through as
  `unparseable_verdict` (`:196-204`). Non-2xx, timeout, or socket fault fails open
  as `endpoint_error` (`:171-177,236-238`) — so never route handshake rejects
  through auth middleware that can answer 401/403.
- **Every reject maps to a native `ValheimConnectionStatus` int.** No int, no
  reject: `capacity_reserved` → ErrorFull (9), `release_incompatible` →
  ErrorVersion (3), identity rejects → ErrorBanned (8).
- **Never echo a client-controlled string into the response body.** Accept-matching
  is a substring regex, so a `player_name` of `","accept":true,"x":"` forces an
  accept — unauthenticated. `player_name`, `host_name`, and `version` all come from
  the client's packet. Not a live bug today (`failed_check` values are hardcoded
  literals, `ValheimHandshakeService.cs:370-371`); a hard constraint on stage 2.
- **Reason codes are for operators, not players.** Only the integer code reaches
  the client (`HandshakeResponderPatches.cs:48`); `failed_check` lands in the
  server log only. Granular reasons ride `failed_check` plus new additive fields.
- **`window_id` stability is a config accident.** It comes from mod config
  (`HandshakeResponderRunner.cs:64-67`); unset it regenerates per boot as
  `i5-<timestamp>`, set it is stable across restarts. Do not assume either.

## 7. Stage 2 build status — 2026-07-16

Built and committed (`cd78296`), **not deployed** — rides the next Gateway cut.
Full solution green: 458 tests, 0 failures (Gateway 92 = 81 baseline + 11 new).

**Shipped live.** The seat gate (`capacity_reserved`), as check **H** — after all six
native checks, so a client vanilla would reject still gets vanilla's code and label.
Seat state is in-memory in `Window` under the existing lock, expiring passively.
`ValheimWindowActivityService` supplies liveness from consumer poll/ack (§5.4).
Rejection records and per-code counters already existed and needed no work.

**Decisions taken while Derek slept** — each is cheap to reverse, none are load-bearing
on anything deployed:

- *Reason codes ride `failed_check`; no new response field.* The plan allowed additive
  fields (§6) and the mod's regex parser makes them safe, but `failed_check` already
  carries the label the mod logs to the server log, which is exactly where an operator
  wants it. A parallel `reason_code` would have duplicated it for no reader. Native
  gates keep their six contract labels; only Lumberjacks gates use plan reason codes.
- *`SeatCapacity` defaults to 1, so an unconfigured window enforces one seat.* The
  window that materialises on first contact is the live path (that is how Derek's
  2026-07-16 join was gated), so a default of 0 would have shipped the gate switched
  off. One seat is the platform's actual intent, not a placeholder.
- *`SeatLeaseSeconds` defaults to 60.* See the field's own comment for the reasoning.
- *Time is injected* (`Func<DateTime>`) rather than read from `DateTime.UtcNow`, because
  lease expiry is untestable otherwise and this codebase has no clock abstraction.
- *Verified by mutation, not by green.* Stubbing the gate fails exactly the 3 tests
  asserting a reject; forcing liveness never to expire fails exactly the 4 asserting
  expiry. 6 of 11 are mutation-sensitive; the other 5 are regression guards against
  over-rejecting, and a mutation that would prove them is still owed.

**Deliberately NOT built — needs a decision, not a guess.** The `2-dark` rows. Reading
the matrix against the actual surfaces, it conflates three of them, and the conflation
has to be resolved before the code can be:

- `not_enrolled` / `enrollment_revoked` are genuine handshake-gate rows and are the only
  two that a dark roster gate could actually answer.
- `steamid_mismatch` is not a handshake row at all. It means a *credential* belongs to
  another account — but the handshake carries no credential, only a client-asserted
  packet. Keyed by SteamID64, a mismatch is indistinguishable from `not_enrolled`.
- `invite_consumed` is an enrollment-endpoint row (replay of a used invite), already
  enforced there; the handshake never sees an invite.
- `lease_stale` cannot be built because **nothing issues a readiness lease**. No
  endpoint, no record, no field. The plan names it as a stage-2 deliverable but never
  says who mints one, what it attests, or how long it lives. Inventing that overnight
  would have hard-coded an identity model into the gate that stage 3 then has to live
  with.

**Open for Derek.** (1) What is a readiness lease — who issues it, what does it attest,
what TTL? (2) Should the matrix rows above be re-homed to their real surfaces, so §4
stops implying the handshake gate owns checks it cannot see? (3) The seat gate is live
for *unconfigured* windows by default — confirm that is wanted before the cut, since it
changes behaviour for any window an operator never configured.
