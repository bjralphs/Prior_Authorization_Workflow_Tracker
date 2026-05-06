# Case Study: Prior Authorization Workflow Tracker

**Prepared for:** Senior Software Engineer portfolio  
**Last updated:** May 2026

---

## 1. Executive Summary

I designed and built the **Prior Authorization Workflow Tracker** — a healthcare-style workflow management application — to demonstrate senior-level full-stack engineering for a portfolio audience. The application models the real-world prior authorization (PA) process used in US healthcare, where authorization specialists, treating providers, payer reviewers, billing managers, and administrators collaborate to submit, evaluate, approve, and deny requests for medical procedures and services.

I owned the entire project: architecture, data model, service layer, Blazor UI, authorization model, audit system, test suite, and documentation. The result is a production-patterned application with 208 tests (200 unit, 8 integration), a 14-rule business rule validator, a 9-state workflow engine with optimistic concurrency, structured audit logging, role-differentiated dashboards, operational reporting with Chart.js visualizations, and a fully seeded demo environment — all delivered in a single cohesive codebase.

**Why this project matters:** Prior authorization is one of the most document-intensive and error-prone workflows in US healthcare. The problem domain is complex enough to require genuine architectural decisions — multi-actor RBAC, state-machine enforcement, audit compliance, background expiration — while remaining narrow enough to build end-to-end as a solo project. The result demonstrates that I can own architecture decisions, enforce business rules at the correct layer, write meaningful tests, and think about maintainability, not just feature delivery.

---

## 2. Context, Role, and Scope

**Project type:** Personal / portfolio (greenfield)  
**Role:** Sole contributor — architect, engineer, product owner, QA  
**Timeline:** Approximately 4–5 weeks of focused development  
**Scope:** Greenfield application built from a .NET 9 Blazor Server scaffold

I personally owned every decision:

- Domain modeling and database schema
- Service-layer architecture and business rule enforcement
- Workflow state machine and transition permission matrix
- Role-based access control at service and UI layers
- Audit logging system with independent transaction semantics
- Unit and integration test suites
- Background expiration job with manual admin trigger
- Reporting service and Chart.js-powered dashboard
- CSV export with 18-column schema
- Real-time in-app notification system
- Scoped CSS UI design including the custom manila folder navigation component

**Technologies:** ASP.NET Core Blazor Server (.NET 9), C# 13, EF Core 9.0.4, ASP.NET Core Identity 9.0.4, SQL Server LocalDB, FluentValidation 11.11.0, Serilog 8, CsvHelper 33.0.1, xUnit 2.9.2, Moq 4.20.72, Testcontainers.MsSql 3.10.0, Chart.js 4.4.4, Bootstrap 5.

**Constraints known at start:**

- No real PHI; all patient data is synthetic
- No real payer API integration (no X12 278 EDI)
- No HIPAA compliance scope — this is demo-only data
- No mobile-native UI — Blazor Server only
- Single-developer build; no code review cycle

---

## 3. Problem

**Domain problem:** Prior authorization is a mandatory gate in most US health insurance workflows. A provider orders a treatment; the insurer must approve it before the service is rendered or reimbursed. This process involves multiple actors operating under time constraints, with regulatory audit requirements and appeal windows that expire.

**Structural problem:** Without a structured tracker, requests get lost or expire silently. Denials go uncontested past their appeal deadlines. Billing teams have no visibility into request status without calling the payer by phone. Compliance teams cannot produce complete audit histories. Providers waste time chasing status updates.

**Portfolio problem:** Most .NET portfolio projects demonstrate CRUD — they do not demonstrate workflow enforcement, multi-actor RBAC, audit compliance, background job design, or meaningful test coverage. I wanted to build something that reflects the kinds of decisions a senior engineer makes in a real production environment: what belongs in the service layer, how to enforce business rules independent of the UI, how to test without hitting a database, and how to structure audit writes so they survive a rolled-back business transaction.

**Cost of doing nothing:** A system that only does CRUD does not demonstrate judgment. A system built around a domain this complex forces genuine architectural decisions and creates verifiable evidence of engineering depth.

---

## 4. Users and Stakeholders

The application models five user roles with distinct permissions, views, and workflows:

