# Pairing-Token Authentication Design

## Overview

The Mail Triage REST API is protected by **bearer-token authentication** using short-lived
pairing tokens. The design is intentionally lightweight: no external identity provider is
required, and the token store lives in-process memory.

---

## Token Type and Transport

| Property | Value |
|---|---|
| Token type | Opaque bearer token (URL-safe Base64, 32 random bytes) |
| Header | `Authorization: Bearer <token>` |
| Default lifetime | 24 hours (configurable via `PairingToken:TokenExpiryHours`) |
| Storage | In-process `ConcurrentDictionary`; lost on restart |

---

## Protected vs Public Endpoints

### Public (no token required)
| Endpoint | Method | Notes |
|---|---|---|
| `/health` | GET | ASP.NET Core health readiness probe |
| `/alive` | GET | ASP.NET Core health liveness probe |
| `/api/pairing/token` | POST | Token provisioning — localhost-only in production |

### Protected (valid bearer token required)
| Endpoint | Method | Notes |
|---|---|---|
| `/api/accounts` | GET, POST | List / create mail accounts |
| `/api/accounts/{id}` | GET, PUT, DELETE | Read / update / delete an account |
| `/api/rules` | GET, POST | List / create forwarding rules |
| `/api/rules/{id}` | DELETE | Delete a rule |
| `/api/emails` | GET | Query triaged emails |
| `/api/triage` | POST | Manually triage a message |

---

## Token Provisioning

### Endpoint

```
POST /api/pairing/token
```

No request body is required. The response contains the new token and its expiry.

```json
{
  "token": "aB3xYz...",
  "expiresAt": "2026-04-15T22:17:13Z"
}
```

### Bootstrap Restriction

In production (`RequireLocalhostForProvisioning: true`, the default), this endpoint only
accepts connections from `127.0.0.1` / `::1`. Requests from any other IP receive **403
Forbidden**.

This prevents remote actors from minting tokens even if the API port is accidentally
exposed to the network.

To use from the command line on the same machine:

```bash
curl -s -X POST http://localhost:5093/api/pairing/token
```

### Initial/Bootstrap Token

For automated bootstrap (e.g., first-run scripting) you may pre-seed a token via
configuration:

```jsonc
// appsettings.Production.json  — use secrets management, NOT this file
"PairingToken": {
  "InitialToken": "<your-strong-random-token>"
}
```

> **Never commit a real token to source control.** Use `dotnet user-secrets`, environment
> variables, or a secrets manager.

---

## Configuration Reference

Bind from the `PairingToken` configuration section (e.g., `appsettings.json` or environment
variables):

| Key | Type | Default | Description |
|---|---|---|---|
| `TokenExpiryHours` | `int` | `24` | Lifetime of each issued token in hours |
| `RequireLocalhostForProvisioning` | `bool` | `true` | Restrict `/api/pairing/token` to localhost |
| `InitialToken` | `string?` | `null` | Pre-seed a token on startup (bootstrap) |

---

## Client Usage Example

```bash
# 1. Provision a token (run on the same machine as the service)
TOKEN=$(curl -s -X POST http://localhost:5093/api/pairing/token | jq -r '.token')

# 2. Use the token for subsequent API calls
curl -H "Authorization: Bearer $TOKEN" http://localhost:5093/api/accounts
```

---

## Implementation Notes

- **`PairingTokenService`** (singleton): generates URL-safe tokens using
  `RandomNumberGenerator`, stores them with expiry timestamps in a
  `ConcurrentDictionary`, and prunes expired entries on each `IssueToken` call.
- **`PairingTokenAuthHandler`**: custom `AuthenticationHandler<AuthenticationSchemeOptions>`
  that reads the `Authorization` header, extracts the bearer token, and delegates validation
  to `PairingTokenService`.
- Tokens are **never logged**. The `InitialToken` configuration value is treated as a secret.
- On process restart all in-memory tokens are invalidated; clients must re-provision.
- There is no token revocation endpoint in v0.1; restart the process to invalidate all tokens.

---

## Security Boundaries

| Threat | Mitigation |
|---|---|
| Remote token minting | Provisioning restricted to localhost by default |
| Token interception | Short-lived tokens (24 h); use HTTPS in production |
| Secret in logs | Handler never logs the bearer token value |
| Replay after restart | In-memory store; restart clears all tokens |
| Brute-force | 32-byte random token space ≈ 2²⁵⁶ (computationally infeasible) |
