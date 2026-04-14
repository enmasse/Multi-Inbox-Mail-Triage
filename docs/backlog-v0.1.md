# v0.1 Backlog — Canonical Refined Format

## Goals

Deliver a service that runs unattended, safely, with observable behaviour, and can be paired to a client/UI later.

## Non-Goals for v0.1

- Full UI polish, complex rule DSL, multi-user auth.

## Assumptions

- Windows-first target (DPAPI is the required secret-store implementation for v0.1).
- `dotnet test MailTriage.slnx` is the canonical CI test command.
- Integration tests may use in-process test servers (e.g. `WebApplicationFactory`) and stub dependencies (fake IMAP, fake Ollama, SQLite in-memory).

---

## Definition of Done

Every backlog item is **Done** when **all** of the following are true:

1. **Test-driven (Red → Green → Refactor):** tests are written (or extended) before or alongside the implementation; the test suite was failing before the change and passes after.
2. **Integration-test verifiable:** at least one integration test exercises the feature end-to-end (HTTP call in, observable side-effect out), using `WebApplicationFactory` or equivalent.
3. **Coverage:** new code paths are covered by tests (unit + integration). Prefer behavioural coverage; no untested public branches.
4. **CI passes:** `dotnet test MailTriage.slnx` is green; no flaky tests introduced.
5. **No secrets in logs:** tokens, passwords, and connection strings must not appear in application logs or test output.
6. **Docs updated:** any new endpoint, config key, or behaviour is reflected in `README.md` or `docs/`.

---

## Label Taxonomy

| Dimension | Values |
|-----------|--------|
| Priority  | `p0`, `p1`, `p2` |
| Area      | `area:auth`, `area:secrets`, `area:api`, `area:ops`, `area:alerts`, `area:service`, `area:metrics`, `area:digest`, `area:ui`, `area:imap`, `area:smtp`, `area:tests`, `area:docs` |
| Type      | `type:feature`, `type:chore`, `type:refactor`, `type:bug` |

---

## Agent Session Prompt Template

Use this template when spinning up a coding agent for any backlog task:

```
Goal: <one sentence — what the task delivers>
Task ID: <e.g. A1>
Constraints:
  - Work test-driven: write/extend tests before the implementation.
  - At least one integration test must verify the feature end-to-end.
  - No secrets or tokens in logs or committed files.
  - All tests must pass: `dotnet test MailTriage.slnx`
Files likely touched: <list key files/projects>
Verification: `dotnet test MailTriage.slnx` + <specific test class/method names if known>
```

---

## Release Intent

**v0.1 goal:** a service that can run unattended, safely, with observable behaviour, and can be paired to a client/UI later.

---

## Work Breakdown by Epic

Tasks are ordered by dependency within each epic. Epics are ordered by priority.

---

### EPIC A — Pairing-Token Auth `p0` `area:auth`

#### A0 — Auth contract and threat boundaries
- **Priority:** P0
- **Dependencies:** none
- **Deliverable:** short design note in `docs/` covering token type (bearer), TTL/expiry, rotation strategy, which endpoints are protected vs public, and local-only bootstrap constraints.
- **Acceptance criteria:**
  - `docs/auth-design.md` exists with threat model and decisions.
  - API contract for token issuance and validation is documented.
- **Test plan:** no code tests; document is the artefact. Subsequent tasks depend on the decisions made here.
- **Agent prompt:**
  > Goal: Produce a concise auth design note for pairing-token auth.
  > Task ID: A0
  > Constraints: documentation only; decisions must be security-conservative (local-only bootstrap, short-lived tokens preferred).
  > Files likely touched: `docs/auth-design.md`

#### A1 — Token validation middleware
- **Priority:** P0
- **Dependencies:** A0
- **Acceptance criteria:**
  - Protected endpoints (accounts, rules, triage write operations) return 401 when the `Authorization: Bearer <token>` header is absent or invalid.
  - Non-protected endpoints (`/health`, `/api/status`) remain accessible without a token.
  - No token value appears in logs.
