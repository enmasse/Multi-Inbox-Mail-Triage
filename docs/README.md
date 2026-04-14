# Docs

- [v0.1 Backlog — Canonical Refined Format (TDD + integration-test requirements)](backlog-v0.1.md)

## Backlog summary

The backlog is structured into epics ordered by priority:

| Epic | Description | Priority |
|------|-------------|----------|
| A — Pairing-Token Auth | Bearer-token auth on write endpoints | P0 |
| B — Secret Storage (DPAPI) | `ISecretStore` abstraction + DPAPI implementation | P0 |
| C — Status & Operability | `/api/status` endpoint + polling telemetry | P0 |
| D — Failure Alerts | Polling + forwarding failure alerting | P0 |
| E — Windows Service | Service hosting mode + install scripts | P1 |
| F — Metrics | `/api/metrics` (Prometheus format) | P1 |
| G — Daily Digest | Scheduled email digest | P1 |
| H — UI (Avalonia) | Desktop app skeleton, pairing, dashboard, viewer | P2 |

All tasks follow the project **Definition of Done**: test-driven (Red/Green/Refactor), at least one integration test per feature, CI must pass (`dotnet test MailTriage.slnx`), no secrets in logs.