| Role | Primary concern | What they can do |
|---|---|---|
| Authorization Specialist | Submit and track requests | Create, submit, re-submit, appeal, withdraw, add comments and documents |
| Treating Provider | View status of their patients' requests | View own patients' requests, add clinical notes, view decisions |
| Payer Reviewer | Evaluate submitted requests | Begin review, approve, deny, request more information |
| Billing Manager | Monitor the financial pipeline | View approved/denied requests, export reports — read-only on workflow |
| Administrator | Full system oversight | All of the above, plus user management, audit log access, admin override with justification, manual expiration trigger |

**Role tensions that required design decisions:**

- A provider should never see another provider's patients — enforced at the query level in `PaRequestService.GetQueueAsync`, not in the UI.
- A billing manager needs reporting but must not modify workflow state — enforced at every service method via role guards, not relying on UI-level button hiding.
- A reviewer must not be able to see internal specialist comments that were marked `isInternal=true` — enforced in the comment query, not by CSS visibility.
- The SYSTEM user (background expiration job) needs to write audit entries without an authenticated HTTP context — solved via `LogAsSystemAsync` and a reserved non-loginable system user ID.

---

## 5. Research and Discovery

This was a self-directed project, so discovery meant learning the real PA workflow rather than interviewing users. I researched:

- The standard prior authorization process in US healthcare (CMS guidelines, payer SLA requirements by priority tier: Routine / Urgent / Emergent)
- The X12 278 EDI transaction format to understand what fields a real system requires
- Common failure modes in paper-based PA processes: expired authorizations, missed appeal windows, missing clinical justification, duplicate requests for the same patient/procedure/plan
- Appeal deadline structures: not all denial reasons are appealable, and deadlines vary by reason code

**Assumptions confirmed:**

- Multi-actor workflows require role guards at the service layer, not just the UI
- Audit compliance requires writes that survive rolled-back primary transactions
- Time-sensitive deadlines (appeal windows, SLA due dates, expiration) require a clock abstraction to be testable

**Assumptions revised during discovery:**

- I initially planned to use a single `UpdatedAt` timestamp to determine whether a PendingInfo request had new content before re-submission. I realized this was insufficient (a field edit without new comments/documents would satisfy the check incorrectly), so BR-013 instead reads `PaStatusHistory` for the exact timestamp when the request entered `PendingInfo`, then queries `PaComments` and `PaDocuments` for any records added after that timestamp.
- I initially considered a single dashboard for all roles. Discovery of the distinct operational concerns per role led to five separate dashboard panels, each backed by a dedicated async service method.

---

## 6. Constraints and Non-Goals

**Technical constraints:**

- Blazor Server's scoped CSS (`.razor.css`) applies a unique `b-xxxx` attribute only to elements directly rendered by the component — child components like `<NavLink>` do not receive it. This required a non-scoped inline `<style>` block for the custom navigation component.
- EF Core InMemory does not enforce FK constraints, does not support raw SQL, and cannot execute database views — so integration tests requiring concurrency (RowVersion), FK enforcement, or view correctness run against a real SQL Server instance via Testcontainers.
- Blazor Server circuits replace HTTP request scope — `HttpContext` is unavailable after the WebSocket upgrade. IP address capture for audit logs must happen at circuit construction time, not during Blazor event handlers.

**Explicit non-goals:**

- Real EHR integration or PHI storage
- Real payer API (X12 278 EDI)
- HIPAA compliance
- Billing adjudication or claims submission
- Patient-facing portal
- Mobile-native UI
- Real file storage (documents are metadata-only records in v1)

**Tradeoffs accepted:**

- Metadata-only document tracking (no byte storage) keeps the demo clean without a blob storage dependency — a known limitation documented in the PRD.
- The `ExpirationJob` fires daily at midnight UTC — production would use a configurable schedule; the hardcoded midnight interval is acceptable for demo scope.
- No pagination on audit logs — acceptable for demo data volume; documented as a future improvement.

---

## 7. Success Metrics

Because this is a portfolio project, success was measured against engineering quality criteria rather than user adoption metrics:

| Criterion | Target | Result |
|---|---|---|
| Workflow correctness | All 9 statuses, all valid transitions, all invalid transitions blocked | ✅ 41 WorkflowService tests covering every transition path |
| Business rule coverage | 14 rules (BR-001 – BR-014) enforced and tested | ✅ 26 BusinessRuleValidator tests with Theory variants |
| Service-layer RBAC | Every service method enforces role, not relying on UI | ✅ AuthorizationException tests for every blocked role |
| Audit independence | Audit writes survive rolled-back primary transactions | ✅ `AuditService` uses `IDbContextFactory` — own transaction per entry |
| Concurrency safety | Concurrent reviewer decisions produce a detectable conflict | ✅ Integration test against real SQL Server RowVersion |
| Test count | ≥ 200 total | ✅ 208 (200 unit + 8 integration), 0 failures |
| Clock testability | No direct `DateTime.UtcNow` calls in service layer | ✅ `IDateTimeProvider` throughout; `FakeDateTimeProvider` in tests |
| Demo-ready seed data | Immediately demonstrable without manual data entry | ✅ ~300 audit rows, realistic request distribution, all 8 accounts |

---

## 8. Ideation and Options Considered

### Workflow enforcement location

**Option A — UI-layer enforcement only:** Hide or disable buttons based on role. Fast to build. Rejected: no protection against direct service calls, fragile, and untestable at the unit level.

**Option B — Controller/endpoint guards:** Attribute-based authorization on Blazor event handlers. Rejected: Blazor Server does not have a clean controller-per-action model; attributes would scatter across many event handlers.

**Option C — Service-layer guards (chosen):** Every service method checks role and status before executing. Every blocked case throws a typed exception (`AuthorizationException`, `WorkflowTransitionException`, `BusinessRuleViolationException`). UI catches these and displays appropriate messages. This approach is testable without a browser, survives any future API or admin tool that calls the service directly, and keeps business logic concentrated in one layer.

### Audit write semantics

**Option A — Same `DbContext` as the primary operation:** Simple. Rejected: a rolled-back business transaction would silently discard the audit record — exactly the wrong behavior for a compliance-oriented system.

**Option B — Separate `DbContext` via `IDbContextFactory` (chosen):** Each audit write opens its own context and commits its own transaction. A failed business save cannot discard an audit entry. Audit failures are swallowed (logged to Serilog) so they never break the primary operation. The tradeoff is two DB round trips per audited operation — acceptable given the non-high-throughput nature of the domain.

### Concurrency for reviewer decisions

**Option A — Last-write-wins:** Simple, no code change. Rejected: two reviewers approving or denying the same request simultaneously produces no error, and the second write silently overwrites the first decision — a correctness failure in a healthcare context.

**Option B — Optimistic concurrency via `RowVersion` (chosen):** EF Core adds a `WHERE RowVersion = @p0` clause to every UPDATE. Concurrent decisions throw `DbUpdateConcurrencyException`, which `WorkflowService` surfaces as a user-visible conflict error. Cost: requires Testcontainers SQL Server in integration tests (InMemory does not enforce RowVersion).

### Dashboard design

**Option A — Single shared dashboard:** Simple. Rejected: a billing manager needs top-denial-reasons and approval rates; a reviewer needs pending-over-5-days and average decision time; a specialist needs overdue alerts and a recent activity feed. One panel cannot serve all roles without becoming cluttered and irrelevant.

**Option B — Role-differentiated panels (chosen):** Five async service methods (`GetSpecialistDashboardAsync`, `GetProviderDashboardAsync`, `GetReviewerDashboardAsync`, `GetBillingDashboardAsync`) called concurrently via `Task.WhenAll`. Each panel surfaces the KPIs meaningful to that role. Cost: more service methods and more tests. Accepted: the alternative is a dashboard that serves no one well.

### Background expiration job

**Option A — On-demand check only:** Triggered by admin or by a cron job external to the app. Rejected: requires external orchestration for a demo app with no infrastructure.

**Option B — `IHostedService` with manual trigger (chosen):** `ExpirationJob` is registered as both `BackgroundService` (fires daily at midnight) and `IExpirationJobTrigger` (manual admin trigger via singleton registration). Admin can click "Run Expiration Check" from the UI and see the expired-count in a toast. The same singleton is the hosted service — no duplicate implementation.

---

## 9. Design

### System architecture

A four-layer separation with strict dependency direction:

```
Razor Components (Pages + Shared + Layout)
        ↓ injects via DI
Service Layer (WorkflowService, PaRequestService, ReportingService,
               AuditService, NotificationService, UserManagementService,
               CsvExportService, ExpirationJob)
        ↓ EF Core
Data Layer (AppDbContext, DbSeeder, Migrations, SQL Views)
        ↓ SQL Server wire protocol
SQL Server (LocalDB dev / Express prod)
```

No business logic in Razor components. No direct `DbContext` access from components. All Razor pages inject services and display results.

### Database design decisions

- **Soft delete with global query filter:** `PaRequest.IsDeleted` is filtered globally in `OnModelCreating`. No query can accidentally surface deleted requests without calling `IgnoreQueryFilters()` explicitly. Draft hard-delete (BR-014) deletes the record only for drafts created by their owner or by an administrator.
- **`RowVersion` optimistic concurrency:** On every `PaRequest` UPDATE, EF Core includes `WHERE RowVersion = @p0`. Concurrent reviewer decisions produce a detectable `DbUpdateConcurrencyException`.
- **`PaStatusHistory`:** Every state transition writes an immutable history row with `FromStatus`, `ToStatus`, `ChangedByUserID`, `ChangeReason`, and `ChangedAt`. This is the source of truth for BR-013 (re-submission requires new content added after the PendingInfo entry timestamp) and for turnaround time reporting.
- **SQL views for reporting:** `vw_*` views perform the aggregate queries (status counts, denial breakdowns, monthly volume, provider volume) at the database layer. `ReportingService` uses EF Core raw SQL to query them. This keeps aggregate queries in SQL where they belong, not in LINQ that produces fragile in-memory grouping.
- **`DenialReason.IsAppealable` + `AppealDeadlineDays`:** Appealability and deadline are properties of the denial reason, not hardcoded constants. A non-appealable denial reason produces no appeal window notification and renders a disabled "Appeal window closed" button in the UI.

### Workflow state machine

Nine statuses: `Draft → Submitted → UnderReview → Approved / Denied / PendingInfo → Appealed → Expired / Withdrawn`. Each transition is enforced in `WorkflowService` against a `Transitions` dictionary and a role permission matrix. Any call with an invalid from-status or unauthorized role throws a typed exception.

Three transitions originate from `Appealed` (→ UnderReview, → Approved, → Denied). These required explicit handling: `BeginReviewAsync`, `ApproveAsync`, and `DenyAsync` each accept their Appealed origin as a valid from-status. The `FromStatus` is captured before any mutation so `PaStatusHistory` and audit logs correctly record `Appealed` as the prior state.

### RBAC design

Two enforcement layers:

1. **Service layer:** Every method begins with a role check via `ICurrentUserService.IsInRole()`. Blocked calls throw `AuthorizationException`. This is the authoritative layer.
2. **UI layer:** Buttons and action panels are conditionally rendered based on role — but only as a UX convenience. The UI cannot be the only enforcement point.

### Permission model highlights

- A provider sees only their own patients' requests (enforced in the `GetQueueAsync` EF Core query predicate)
- A provider cannot add internal comments (`isInternal=true`) — enforced per comment in `AddCommentAsync`
- A billing manager cannot upload documents — enforced per upload in `AddDocumentAsync`
- Only a specialist or admin can file an appeal — enforced in `AppealAsync`
- The system user (expiration job) cannot be logged in — it is a seeded non-loginable account with a fixed reserved GUID

### Audit design

`AuditService` uses `IDbContextFactory<AppDbContext>` to open its own `DbContext` per log entry — committed in its own transaction. This means:
- A rolled-back business transaction cannot discard an audit row
- Audit failures are swallowed (logged to Serilog) — they never break the primary operation
- `LogAsync` captures `UserID`, `UserName`, `IpAddress`, and `CorrelationId` from `ICurrentUserService` and `IHttpContextAccessor`
- `LogAsSystemAsync` uses the hardcoded system user ID — called by `ExpirationJob` where no authenticated user context exists

`CorrelationIdMiddleware` stamps every HTTP request with a GUID in `HttpContext.Items["CorrelationId"]`. `AuditService` reads this value and stores it with each audit row, enabling complete request tracing across audit entries.

### Notification design

`NotificationService` creates per-user `Notification` records for appeal windows, expiration events, and PendingInfo state changes. `NavMenu.razor` polls `GetUnreadCountAsync` every 30 seconds and displays an unread badge. The 30-second polling interval avoids SignalR complexity while keeping the badge reasonably current for a demo.