- **Test plan:**
  - **Unit:** validator logic for missing/expired/malformed tokens.
  - **Integration:** `WebApplicationFactory` — call protected endpoint without token → 401; with valid token → 2xx.
- **Agent prompt:**
  > Goal: Implement bearer-token validation middleware on all write endpoints.
  > Task ID: A1
  > Constraints: TDD; integration test required; tokens must not leak into logs.
  > Files likely touched: `src/MailTriage.Api/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

#### A2 — Token provisioning endpoint
- **Priority:** P0
- **Dependencies:** A0, A1
- **Acceptance criteria:**
  - `POST /api/pairing/token` returns a token and expiry.
  - The returned token can immediately be used to call protected endpoints.
  - Endpoint is only callable from localhost (enforced or documented).
- **Test plan:**
  - **Integration:** provision token → use token on protected endpoint → success.
  - **Negative case:** use expired/revoked token → 401.
- **Agent prompt:**
  > Goal: Implement `POST /api/pairing/token` endpoint for token provisioning.
  > Task ID: A2
  > Constraints: TDD; integration test covering provision → use flow; no token in logs.
  > Files likely touched: `src/MailTriage.Api/Controllers/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

#### A3 — Safe logging for auth events
- **Priority:** P0
- **Dependencies:** A1
- **Acceptance criteria:**
  - Auth failures are logged with safe metadata only (timestamp, endpoint, reason code — no token values).
  - A unit test asserts that a token value does not appear in the formatted log output.
- **Test plan:**
  - **Unit:** test log-formatting helper / redaction utility.
- **Agent prompt:**
  > Goal: Ensure auth-related log entries never include raw token values.
  > Task ID: A3
  > Constraints: TDD; unit test for redaction; no functional change to auth logic.
  > Files likely touched: `src/MailTriage.Api/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

---

### EPIC B — Secret Storage (DPAPI) `p0` `area:secrets`

#### B0 — `ISecretStore` abstraction
- **Priority:** P0
- **Dependencies:** none
- **Acceptance criteria:**
  - `ISecretStore` interface exists in `MailTriage.Core`.
  - An in-memory implementation is available for use in tests.
  - No code outside `MailTriage.Infrastructure` references DPAPI directly.
- **Test plan:**
  - **Unit:** in-memory implementation — store, retrieve, missing key returns null/throws.
- **Agent prompt:**
  > Goal: Introduce `ISecretStore` abstraction with an in-memory test implementation.
  > Task ID: B0
  > Constraints: TDD; interface in Core; no DPAPI dependency in Core or Api projects.
  > Files likely touched: `src/MailTriage.Core/Interfaces/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

#### B1 — DPAPI implementation (Windows)
- **Priority:** P0
- **Dependencies:** B0
- **Acceptance criteria:**
  - `DpapiSecretStore` implements `ISecretStore` using `System.Security.Cryptography.ProtectedData`.
  - Explicit scope choice (CurrentUser) is documented.
  - Secrets round-trip (store → retrieve returns same value).
  - Wrong-scope decryption fails safely (exception, not silent corruption).
- **Test plan:**
  - **Unit (Windows-only, conditional):** round-trip test; wrong-scope test gated with `[SkippableFact]` (xunit.SkippableFact) or using `[SupportedOSPlatform("windows")]` combined with a runtime guard.
  - **CI:** ubuntu-latest runner must stay green (DPAPI tests skipped or excluded).
