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
| Join hook asks Gateway about actual joining SteamID | Native emulation only | Roster lookup keyed on the joining SteamID64 |
| Release/protocol compatibility gate | Not checked at admission | Manifest-pinned hash check in the handshake verdict |
| Fresh readiness lease | Not implemented | Lease issue/verify, expiry, single active lease |
| One-seat capacity reservation | `MaxPlayers = 10` const | Gateway-enforced reservation of exactly one seat |
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
2. **Strict admission decision + one-seat reservation** (Gateway only) —
   `Evaluate` consults the roster by joining SteamID64, checks release/protocol
   compatibility and a fresh readiness lease, enforces the single-seat
   reservation, returns actionable reason codes, retains rejection records.
   Verify with synthetic handshake tests covering the full Gate M1
   accept/reject matrix.
3. **Mod cut: fail-closed strict mode + TLS + server-derived recipient**
   (mod + Gateway, one release cut) — TLS-capable client with certificate
   validation; in strict cutover mode any endpoint fault becomes a reject
   (labeled native recovery stays an explicit operator mode); stop sending a
   client-chosen `consumer_id`. Verify live: Gateway stopped ⇒ strict mode
   refuses admission; https endpoint exercised end-to-end; no reusable
   credential on a plaintext public link (capture check).
4. **Bootstrap handoff hardening** (Gateway only) — replace the plaintext
   config echo with a one-use bootstrap consumed by the installer (M2
   consumes this; M1 only guarantees no reusable secret crosses the public
   link in cleartext). Verify replay of a used bootstrap fails.

Release cuts: stages 1–2 can ship as one Gateway release; stage 3 is the
single mod+Gateway cut; stage 4 rides the next Gateway cut. Every cut goes
through bundle validation and the promotion drill per the M0 pipeline.

## 4. Admission acceptance matrix (skeleton)

| Case | Setup | Expected decision | Reason surface | Test |
| --- | --- | --- | --- | --- |
| Enrolled + compatible + fresh lease + seat free | happy path | accept | — | auto + live |
| Uninvited SteamID | no enrollment record | reject | not_enrolled | auto + live |
| Revoked / expired enrollment | admin revoke; expiry passed | reject | enrollment_revoked / expired | auto |
| Wrong Steam account | enrollment for different SteamID64 | reject | steamid_mismatch | auto + live |
| Wrong mod/release | stale hash in handshake | reject | release_incompatible | auto |
| Stale readiness lease | lease older than TTL | reject | lease_stale | auto |
| Seat taken | second concurrent enrollee | reject | capacity_reserved | auto |
| Replayed invite / duplicate enrollment | reuse consumed invite | reject | invite_consumed | auto |
| Consumer token on producer/admin/reset/compaction/handshake ops | scoped credential | deny (403) | capability_denied | auto |
| Consumer names another recipient_id | forged recipient in poll/ACK | deny | recipient_forbidden | auto |
| Gateway down, strict mode | stop gateway container | reject at join | strict_authority_unavailable | live |
| Gateway down, native recovery mode | operator-labeled mode | native decision | labeled pass-through | live |
| Plaintext public link | http:// probe of volunteer surface | no reusable credential observable | redirect/refuse | live |

## 5. Risks and open questions

1. Mono/Unity TLS: the raw-socket client may need `SslStream` with explicit
   validation callback or a `UnityWebRequest` rewrite; cipher support on
   Valheim's Mono runtime must be proven early in stage 3.
2. Public DNS name for the volunteer endpoint is an operator decision and a
   stage-3 prerequisite (ACME needs it).
3. Confirm the handshake submission's `uid` is exactly the joining SteamID64
   in all join paths (crossplay off) before trusting it as the roster key.
4. Lease/seat recovery time after a client crash (no graceful disconnect)
   needs a declared TTL so the sole volunteer can rejoin quickly.
5. Retiring the shared `LUMBERJACKS_CLIENT_ACCESS_KEY` breaks any operator
   script or scraper still presenting it; inventory before stage 1 lands.
6. Strict-mode rejects surface as vanilla disconnect codes; custom reason text
   in the Valheim UI would need an extra Harmony patch (nice-to-have, not gate).
7. Consumer transport change (if `UnityWebRequest`) alters poll/ACK timing;
   re-run the priority-drain baseline before declaring stage 3 done.
8. The admission release-compatibility gate needs the manifest identity
   (hash source of truth) exposed to the Gateway config — decide whether that
   rides the environment file or the release bundle.