---

## 10. Implementation

### WorkflowService — state machine

`WorkflowService` owns all status transitions. A `Transitions` dictionary maps `(FromStatus, ToStatus)` pairs to their required role. Every transition method:
1. Checks the role guard
2. Validates the from-status
3. Applies FluentValidation business rules where applicable
4. Updates `PaRequest.Status` and writes a `PaStatusHistory` row
5. Writes an audit entry via `AuditService.LogAsync`
6. Creates notifications where applicable
7. Saves with EF Core (which enforces RowVersion on the UPDATE)

### BusinessRuleValidator — FluentValidation

14 rules enforced as FluentValidation validators, completely decoupled from the UI:

- BR-001: Procedure requires prior auth
- BR-002: Decision due date = submitted date + SLA days per priority tier
- BR-003: Diagnosis code matches ICD-10 regex `^[A-Z][0-9]{2}(\.[0-9A-Z]{1,4})?$`
- BR-004: Units requested 1–999
- BR-005: No duplicate active request for same patient + procedure code + insurance plan
- BR-006: Authorization end date > start date
- BR-007: Units granted ≤ units requested
- BR-008: Appeal only within denial reason's appeal deadline
- BR-009: Cannot withdraw from Approved, Expired, or Denied
- BR-010: Clinical justification ≥ 50 characters
- BR-011: Authorization start date ≥ today
- BR-012: Units granted ≥ 1
- BR-013: PendingInfo re-submission requires new comment or document added after PendingInfo entry
- BR-014: Hard delete only for Draft by creator or Administrator

### IDateTimeProvider — clock abstraction

No direct `DateTime.UtcNow` calls in the service layer or in Razor components. `SystemDateTimeProvider` returns the real clock; `FakeDateTimeProvider` returns a mutable fixed value. Used in: BR-011 (start date validation), BR-008 (appeal deadline), `RequestDetail.razor` (appeal window display, overdue highlighting), expiration checks.

### ICurrentUserService — circuit-scoped identity

Registered as Scoped (one per Blazor circuit). `IP address` is captured at construction time from `IHttpContextAccessor` — before the connection upgrades to WebSocket, at which point `HttpContext` becomes unavailable. `IsActive` is cached after the first DB lookup per circuit to avoid repeated queries.

### ExpirationJob — hosted service + manual trigger

`ExpirationJob` extends `BackgroundService` and implements `IExpirationJobTrigger`. Both are the same singleton instance:

```csharp
builder.Services.AddSingleton<ExpirationJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ExpirationJob>());
builder.Services.AddSingleton<IExpirationJobTrigger>(sp => sp.GetRequiredService<ExpirationJob>());
```

This allows the Admin `Users.razor` page to inject `IExpirationJobTrigger` and trigger the same logic on demand, returning an expired-count displayed in a toast — without duplicating any job logic.

The job queries `PaRequests WHERE Status = 'Approved' AND AuthorizationEndDate < UtcNow AND IsDeleted = 0`, transitions each result via `WorkflowService.ExpireAsync`, creates notifications for the specialist and provider, and calls `AuditService.LogAsSystemAsync`. A per-request try/catch isolates individual failures — one failed expiration does not abort the rest.

### N+1 elimination — UserManagementService

`GetAllUsersAsync` previously called `UserManager.GetRolesAsync` for every user — O(N) round trips. The fix executes a single JOIN between `AspNetUserRoles` and `AspNetRoles`, materializes with `ToListAsync`, then groups in memory. This pattern works with both SQL Server and the EF Core InMemory provider used in unit tests.

### Authorization number uniqueness

`WorkflowService.GenerateAuthorizationNumber` generates `AUTH-{date}-{6 hex chars}` using `RandomNumberGenerator.GetBytes`. A unit test fires 100 concurrent calls and asserts zero collisions — P(collision) ≈ 0.03% over 100 calls with a 6-byte (48-bit) random component.

---

## 11. Validation, Testing, and Quality

### Test architecture

| Project | Framework | Count | Purpose |
|---|---|---|---|
| `Tests.Unit` | xUnit 2.9.2 + Moq 4.20.72 | 200 | Service layer logic, workflow rules, business rule validation |
| `Tests.Integration` | xUnit + Testcontainers.MsSql 3.10.0 | 8 | RowVersion concurrency, SQL view smoke tests, unique index enforcement |