- **Agent prompt:**
  > Goal: Implement DPAPI-backed `ISecretStore` for Windows.
  > Task ID: B1
  > Constraints: TDD; Windows-only tests gated so ubuntu-latest CI stays green.
  > Files likely touched: `src/MailTriage.Infrastructure/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

#### B2 — Store IMAP/SMTP credentials via secret store
- **Priority:** P0
- **Dependencies:** B0, B1
- **Acceptance criteria:**
  - The database/config stores only a secret reference/key, not a plaintext password.
  - The service can still authenticate to IMAP and SMTP at runtime.
  - A migration note exists for anyone upgrading from a plaintext-password config.
- **Test plan:**
  - **Integration:** add account with secret reference → mock IMAP poll succeeds; assert DB row contains no plaintext password.
- **Agent prompt:**
  > Goal: Replace plaintext IMAP/SMTP passwords in DB with secret-store references.
  > Task ID: B2
  > Constraints: TDD; integration test asserts no plaintext password persisted; migration note in docs.
  > Files likely touched: `src/MailTriage.Infrastructure/Data/`, `src/MailTriage.Api/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

#### B3 — Secrets migration path
- **Priority:** P0
- **Dependencies:** B2
- **Acceptance criteria:**
  - A migration utility or documented one-time script imports existing plaintext passwords into the secret store.
- **Test plan:**
  - **Integration:** simulate legacy config → run migration → verify secret store populated, DB has no plaintext.
- **Agent prompt:**
  > Goal: Provide a one-time migration path from plaintext credentials to the secret store.
  > Task ID: B3
  > Constraints: TDD; integration test covers migration scenario.
  > Files likely touched: `src/MailTriage.Infrastructure/`, `docs/`
  > Verification: `dotnet test MailTriage.slnx`

---

### EPIC C — Status & Operability `p0` `area:ops` `area:api`

#### C0 — `/api/status` schema
- **Priority:** P0
- **Dependencies:** none
- **Deliverable:** documented schema in `docs/` covering: uptime, version/build info, per-account polling state (enabled, last poll time, last success, last error, last message processed), dependency health (DB reachable, Ollama reachable — non-blocking).
- **Acceptance criteria:**
  - Schema is stable and documented before implementation begins.
- **Test plan:** documentation artefact; schema used in C1 tests.
- **Agent prompt:**
  > Goal: Document the `/api/status` response schema.
  > Task ID: C0
  > Constraints: documentation only; schema must be non-blocking-safe (status returned even when dependencies are down).
  > Files likely touched: `docs/`

#### C1 — Implement `/api/status` endpoint
- **Priority:** P0
- **Dependencies:** C0
- **Acceptance criteria:**
  - Endpoint responds in < 100 ms under normal conditions.
  - When Ollama or DB is unreachable, endpoint still returns 200 with a degraded status object (does not throw/500).
  - Response matches the schema from C0.
- **Test plan:**
  - **Integration:** stub Ollama as down → GET `/api/status` → 200 with `ollamaReachable: false`.
  - **Integration:** normal conditions → all fields populated correctly.
- **Agent prompt:**
  > Goal: Implement `GET /api/status` endpoint that is resilient to dependency failures.
  > Task ID: C1
  > Constraints: TDD; integration test with Ollama stub down; response < 100 ms.
  > Files likely touched: `src/MailTriage.Api/Controllers/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

#### C2 — Background polling telemetry hooks
- **Priority:** P0
- **Dependencies:** C1
- **Acceptance criteria:**
  - Polling loop records last-run timestamp and last error per account to a shared state store.
  - `/api/status` reflects the updated per-account state after a poll cycle.
- **Test plan:**
  - **Unit:** state transitions in the telemetry store (idle → polling → success/error).
  - **Integration:** trigger a mock poll → GET `/api/status` → per-account fields updated.
- **Agent prompt:**
  > Goal: Hook polling loop to record per-account telemetry surfaced by `/api/status`.
  > Task ID: C2
  > Constraints: TDD; integration test validates status changes after a poll cycle.
  > Files likely touched: `src/MailTriage.Api/BackgroundServices/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

---

### EPIC D — Failure Alerts `p0` `area:alerts`

#### D0 — Alert channel decision and config
- **Priority:** P0
- **Dependencies:** none
- **Deliverable:** config schema and documented decision on alert channels (v0.1 minimum: structured log entry + optional SMTP email).
- **Acceptance criteria:**
  - `AlertOptions` config section defined and documented.
  - Defaults are safe (log only; SMTP alert disabled by default).
