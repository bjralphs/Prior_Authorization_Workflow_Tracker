# Deployment and Setup

**Last Updated:** 2026-05-05
**Environment:** Development (LocalDB)

---

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 9.0 | `dotnet --version` |
| SQL Server LocalDB | Included with VS 2022 | Or SQL Server Express |
| dotnet-ef CLI | 10.0+ | `dotnet tool install -g dotnet-ef` |

---

## First-Time Setup

```bash
# 1. Clone the repository
git clone <repo-url>
cd Prior_Authorization_Workflow_Tracker

# 2. Restore packages
dotnet restore

# 3. Apply database migrations (creates PaTracker_Dev database in LocalDB)
dotnet-ef database update \
  --project "Prior Authorization Workflow Tracker" \
  --startup-project "Prior Authorization Workflow Tracker"

# 4. Run the application (migrations applied + seed data on first start)
dotnet run --project "Prior Authorization Workflow Tracker"
```

EF Core migrations are applied automatically at startup in **all environments** via `Database.MigrateAsync()` in `Program.cs`. Seed data runs only in Development environment via `DbSeeder.SeedAsync()`. Both steps are idempotent — running them multiple times is safe.

---

## Demo Accounts (§14.1)

| Email | Password | Role |
|---|---|---|
| `specialist@pademo.com` | `Demo@1234` | Authorization Specialist |
| `specialist2@pademo.com` | `Demo@1234` | Authorization Specialist |
| `provider@pademo.com` | `Demo@1234` | Treating Provider |
| `provider2@pademo.com` | `Demo@1234` | Treating Provider |
| `provider3@pademo.com` | `Demo@1234` | Treating Provider |
| `billing@pademo.com` | `Demo@1234` | Billing Manager |
| `reviewer@pademo.com` | `Demo@1234` | Payer Reviewer |
| `admin@pademo.com` | `Demo@1234` | Administrator |

The `system@pademo.internal` account is reserved for the `ExpirationJob` and cannot log in.

---

## Connection Strings

Development (`appsettings.Development.json`):
```
Server=(localdb)\mssqllocaldb;Database=PaTracker_Dev;Trusted_Connection=True
```

Production (`appsettings.json` — override via environment variable or secrets):
```
Server=(localdb)\mssqllocaldb;Database=PaTracker;Trusted_Connection=True
```

---

## Running Tests

```bash
# Unit tests only (no Docker required)
dotnet test Tests.Unit

# Integration tests (requires Docker Engine running)
dotnet test Tests.Integration

# All tests
dotnet test
```

Current: **199 unit tests + 8 integration tests = 207 tests passing**, 0 failures.