**Why Testcontainers for integration tests:** EF Core InMemory does not enforce FK constraints, cannot execute raw SQL, and does not support database views. Integration tests requiring concurrency (RowVersion), FK enforcement, or view correctness run against a real SQL Server instance in Docker.

### What the tests cover

- **WorkflowService:** 41 tests covering all 9 valid transitions (including the 3 Appealed-origin paths that previously threw incorrectly), 3 invalid transitions, concurrent decision detection, appeal window enforcement, admin override, withdrawal guard (BR-009), zero-units guard (BR-012), and authorization number uniqueness under load
- **BusinessRuleValidator:** 26 tests — one or more per rule, Theory variants for BR-003 (multiple invalid and valid ICD-10 formats), BR-004 (boundary values), BR-008 (inclusive deadline)
- **PaRequestService:** 25 tests — draft CRUD, queue filters including PatientSearch free-text, comment RBAC (provider blocked from internal comments), document upload RBAC (billing manager blocked), BR-014 delete ownership
- **AuditService:** 6 tests — factory independence, fault tolerance (exception swallowed), user context, old/new values, CorrelationId capture, CorrelationId null when no HttpContext
- **NotificationService:** 8 tests — create, unread query, count, markRead (own record), markRead (wrong owner silently ignored — enumeration attack prevention), markAllRead
- **ReportingService:** 36 tests — all 7 report types including role-specific dashboard panels (specialist overdue, reviewer avg decision time, billing pending count), approval rate per priority, recent activity feed
- **CsvExportService:** 7 tests — RBAC, 18-column header, filename format, audit event written, status filter, priority filter (regression for BUG-02)
- **ExpirationJob:** 5 tests — no candidates, one expired, active request skipped, double-expiration guard, per-request failure isolation (one failure does not abort others)
- **Integration (Testcontainers):** 8 tests — concurrent ReviewerService decisions produce `DbUpdateConcurrencyException`, SQL view aggregates return expected rows, unique index blocks duplicate active requests

### Key bug caught by tests

- **BR-013 regression:** An early implementation checked `PaRequest.UpdatedAt` to determine if new content existed. Tests revealed this allowed a field edit (e.g., updating justification text) to satisfy the re-submission requirement without adding a comment or document. The fix reads `PaStatusHistory` for the PendingInfo entry timestamp and queries `PaComments`/`PaDocuments` for records added after it.
- **Appealed-origin transitions:** Three `WorkflowService` methods (`BeginReviewAsync`, `ApproveAsync`, `DenyAsync`) incorrectly threw `WorkflowTransitionException` when called from `Appealed` status, despite the `Transitions` dictionary listing them correctly. Tests uncovered that the guard logic was hardcoding the expected from-status. Fixed by explicitly accepting the Appealed origin in each method.
- **CSV priority filter:** `CsvExportService` applied all queue filters except `Priority` — the priority field was passed through the filter DTO but not applied to the query predicate. A regression test (`ExportRequestsAsync_PriorityFilter_OnlyExportsMatchingRows`) catches this class of filter omission.

---

## 12. Launch, Rollout, and Adoption

This is a portfolio project; there is no production launch or user adoption to report.

**Demo environment:** The application seeds automatically on first startup in the Development environment:
- 8 demo accounts across all 5 roles
- Synthetic PA requests covering all 9 statuses and both priority tiers
- Realistic patient MRNs, procedure codes, diagnosis codes (ICD-10 format)
- ~300 audit log entries spanning 30 days with sequential correlation IDs
- Expiration-eligible requests for the admin to trigger manually
- Dates computed relative to `DateTime.UtcNow` so the demo data stays fresh on every clean install

**Demonstrability:** Any reviewer can clone the repo, run `dotnet run`, and navigate to `http://localhost:5235` without any manual data setup. Logging in as `admin@pademo.com` / `Demo@1234` provides full-system access; logging in as any other role shows the role-differentiated dashboard and permission-gated UI.

---

## 13. Outcome and Impact

**Engineering outcomes:**