- **Test plan:**
  - **Unit:** config binding test — `AlertOptions` deserialises correctly from JSON.
- **Agent prompt:**
  > Goal: Define and document the alert channel config schema (`AlertOptions`).
  > Task ID: D0
  > Constraints: TDD; unit test for config binding; SMTP alert disabled by default.
  > Files likely touched: `src/MailTriage.Api/`, `docs/`
  > Verification: `dotnet test MailTriage.slnx`

#### D1 — Alert on repeated polling failures
- **Priority:** P0
- **Dependencies:** D0, C2
- **Acceptance criteria:**
  - After N consecutive IMAP polling failures for an account (N configurable, default 3), an alert is emitted.
  - Alerts use exponential back-off to prevent spam.
  - Alert content includes account name and sanitised error — no credentials in alert payload.
- **Test plan:**
  - **Unit:** back-off logic and consecutive-failure counter.
  - **Integration:** force N mock IMAP failures → assert alert emitted exactly once; N-1 failures → no alert.
- **Agent prompt:**
  > Goal: Emit an alert after N consecutive IMAP polling failures per account.
  > Task ID: D1
  > Constraints: TDD; integration test with mock IMAP failures; no credentials in alert payload.
  > Files likely touched: `src/MailTriage.Api/BackgroundServices/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

#### D2 — Alert on forwarding failures
- **Priority:** P0
- **Dependencies:** D0
- **Acceptance criteria:**
  - Failed email forwarding produces a durable record (DB or log) and triggers an alert.
  - Alert is emitted once per failure event, not on every retry.
- **Test plan:**
  - **Integration:** simulate SMTP send failure for a forwarding rule → assert alert emitted; assert failure recorded.
- **Agent prompt:**
  > Goal: Emit an alert and record durable failure when email forwarding fails.
  > Task ID: D2
  > Constraints: TDD; integration test with SMTP stub failure; one alert per failure event.
  > Files likely touched: `src/MailTriage.Infrastructure/Imap/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

---

### EPIC E — Windows Service Packaging `p1` `area:service`

#### E0 — Windows Service hosting mode
- **Priority:** P1
- **Dependencies:** C1, D1 (recommended)
- **Acceptance criteria:**
  - Service can run as a console application and as a Windows Service (`UseWindowsService()`).
  - Graceful shutdown (SIGTERM / SCM stop) completes without data loss.
  - Logs are accessible via Windows Event Log when running as a service.
- **Test plan:**
  - **Unit:** host composition test — service registers correctly with DI.
  - **Manual checklist:** install as service → start on boot → stop cleanly → logs visible. (Full service test not automatable in CI.)
- **Agent prompt:**
  > Goal: Add `UseWindowsService()` hosting mode with graceful shutdown.
  > Task ID: E0
  > Constraints: TDD; unit test for host composition; manual checklist documented in PR.
  > Files likely touched: `src/MailTriage.Api/Program.cs`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

#### E1 — Install and uninstall scripts
- **Priority:** P1
- **Dependencies:** E0
- **Acceptance criteria:**
  - PowerShell scripts for install and uninstall are provided.
  - Scripts are idempotent (re-running does not corrupt state).
  - README documents install/uninstall steps.
- **Test plan:**
  - **Manual:** run install script in a test VM; verify service appears and starts.
- **Agent prompt:**
  > Goal: Provide idempotent PowerShell install/uninstall scripts for the Windows Service.
  > Task ID: E1
  > Constraints: scripts must be idempotent; README updated.
  > Files likely touched: `scripts/`, `README.md`

---

### EPIC F — Metrics `p1` `area:metrics` `area:api`

#### F0 — `/api/metrics` endpoint (format decision + implementation)
- **Priority:** P1
- **Dependencies:** C2
- **Acceptance criteria:**
  - Endpoint exposes metrics in a documented format (Prometheus text format preferred).
  - At minimum: poll duration, emails processed, triage success/failure counts, forward success/failure counts.
  - Metric names and descriptions documented.
