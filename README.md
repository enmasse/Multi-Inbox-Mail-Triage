# Multi-Inbox Mail Triage

An autonomous, agentic mail triaging service written in C# (.NET 9). It monitors multiple IMAP inboxes, uses a local LLM (Ollama) to categorize and prioritize incoming emails, stores results in SQLite, and can automatically forward emails based on configurable rules.

---

## Features

- **Multi-inbox IMAP monitoring** – configure any number of IMAP accounts; each is polled in parallel.
- **Local LLM triage** – integrates with [Ollama](https://ollama.com) (llama3.2 by default) to classify emails by category and priority, generate one-line summaries, and identify required actions.
- **SQLite persistence** – all accounts, triaged emails, and forwarding rules are stored in a local SQLite database via EF Core.
- **Forwarding rules** – automatically forward emails to specified addresses based on category, priority, sender pattern, or subject pattern.
- **REST API** – full HTTP API for managing accounts, viewing triaged emails, and configuring forwarding rules.
- **Autonomous background service** – runs as a long-lived ASP.NET Core hosted service with configurable polling intervals.

---

## Architecture

```
src/
  MailTriage.Core/            # Domain models & interfaces (no dependencies)
    Models/                   # MailAccount, TriagedEmail, ForwardingRule, enums
    Interfaces/               # IEmailRepository, ITriageService, IMailMonitorService, ...

  MailTriage.Infrastructure/  # Concrete implementations
    Data/                     # EF Core SQLite DbContext + EmailRepository
    Imap/                     # MailKit IMAP monitor + SMTP forwarder
    Llm/                      # Ollama HTTP client triage service

  MailTriage.Api/             # ASP.NET Core host
    BackgroundServices/       # MailPollingService (IHostedService)
    Controllers/              # AccountsController, EmailsController, RulesController, TriageController

tests/
  MailTriage.Tests/           # xUnit tests (27 tests)
```

---

## Prerequisites

| Tool | Purpose |
|------|---------|
| [.NET 9 SDK](https://dotnet.microsoft.com/download) | Build and run the service |
| [Ollama](https://ollama.com/download) | Local LLM for email triage |
| An IMAP account | Email inbox to monitor |
| *(Optional)* SMTP server | For email forwarding |

### Install Ollama model

```bash
ollama pull llama3.2
```

---

## Quick Start

### 1. Clone & build

```bash
git clone https://github.com/enmasse/Multi-Inbox-Mail-Triage
cd Multi-Inbox-Mail-Triage
dotnet build MailTriage.slnx
```

### 2. Configure

Edit `src/MailTriage.Api/appsettings.json` (or use environment variables / user secrets):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=mailtriage.db"
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2",
    "TimeoutSeconds": 60
  },
  "Smtp": {
    "Host": "smtp.example.com",
    "Port": 587,
    "Username": "you@example.com",
    "Password": "yourpassword",
    "FromAddress": "triage@example.com"
  }
}
```

### 3. Run

```bash
cd src/MailTriage.Api
dotnet run
```

The service starts on `http://localhost:5000` (and `https://localhost:5001`). The SQLite database is auto-created on first run.

---

## REST API

### Mail Accounts

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/accounts` | List all enabled accounts |
| `GET` | `/api/accounts/{id}` | Get a single account |
| `POST` | `/api/accounts` | Add a new IMAP account |
| `PUT` | `/api/accounts/{id}` | Update account settings |
| `DELETE` | `/api/accounts/{id}` | Remove an account |

**Add account example:**
```bash
curl -X POST http://localhost:5000/api/accounts \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Work Gmail",
    "host": "imap.gmail.com",
    "port": 993,
    "username": "you@gmail.com",
    "password": "app-password",
    "useSsl": true,
    "mailbox": "INBOX",
    "pollingIntervalSeconds": 60
  }'
```

### Triaged Emails

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/emails` | List triaged emails (filterable) |

Query parameters: `accountId`, `category`, `minPriority`, `skip`, `take` (max 200).

```bash
# Get urgent action-required emails
curl "http://localhost:5000/api/emails?category=ActionRequired&minPriority=High"
```

### Forwarding Rules

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/rules` | List forwarding rules |
| `POST` | `/api/rules` | Create a forwarding rule |
| `DELETE` | `/api/rules/{id}` | Delete a rule |

**Create rule example:**
```bash
curl -X POST http://localhost:5000/api/rules \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Forward Urgent",
    "forwardToAddress": "manager@example.com",
    "minPriority": 3,
    "isEnabled": true
  }'
```

### Manual Triage (for testing)

```bash
curl -X POST http://localhost:5000/api/triage \
  -H "Content-Type: application/json" \
  -d '{
    "subject": "Invoice #1234 due tomorrow",
    "fromAddress": "billing@vendor.com",
    "bodyText": "Your invoice of $500 is due tomorrow."
  }'
```

---

## Triage Categories & Priorities

| Category | Description |
|----------|-------------|
| `ActionRequired` | Email requires a response or action |
| `FYI` | Informational only |
| `Newsletter` | Marketing or newsletters |
| `Spam` | Unwanted email |
| `Meeting` | Meeting invites or scheduling |
| `Invoice` | Billing/invoice related |
| `Support` | Support tickets or requests |
| `Personal` | Personal correspondence |
| `Automated` | Automated system notifications |
| `Unknown` | Could not be classified |

| Priority | Value |
|----------|-------|
| `Low` | 0 |
| `Normal` | 1 |
| `High` | 2 |
| `Urgent` | 3 |

---

## Running Tests

```bash
dotnet test MailTriage.slnx
```

27 tests covering:
- Email repository CRUD and filtering
- Ollama triage service (JSON parsing, fallback, markdown handling, body truncation)
- IMAP monitor resilience (connection failures)
- Domain model defaults and enum semantics

---

## Configuration Reference

| Section | Key | Default | Description |
|---------|-----|---------|-------------|
| `ConnectionStrings` | `DefaultConnection` | `Data Source=mailtriage.db` | SQLite database path |
| `Ollama` | `BaseUrl` | `http://localhost:11434` | Ollama API URL |
| `Ollama` | `Model` | `llama3.2` | Ollama model name |
| `Ollama` | `TimeoutSeconds` | `60` | LLM request timeout |
| `Smtp` | `Host` | *(empty)* | SMTP server host (leave empty to disable forwarding) |
| `Smtp` | `Port` | `587` | SMTP port |
| `Smtp` | `UseSsl` | `false` | Use SSL/TLS directly (vs STARTTLS) |
| `Smtp` | `FromAddress` | *(empty)* | From address for forwarded emails |

---

## License

MIT