- 208 tests, 0 failures — the full service layer is covered without integration test infrastructure for the majority of cases
- 14 business rules enforced at the service layer and verified by dedicated test cases
- 9-state workflow with 18 valid transitions, all permission-gated and audited
- Audit writes survive rolled-back primary transactions by design
- Concurrent reviewer decisions produce a detectable conflict via RowVersion — not a silent last-write-wins
- N+1 eliminated in user management (`O(N)` → 1 query)
- Authorization number uniqueness verified under simulated burst load
- All time-sensitive logic is testable via clock abstraction — no flaky time-dependent tests

**Portfolio outcomes:**

- Demonstrates multi-actor RBAC enforced at the service layer, not the UI
- Demonstrates workflow state machine design with typed exceptions for invalid transitions
- Demonstrates audit logging with compliance-grade transaction semantics
- Demonstrates FluentValidation as a service-layer concern, not a form-binding concern
- Demonstrates background job design with both scheduled and on-demand triggers
- Demonstrates test architecture that separates unit tests (InMemory) from integration tests (Testcontainers) based on what each provider can and cannot support

**What would be different in a real production system:**

- Formal performance/load testing against realistic data volumes
- Pagination on audit logs and the request queue (currently unbounded)
- Real file storage for documents (currently metadata-only)
- Real payer API integration (X12 278)
- HIPAA-compliant PHI handling
- Monitoring, alerting, and structured log aggregation beyond Serilog file sinks

---

## 14. Collaboration and Leadership

As the sole contributor, I acted as my own architecture reviewer, product owner, and QA. The decisions I document here are the ones I would expect to defend in a design review:

- Why the service layer enforces RBAC rather than relying on UI guards
- Why `AuditService` uses `IDbContextFactory` rather than the shared `DbContext`
- Why integration tests use Testcontainers rather than EF Core InMemory
- Why `ExpirationJob` implements both `BackgroundService` and `IExpirationJobTrigger` rather than having a separate trigger service
- Why BR-013 reads `PaStatusHistory` rather than `PaRequest.UpdatedAt`
- Why `IDateTimeProvider` is injected into Razor components, not just services

The documentation — PRD, architecture.md, data-model.md, workflow.md, testing-strategy.md, deployment.md — is written to the standard I would expect from a team project: enough for a new engineer to understand the system, run it, and extend it without asking the original author.

---

## 15. Risks, Tradeoffs, and Failure Modes

### Risks accepted

**Metadata-only document storage:** Documents are tracked as database records (filename, type, size, uploader, timestamp) with no actual bytes stored. This was a deliberate v1 scope decision to avoid a blob storage dependency in a demo app. A real system would need Azure Blob Storage, S3, or equivalent, with a storage path reference in the `PaDocument` record. The schema already includes a `StoragePath` column to support this future extension.

**30-second notification polling:** The NavMenu polls `GetUnreadCountAsync` every 30 seconds using a `System.Threading.Timer`. This is acceptable for demo volumes but would not scale without connection pooling tuning and query optimization on high-throughput installations. A production-grade approach would use SignalR push from the server when a notification is created.

**Midnight UTC expiration schedule:** The expiration job fires at a hardcoded midnight UTC interval. A production system would expose this as a configurable setting (e.g., in `appsettings.json`) and likely use a more robust job scheduler (Hangfire, Quartz.NET) for retry, observability, and concurrency control across multiple app instances.

**Single SQL Server instance:** The architecture assumes a single writable SQL Server. Horizontal scale-out (multiple Blazor Server nodes sharing a database) would work for the data layer but would require sticky sessions or a distributed session store for Blazor Server circuit state. This is not a concern for a single-server demo but would need to be addressed before a multi-node deployment.

### Failure modes that are handled

- **Concurrent reviewer decisions:** `DbUpdateConcurrencyException` is caught by `WorkflowService` and surfaced as a user-visible conflict message. The second reviewer sees the error and must reload the request to see the current state.
- **Audit write failure:** `AuditService` swallows exceptions from its own `DbContext` operations and logs them to Serilog. Audit failures never break the primary operation.
- **Per-request expiration failure:** `ExpirationJob` wraps each request expiration in a try/catch. One failed expiration (e.g., a concurrency exception on a request that was just manually resolved) does not abort the remaining candidates.
- **Double-expiration guard:** `WorkflowService.ExpireAsync` checks `Status == Approved` before updating. If the job runs twice (e.g., after a manual admin trigger immediately before midnight), the second run finds no eligible candidates and returns 0.
- **Inactive user access:** `ICurrentUserService` checks `IsActive` on construction and caches the result. An admin-deactivated user whose circuit is still connected cannot successfully call any service method — the `IsActive` guard fires at the service layer.