- **Test plan:**
  - **Integration:** perform a mock poll + triage cycle → GET `/api/metrics` → assert counters incremented.
- **Agent prompt:**
  > Goal: Implement `GET /api/metrics` in Prometheus text format with core counters.
  > Task ID: F0
  > Constraints: TDD; integration test validates counters after a poll/triage cycle; metrics docs updated.
  > Files likely touched: `src/MailTriage.Api/`, `tests/MailTriage.Tests/`, `docs/`
  > Verification: `dotnet test MailTriage.slnx`

#### F1 — Instrument additional metrics
- **Priority:** P1
- **Dependencies:** F0
- **Acceptance criteria:**
  - Additional metrics added: DB operation latency, queue depth, LLM call duration.
  - All metrics are stable and meaningful (no always-zero counters).
- **Test plan:**
  - **Unit:** verify histogram/gauge types register correctly.
  - **Integration:** DB write → DB latency histogram has at least one observation.
- **Agent prompt:**
  > Goal: Add DB latency, queue depth, and LLM call duration metrics.
  > Task ID: F1
  > Constraints: TDD; integration test validates each new metric type.
  > Files likely touched: `src/MailTriage.Infrastructure/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

---

### EPIC G — Daily Digest `p1` `area:digest` `area:smtp`

#### G0 — Digest specification (content, schedule, recipients)
- **Priority:** P1
- **Dependencies:** none
- **Deliverable:** documented spec covering: configurable schedule (cron or time-of-day), timezone handling, recipient list, digest content rules (top N emails by priority/category).
- **Acceptance criteria:**
  - `DigestOptions` config section documented.
  - Digest can be disabled via config.
- **Test plan:**
  - **Unit:** config binding test for `DigestOptions`.
- **Agent prompt:**
  > Goal: Define and document the daily digest config schema and content rules.
  > Task ID: G0
  > Constraints: TDD; unit test for config binding; digest disabled by default.
  > Files likely touched: `src/MailTriage.Api/`, `docs/`
  > Verification: `dotnet test MailTriage.slnx`

#### G1 — Digest scheduler
- **Priority:** P1
- **Dependencies:** G0
- **Acceptance criteria:**
  - Hosted service fires the digest at the configured time each day.
  - Uses an injectable clock abstraction to enable deterministic testing.
  - Duplicate sends within the same digest window are prevented.
- **Test plan:**
  - **Unit:** inject a fake clock → verify digest fires at the right time; does not fire twice in the same window.
  - **Integration:** fake clock advances → digest triggered → SMTP stub receives exactly one send.
- **Agent prompt:**
  > Goal: Implement the daily digest scheduler using an injectable clock.
  > Task ID: G1
  > Constraints: TDD; inject clock for deterministic tests; SMTP stub in integration test.
  > Files likely touched: `src/MailTriage.Api/BackgroundServices/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

#### G2 — Digest content generation
- **Priority:** P1
- **Dependencies:** G1
- **Acceptance criteria:**
  - Digest includes top triaged emails ranked by priority and category.
  - Email IDs/links are included in the digest body for traceability.
  - Content generation is deterministic for a given input set.
- **Test plan:**
  - **Unit:** given a fixed set of triaged emails → generated digest body matches expected content (snapshot or assertion-based).