Integration tests use [Testcontainers.MsSql](https://testcontainers.com/modules/mssql/) to spin up `mcr.microsoft.com/mssql/server:2022-latest` automatically. Docker Engine must be running. The image (~1.5 GB) is pulled on first run and cached locally.

---

## Applied Migrations

| Migration | Date | Changes |
|---|---|---|
| `20260504213344_InitialCreate` | 2026-05-04 | Initial schema: all tables, indexes, FK constraints |
| `20260505023303_AddReportingViews` | 2026-05-05 | Four SQL reporting views (§7.2) |
| `20260505173749_AddAuthorizationNumberUniqueIndex` | 2026-05-05 | Unique partial index `UX_PaRequests_AuthorizationNumber` (BUG-01 DB safety net) |
| `20260505185343_AddReviewerAndProviderIndexes` | 2026-05-05 | `IX_PaRequests_ReviewerUserID` and `IX_PaRequests_ProviderID` (IDX-001/IDX-002, NFR-001) |

---

## Schema Changes

### Bug Fixes Applied (2026-05-04 – 2026-05-05)

| ID | Component | Change |
|---|---|---|
| BUG-01 / T-F1 | `WorkflowService.ExpireAsync` | Audit log for expiration events now writes with `UserID = SYSTEM` directly to DB. Previously wrote `UserID = ""` (no auth context in background job scope). |
| BUG-01 / §3.24.7 | `WorkflowService.GenerateAuthorizationNumber` | Replaced `Random.Shared` with `RandomNumberGenerator` (CSPRNG, 3 bytes → 6 hex chars, ~16.7M distinct values/day). |
| BUG-02 / T-F2 | `CsvExportService.ExportRequestsAsync` | `Priority` filter from `PaRequestFilter` now applied to the export query. Previously ignored, causing all-priority exports when user filtered by priority in the queue. |
| BUG-03 / T-F3 | `Queue.razor` | `[APPEAL]` text badge added to Appealed rows (PRD §8.7). Previously only row color was shown. |
| FR-004 / §3.27 | `WorkflowService.BeginReviewAsync` / `ApproveAsync` / `DenyAsync` | Methods now accept `PaStatus.Appealed` as a valid from-status in addition to `Submitted`/`UnderReview`. Previously threw `WorkflowTransitionException` when called on Appealed requests, despite the UI rendering action buttons for those states. |

### Refactoring Applied (2026-05-05)

| ID | Component | Change |
|---|---|---|
| T-R1 | `ReportingService` (5 methods) | Dashboard aggregation methods replaced `ToListAsync()` + in-memory counting with sequential server-side `CountAsync()` queries. Eliminates all-row data transfer to application memory. |
| T-R2 | `RequestDetail.razor` | `RenderActionButtons()` / `BuildButton()` / `ChildContent` helper pattern (low-level Blazor render builder API) replaced with readable `@if` + `<AuthorizeView>` Razor markup. No behavior change. |

### UX / Correctness Applied (2026-05-05)

| ID | Component | Change |
|---|---|---|
| REF-03 | `RequestDetail.razor RunActionAsync` | Added `await LoadUserNamesAsync()` after every workflow transition. Fixes stale reviewer name display when `BeginReviewAsync` sets a new `ReviewerUserID`. Previously required a full page reload to see the reviewer's name. |
| NavMenu refresh | `NavMenu.razor` | Notification badge now auto-refreshes every 30 seconds via `System.Threading.Timer` + `InvokeAsync`. Implements `IDisposable` to prevent timer leaks on circuit disconnect. Previously badge loaded once on page init only. |
| NavMenu LocationChanged | `NavMenu.razor` | Added `NavigationManager.LocationChanged` subscription so the badge snaps to the correct count on every navigation, not just every 30 s. Unsubscribed in `Dispose()` to prevent event handler leaks. |
| Admin dashboard | `Home.razor`, `AuditLog.razor` | Admin-specific dashboard panel added (PRD §12.1): Submission Volume This Month table (top-5 providers) + Quick Audit Search widget (navigates to `/admin/audit-log?user=...&entity=...`). `AuditLog.razor` now accepts `[SupplyParameterFromQuery]` for `user`, `entity`, and `action` to pre-populate filters from deep-links. |

| ID | Component | Change |
|---|---|---|
| BLIND-01 / T-U1 | `RequestDetail.razor` | Appeal deadline date shown in denial section when `IsAppealable = true`. Previously computed internally only for button enable/disable. |
| BLIND-02 / T-U2 | `NavMenu.razor` | Unread notification count badge rendered next to Notifications link. |
| BLIND-03 / T-U3 | `RequestDetail.razor` | Delete Draft button added for Specialist/Admin when status is Draft. Previously required navigating to Edit page. |

All schema changes go through EF Core migrations:

```bash
# Add a new migration
dotnet-ef migrations add <MigrationName> \
  --project "Prior Authorization Workflow Tracker" \
  --startup-project "Prior Authorization Workflow Tracker"

# Apply migrations
dotnet-ef database update \
  --project "Prior Authorization Workflow Tracker" \
  --startup-project "Prior Authorization Workflow Tracker"

# Generate SQL script (for review before prod apply)
dotnet-ef migrations script \
  --project "Prior Authorization Workflow Tracker" \
  --startup-project "Prior Authorization Workflow Tracker" \
  --output migrations.sql
```

---

## Logs

Development logs appear in the console.  
Production logs are written to `logs/pa-tracker-YYYY-MM-DD.log` (rolling daily, 30-day retention).

---

## Known Limitations (v1)

- No real file storage — document uploads store metadata only
- Email notifications are stubs — Notification rows created but no SMTP calls
- Single-organization — no multi-tenancy
- `ExpirationJob` uses a simple daily timer — no durable job queue (Hangfire/Quartz)
- All patient data is synthetic — not HIPAA compliant

See [../PRD.md §18](../PRD.md#18-known-limitations) for the authoritative list.