### Largest known weakness

The absence of pagination is the most significant structural gap. The request queue, audit log, and reporting tables currently return all matching records. For a real production dataset, these queries would need server-side pagination to avoid unbounded result sets. The service interfaces already accept filter DTOs, so adding `Skip`/`Take` parameters is straightforward — it was deferred as a known limitation.

---

## 16. Reflection and Next Steps

### What I would do differently

**Add pagination from day one.** I deferred it as a known limitation, but retrofitting `Skip`/`Take` into established service interfaces and UI components is more work than including it in the initial design. The filter DTOs could have included `PageNumber`/`PageSize` parameters from the start.

**Add observability beyond Serilog file sinks.** The application logs structured events to a rolling file, but there is no health check endpoint, no metrics emission, and no request tracing dashboard. A production readiness pass would add `/health` (EF Core health check), OpenTelemetry traces, and a basic Prometheus metrics endpoint.

**Consider a more robust job scheduler.** `IHostedService` works for a demo, but Hangfire or Quartz.NET would provide retry, persistence across restarts, concurrency control across multiple instances, and a management UI — all of which matter in production.

### What I would measure next

- Query latency per service method at realistic data volumes (1,000+ requests, 5 years of audit rows)
- Notification polling impact on connection pool utilization at concurrent user counts
- Authorization number collision probability in production burst scenarios (currently validated at 100 concurrent calls; a real system might see 10,000/day)

### What the project taught me

**Architecture decisions compound.** The choice to use `IDbContextFactory` in `AuditService` was small — one line in `Program.cs` and a different constructor parameter — but it changed the transactional semantics of every audit write in the system. The choice to inject `IDateTimeProvider` into Razor components, not just services, meant that every time-sensitive UI behavior (overdue date highlighting, appeal window display) is also testable without mocking the system clock at the OS level.

**Tests constrain design in useful ways.** Several design decisions — the clock abstraction, the `FakeCurrentUserService` factory methods, the `AuditService` independence — emerged directly from the need to make the code testable. The test suite is not a validation layer sitting on top of the design; it is a design input.

**Role-differentiated UX requires more design surface than a shared dashboard.** Building five distinct dashboard panels required five service methods, five sets of tests, five Razor rendering branches, and five product decisions about which KPIs matter to which role. This was significantly more work than a single shared panel, and it is also significantly more useful — a billing manager does not need to know how many requests are under review; they need to know the approval rate and the top denial reasons.

---

## 17. Artifacts

| Artifact | Location | What it demonstrates |
|---|---|---|
| Architecture overview | [docs/architecture.md](architecture.md) | Layered design, key decisions (concurrency, audit independence, clock abstraction, N+1 fix) |
| Workflow state machine | [docs/workflow.md](workflow.md) | Mermaid state diagram, transition permission matrix, business rules table, audit event table |
| Data model | [docs/data-model.md](data-model.md) | ER diagram, all tables, indexes, FK relationships |
| Testing strategy | [docs/testing-strategy.md](testing-strategy.md) | Coverage targets by service, mocking strategy, full test scenario checklist |
| PRD | [PRD.md](../PRD.md) | Product requirements, business rules, non-functional requirements |
| Unit tests | `Tests.Unit/` | 200 passing tests, service-layer coverage |
| Integration tests | `Tests.Integration/` | 8 Testcontainers-backed tests: RowVersion concurrency, SQL views, unique index |
| Service layer | `Prior Authorization Workflow Tracker/Services/` | WorkflowService, PaRequestService, AuditService, ReportingService, ExpirationJob, CsvExportService |
| Migrations | `Prior Authorization Workflow Tracker/Data/Migrations/` | Code-first schema history |
| Seed data | `Prior Authorization Workflow Tracker/Data/DbSeeder.cs` | 300 audit rows, realistic request distribution, relative-date generation |

**Demonstrable at:** `http://localhost:5235` after `dotnet run` (seeds automatically on first start)  
**Admin account:** `admin@pademo.com` / `Demo@1234`
