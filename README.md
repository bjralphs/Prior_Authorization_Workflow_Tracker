# Prior Authorization Workflow Tracker

A healthcare-style workflow management application built with **ASP.NET Core Blazor Server (.NET 9)** and **SQL Server**. It models the real-world prior authorization (PA) process in which authorization specialists, treating providers, payer reviewers, billing managers, and administrators collaborate to submit, review, approve, deny, and track prior authorization requests for medical procedures and services.

---

## Features

- **Role-based workflow** — five roles (Authorization Specialist, Treating Provider, Payer Reviewer, Billing Manager, Administrator), each with a tailored dashboard and queue view
- **Full PA lifecycle** — Draft → Submitted → Under Review → Approved / Denied / PendingInfo → Appealed → Expired, with admin override capability
- **Filterable request queue** — filter by status, priority, date range, insurance plan, procedure code, reviewer, and patient name / MRN free-text search
- **Real-time dashboard KPIs** — per-role tiles, overdue alerts, recent activity feeds, denial reason breakdowns, and expiring authorization alerts
- **Reporting** — 7 report tabs including approval rates, provider volume, denial reasons, turnaround time; CSV export with 18 columns
- **Audit trail** — every state change, login event, and admin action is logged with user, timestamp, IP, and correlation ID
- **In-app notifications** — unread badge in NavMenu with 30-second polling and navigation refresh
- **Document tracking** — metadata-only document records per request (Specialist, Provider, Reviewer, Admin)
- **Automated expiration** — background `IHostedService` expires approved authorizations past their end date; admin-triggerable on demand
- **Toast notifications** — per-circuit Scoped service; no cross-tab bleed

---

## Technology Stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core Blazor Server (.NET 9) |
| Language | C# 13 |
| Database | SQL Server (LocalDB for dev, Express for prod) |
| ORM | Entity Framework Core 9.0.4 (code-first migrations) |
| Auth | ASP.NET Core Identity 9.0.4 |
| Validation | FluentValidation 11.11.0 |
| Logging | Serilog 8 (rolling file + console) |
| CSV Export | CsvHelper 33.0.1 |
| Testing | xUnit 2.9.2 + Moq 4.20.72 + Testcontainers.MsSql 3.10.0 |
| UI | Bootstrap 5 + Bootstrap Icons |

---

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)
- SQL Server LocalDB (ships with Visual Studio) **or** any SQL Server instance
- Visual Studio 2022 17.8+ **or** VS Code with C# Dev Kit

---

## Getting Started

### 1. Clone the repo

```bash
git clone https://github.com/<your-org>/Prior_Authorization_Workflow_Tracker.git
cd Prior_Authorization_Workflow_Tracker
```

### 2. Configure the connection string

The default connection string in `appsettings.json` targets SQL Server LocalDB:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=PaTracker;Trusted_Connection=True;"
}
```

To use a different SQL Server instance, override this in `appsettings.Development.json` (not committed) or via environment variable:

```bash
# PowerShell
$env:ConnectionStrings__DefaultConnection = "Server=.;Database=PaTracker;Trusted_Connection=True;"
```

### 3. Run the application

```bash
cd "Prior Authorization Workflow Tracker"
dotnet run
```

On first startup the application automatically:
1. Applies all EF Core migrations
2. Seeds demo users and sample data (Development environment only)

Navigate to `https://localhost:7014` (or `http://localhost:5235`).

---

## Demo Accounts

| Role | Email | Password |
|---|---|---|
| Authorization Specialist | specialist1@pademo.internal | Demo@1234! |
| Authorization Specialist | specialist2@pademo.internal | Demo@1234! |
| Treating Provider | provider1@pademo.internal | Demo@1234! |
| Treating Provider | provider2@pademo.internal | Demo@1234! |
| Treating Provider | provider3@pademo.internal | Demo@1234! |
| Payer Reviewer | reviewer1@pademo.internal | Demo@1234! |
| Billing Manager | billing1@pademo.internal | Demo@1234! |
| Administrator | admin@pademo.internal | Demo@1234! |

> **Note:** Demo accounts and seed data are only created in the `Development` environment.

---

## Running Tests

```bash
# Unit tests (200 tests, no external dependencies)
dotnet test Tests.Unit

# Integration tests (8 tests, requires Docker for Testcontainers SQL Server)
dotnet test Tests.Integration

# All tests
dotnet test
```

---

## Project Structure

```
Prior_Authorization_Workflow_Tracker.sln
├── Prior Authorization Workflow Tracker/   ← Main Blazor Server app
│   ├── Components/Pages/                   ← Razor pages (Home, Queue, RequestDetail, Reports, …)
│   ├── Components/Shared/                  ← StatusBadge, Toast, ConfirmationModal, Breadcrumb
│   ├── Data/                               ← AppDbContext, DbSeeder, Migrations
│   ├── Models/                             ← EF Core entities
│   ├── Services/                           ← Business logic (WorkflowService, PaRequestService, …)
│   ├── Pages/Account/                      ← Razor Pages for Login/Logout (Identity cookie auth)
│   └── appsettings.json
├── Tests.Unit/                             ← xUnit + Moq unit tests (200 tests)
├── Tests.Integration/                      ← xUnit + Testcontainers integration tests (8 tests)
└── docs/                                   ← Architecture, data model, testing strategy, deployment
```

---

## Documentation

| File | Contents |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Layered architecture, key design decisions, service patterns |
| [docs/data-model.md](docs/data-model.md) | Entity relationships, indexes, migrations |
| [docs/api-workflow.md](docs/api-workflow.md) | Workflow state machine, transition rules, business rules |
| [docs/testing-strategy.md](docs/testing-strategy.md) | Test coverage by service, mocking strategy |
| [docs/deployment.md](docs/deployment.md) | Build, migrate, deploy, environment configuration |
| [PRD.md](PRD.md) | Full product requirements document |

---

## License

This project is for demonstration and portfolio purposes.