- **Agent prompt:**
  > Goal: Implement deterministic digest content generation (top emails by priority/category).
  > Task ID: G2
  > Constraints: TDD; unit test with deterministic input → expected output.
  > Files likely touched: `src/MailTriage.Api/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

---

### EPIC H — UI (Avalonia) `p2` `area:ui`

> **Note:** UI epics are intentionally after P0/P1 so the service is secure and operable first.

#### H0 — Avalonia skeleton
- **Priority:** P2
- **Dependencies:** none
- **Acceptance criteria:**
  - Avalonia project added to solution, builds in CI.
  - Placeholder pages exist for: Pairing, Status, Triaged Emails.
- **Test plan:**
  - **Build test:** `dotnet build MailTriage.slnx` succeeds on ubuntu-latest (headless).
- **Agent prompt:**
  > Goal: Scaffold an Avalonia app project that builds in CI with placeholder pages.
  > Task ID: H0
  > Constraints: must build headless on ubuntu-latest; no functional logic yet.
  > Files likely touched: `src/MailTriage.App/`, `MailTriage.slnx`
  > Verification: `dotnet build MailTriage.slnx`

#### H1 — Pairing flow UI
- **Priority:** P2
- **Dependencies:** A2, B2, H0
- **Acceptance criteria:**
  - User can input service URL and token, connect, and store the token securely.
  - Token is not stored in plaintext in an obvious location.
- **Test plan:**
  - **Integration (API-level):** provision token via `POST /api/pairing/token` → use in API call → success.
  - **UI-level tests:** optional (Avalonia headless tests if feasible).
- **Agent prompt:**
  > Goal: Implement pairing flow UI — enter URL + token, connect, store token securely.
  > Task ID: H1
  > Constraints: TDD at API level; no plaintext token storage; UI tests optional but encouraged.
  > Files likely touched: `src/MailTriage.App/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

#### H2 — Status dashboard UI
- **Priority:** P2
- **Dependencies:** C1, F0, H0
- **Acceptance criteria:**
  - Renders data from `/api/status` and `/api/metrics`.
  - Handles degraded/offline service gracefully (shows last-known state or error message).
- **Test plan:**
  - **Integration (API-level):** `/api/status` + `/api/metrics` return expected shapes (covered by C1/F0 tests).
  - **UI-level:** optional.
- **Agent prompt:**
  > Goal: Implement status dashboard UI consuming `/api/status` and `/api/metrics`.
  > Task ID: H2
  > Constraints: handles degraded service gracefully; API-level integration tests must pass.
  > Files likely touched: `src/MailTriage.App/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

#### H3 — Triaged viewer UI
- **Priority:** P2
- **Dependencies:** H0
- **Acceptance criteria:**
  - Displays a list of triaged emails with filtering by category and priority.
  - Detail view shows triage result, summary, and forwarding rule matches.
- **Test plan:**
  - **Integration:** `GET /api/emails` with filters returns expected results (existing tests cover this).
  - **UI-level:** optional.
- **Agent prompt:**
  > Goal: Implement triaged email list + detail view with category/priority filters.
  > Task ID: H3
  > Constraints: re-use existing `/api/emails` integration tests; UI tests optional.
  > Files likely touched: `src/MailTriage.App/`, `tests/MailTriage.Tests/`
  > Verification: `dotnet test MailTriage.slnx`

---

## Items Reframed from Original Backlog

### Localhost Binding → Regression test task
- The service already binds to localhost by default (Kestrel default).
- **Action:** add regression test(s) asserting that the default configuration binds to `127.0.0.1` / `::1` only, not `0.0.0.0`.
- **Priority:** P1 `area:ops` `area:tests`
- **Acceptance criteria:** test asserts default binding; documentation confirms behaviour.

### IMAP Add+Test → Account validation endpoint
- Core IMAP monitoring already exists.
- **Action:** reframe as "add account validation endpoint (`POST /api/accounts/{id}/test`) and UX flow".
- **Priority:** P1 `area:imap` `area:api`
- **Acceptance criteria:** endpoint attempts an IMAP connection with stored credentials and returns success/failure; integration test uses a GreenMail stub or equivalent.
- **Test plan:**
  - **Integration:** POST to `/api/accounts/{id}/test` with valid stub IMAP config → 200 OK; with bad credentials → 400 (client configuration error); with unreachable IMAP server → 503 (service unavailable).

### Scripts → Deployment tooling
- **Priority:** P1 `area:service` `type:chore`
- **Acceptance criteria:** install/update/uninstall PowerShell scripts exist; dev bootstrap documented; scripts are idempotent.
- **Test plan:** manual verification checklist; CI lint of scripts optional.  
