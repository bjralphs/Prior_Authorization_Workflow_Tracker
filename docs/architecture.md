# Architecture Overview

**Application:** Prior Authorization Workflow Tracker  
**Version:** 2.2 (Phase-2 remediation — PatientSearch filter GAP-10, Provider PendingInfo link GAP-07, aria-label GAP-09, ToastService scope fix GAP-13; 207 tests)
**Last Updated:** 2026-05-05

---

## 1. Technology Stack

| Layer | Technology | Version |
|---|---|---|
| Web Framework | ASP.NET Core Blazor Server | .NET 9.0 |
| Language | C# 13 | — |
| Database | SQL Server LocalDB (dev) / SQL Server Express (prod) | — |
| ORM | Entity Framework Core | 9.0.4 |
| Authentication | ASP.NET Core Identity | 9.0.4 |
| Structured Logging | Serilog + File sink | 8.0.3 / 6.0.0 |
| Validation | FluentValidation | 11.11.0 |
| CSV Export | CsvHelper | 33.0.1 |
| Testing | xUnit 2.9.2 + Moq 4.20.72 | — |

---

## 2. Layered Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                  Blazor Server (.NET 9)                         │
│                                                                 │
│  Components/Pages/       Components/Layout/    Components/Shared│
│  (Razor UI pages)        (MainLayout, NavMenu) (badges, toasts) │
└────────────────────────────────┬────────────────────────────────┘
                                 │ injects via DI
┌────────────────────────────────▼────────────────────────────────┐
│                        Service Layer                            │
│                                                                 │
│  WorkflowService     PaRequestService     ReportingService      │
│  AuditService        NotificationService  UserManagementService │
│  CsvExportService    ExpirationJob (IHostedService + IExpirationJobTrigger)  │
│                                                                 │
│  Abstractions: ICurrentUserService  IDateTimeProvider          │
└────────────────────────────────┬────────────────────────────────┘
                                 │ EF Core
┌────────────────────────────────▼────────────────────────────────┐
│                      Data Layer                                 │
│                                                                 │
│  AppDbContext (IdentityDbContext<ApplicationUser>)              │
│  DbSeeder (development seed data)                               │
│  Migrations/ (code-first schema)                                │
└────────────────────────────────┬────────────────────────────────┘
                                 │ SQL Server wire protocol
┌────────────────────────────────▼────────────────────────────────┐
│            SQL Server LocalDB / Express                         │
│  Tables + Identity tables + SQL Views (vw_*)                   │
└─────────────────────────────────────────────────────────────────┘
```

### Layer Responsibilities

| Layer | What it owns |
|---|---|
| **Components (Pages)** | UI rendering, user input, calling services, displaying results. No business logic. |
| **Service Layer** | All business rules (BR-001–BR-014), workflow transitions, audit writes, RBAC enforcement. |
| **Data Layer** | EF Core DbContext, migrations, seed data, SQL view definitions. |
| **SQL Server** | Persistence, indexed queries, reporting aggregate views. |

---

## 3. Key Design Decisions

### 3.1 Soft Delete with Global Query Filter
`PaRequest.IsDeleted` is filtered globally via `AppDbContext.OnModelCreating`:
```csharp
e.HasQueryFilter(r => !r.IsDeleted);
```
This prevents deleted requests leaking into any query without explicit `IgnoreQueryFilters()`. The seeder uses `IgnoreQueryFilters()` to check existence before seeding.

### 3.2 Optimistic Concurrency
`PaRequest.RowVersion` is configured as:
```csharp
e.Property(r => r.RowVersion).IsRowVersion();
```
EF Core includes the `WHERE RowVersion = @p0` clause on every UPDATE. Concurrent reviewer decisions throw `DbUpdateConcurrencyException`, which `WorkflowService` surfaces as a user-visible conflict error.

### 3.3 AuditService Independence
`AuditService` uses `IDbContextFactory<AppDbContext>` to open its own `DbContext` per log entry. This means an audit write is committed in its own transaction — a rolled-back business operation cannot discard audit rows. Audit failures are swallowed and logged to Serilog so they never break the primary operation.

`AuditService` exposes two methods:

| Method | When to use |
|---|---|
| `LogAsync` | All normal operations with an authenticated user in scope (request-handling path, Blazor circuit) |
| `LogAsSystemAsync` | Background-job operations without an HTTP or Blazor context (e.g. `ExpirationJob`). Uses hardcoded `DbSeeder.SystemUserId` / `"SYSTEM"` instead of `ICurrentUserService`. `IpAddress` and `CorrelationId` are `null` for system entries. |

`WorkflowService.ExpireAsync` calls `_audit.LogAsSystemAsync` so that the audit entry is correctly attributed to the SYSTEM user regardless of the DI scope in which `ExpirationJob` resolves its services.

`AuditService` also captures the current HTTP request's Correlation ID from `IHttpContextAccessor` and stores it in `AuditLog.CorrelationId`. The ID is placed in `HttpContext.Items["CorrelationId"]` by `CorrelationIdMiddleware` during request processing.

### 3.4 Clock Abstraction
All time-sensitive service logic injects `IDateTimeProvider` instead of calling `DateTime.UtcNow` directly. `SystemDateTimeProvider` returns the real clock; `FakeDateTimeProvider` (test only) returns a fixed value. **All Razor components also inject `IDateTimeProvider`** — including `RequestDetail.razor` which uses `Clock.UtcNow` for overdue date highlighting, appeal deadline display, and appeal window gating (BUG-002 fix, NFR-009 compliance).

### 3.5 ICurrentUserService Scope
`CurrentUserService` is registered as **Scoped** (one per Blazor circuit). It caches `IsActive` after the first DB lookup within a circuit to avoid repeated queries. The `IpAddress` is captured from `IHttpContextAccessor` at construction time — before the connection upgrades to WebSocket, after which `HttpContext` is unavailable.

### 3.6 SYSTEM Reserved User
`DbSeeder` creates a non-loginable user with ID `00000000-0000-0000-0000-000000000001` and username `system@pademo.internal`. `ExpirationJob` uses this ID as `ChangedByUserID` in `PaStatusHistory` rows. `WorkflowService.ExpireAsync` calls `IAuditService.LogAsSystemAsync` (see §3.3) to write the `AuditLog` entry with SYSTEM attribution. The background-job scope has no authenticated `ICurrentUserService` context, so `LogAsync` would produce empty `UserID`/`UserName` fields.

### 3.7 IExpirationJobTrigger — Admin Manual Trigger (PRD §8.8)
`ExpirationJob` implements both `BackgroundService` and `IExpirationJobTrigger`. It is registered as a singleton so both the hosted service and the interface can be resolved from DI:
```csharp
builder.Services.AddSingleton<ExpirationJob>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ExpirationJob>());
builder.Services.AddSingleton<IExpirationJobTrigger>(sp => sp.GetRequiredService<ExpirationJob>());
```
The Admin `Users.razor` page injects `IExpirationJobTrigger` and shows a "Run Expiration Check" button. The trigger returns the count of expired requests for display in a toast.

### 3.8 Audit Events Added (T-C3)
- **Login / FailedLogin:** `Login.cshtml.cs` calls `IAuditService.LogAsync` on every sign-in attempt (success, lockout, and invalid credentials). Entity `"User"`, actions `"Login"` and `"FailedLogin"`.
- **ExportDownloaded:** `CsvExportService.ExportRequestsAsync` calls `IAuditService.LogAsync` after building the CSV bytes. Entity `"PaReport"`, action `"ExportDownloaded"`, `newValues` includes filename and row count.
- **Seed data:** `DbSeeder.SeedAuditLogsAsync()` creates ~300 realistic audit rows spanning 30 days, covering PA request lifecycle events, login events, comment events, user management events, and CSV export download events. All rows carry sequential `CorrelationId` values. Dates are relative to `DateTime.UtcNow.Date` so demo data stays fresh on every fresh install.

### 3.9 UserManagementService — Batch Role Load (T-A4 / REF-01)
`UserManagementService.GetAllUsersAsync` previously called `UserManager.GetRolesAsync` for every user (O(N) round trips). It now executes a single JOIN query between `AspNetUserRoles` and `AspNetRoles`, materializes with `ToListAsync`, then groups in memory. This eliminates the N+1 pattern and is compatible with both SQL Server and the InMemory EF Core provider used in tests.

### 3.10 CSV Export — 18 Columns + Priority Filter (T-C1, T-F2)
`CsvExportService` exports 18 columns (previously 16), adding **Submitted By** and **Reviewer** resolved full names. It now also applies the `Priority` field from `PaRequestFilter` (BUG-02 fix — previously Priority was missing from the export query while all other filters were applied). The test `ExportRequestsAsync_PriorityFilter_OnlyExportsMatchingRows` covers this regression.

### 3.11 Provider Picker Dropdown (T-B1 / BUG-G)
`SubmitRequest.razor` now loads all active `Treating Provider` accounts from the database in `OnInitializedAsync` and renders them in an `<InputSelect>` dropdown. The lookup joins `AspNetUserRoles` and `AspNetRoles` directly via `DbContext` (no `UserManager`) to avoid extra Identity round-trips. This replaces the free-text GUID input that required users to know internal user IDs.

### 3.12 Role-Differentiated Dashboard (T-B3–T-B8 / §12.1)
`Home.razor` now renders a role-specific KPI panel above the shared global summary:
- **Specialist:** My drafts, submitted, pending-info, approved/denied this month, expiring soon, **overdue requests (GAP-01)**, **recent activity feed (GAP-02)**
- **Provider:** My submitted, under review, pending-info, approved this month, expiring alert
- **Reviewer:** Awaiting review, under review by me, pending-info total, overdue, month decisions, **pending > 5 days (GAP-05)**, **avg decision time this month (GAP-06)**
- **Billing Manager:** Active approved auths, expiring soon, approval rate, month decisions, **pending requests count (GAP-03)**, **top 5 denial reasons this month (GAP-04)**
- **Admin:** Shared global panel only (KPI tiles + status breakdown + expiring table)

Five service methods power these panels: `GetSpecialistDashboardAsync`, `GetProviderDashboardAsync`, `GetReviewerDashboardAsync`, `GetBillingDashboardAsync` (all updated), and the new `GetRecentActivityAsync` (GAP-02). Each method opens its own `DbContext` via `IDbContextFactory` and uses `AsNoTracking()`. Role detection uses `ICurrentUserService.IsInRole()` to dispatch the correct async load at startup — all role loads run concurrently via `Task.WhenAll`.

### 3.13 Approval Rate Report Tab (T-B2 / §12.2 report 3)
`Reports.razor` now has a 7th tab **Approval Rate** backed by `GetApprovalRateAsync(from, to)` which groups `Approved` and `Denied` requests (only these two statuses — Withdrawn/Expired are excluded from the denominator) by priority. The result includes per-priority approval percentage and an overall footer row. A color-coded badge (green ≥ 70%, yellow ≥ 50%, red < 50%) is shown in the table.

### 3.14 Document Upload (T-C4 / FR-003, §10.1)
`IPaRequestService` gained two new methods:
- `GetDocumentsAsync(requestId)` — returns all `PaDocument` rows for a request ordered by `UploadedAt`.
- `AddDocumentAsync(requestId, fileName, documentType, contentType, fileSizeBytes)` — persists a metadata-only record (no bytes in v1 per known limitation §18) and writes a `PaDocument / Uploaded` audit entry.

Permitted roles: `Specialist`, `Treating Provider`, `Payer Reviewer`, `Admin`. `Billing Manager` is explicitly excluded per §10.1. `RequestDetail.razor` now includes a **Documents** card (count badge in header, file list, and a collapsible upload form) visible only to authorized roles.

### 3.15 Admin Workflow Override (T-C5 / FR-008, §8.8)
`IWorkflowService.AdminOverrideAsync(requestId, newStatus, justification)` allows an Administrator to force any PA request to any status, bypassing the normal state machine. Safeguards:
- Admin role required (throws `AuthorizationException` otherwise).
- Non-empty justification required (throws `BusinessRuleViolationException`).
- Writes a `PaStatusHistory` row with reason prefixed `[Admin Override]`.
- Writes a `PaRequest / AdminOverride` audit entry per §11 logged events.
- Emits a `LogWarning` with all override details for post-hoc review.

`RequestDetail.razor` shows an **Override Status** button (Admin only) in the Actions card. The inline form presents a target-status dropdown and a required justification textarea with a prominent warning banner.

### 3.16 Reporting SQL Views Migration (T-E1 / §7.2)
EF Core migration `20260505023303_AddReportingViews` adds four `CREATE OR ALTER VIEW` statements via `migrationBuilder.Sql()`:

| View | Purpose |
|---|---|
| `vw_PaRequestSummary` | Flat joined row per request (procedure, plan, provider, reviewer, submitter, denial reason) |
| `vw_DenialsByReason` | Denial counts and percentage share per `DenialReason` row |
| `vw_RequestVolumeByMonth` | Submission counts grouped by `YEAR(SubmittedAt)`, `MONTH(SubmittedAt)`, and `Status` |
| `vw_ExpiringAuthorizations` | Approved requests where `AuthorizationEndDate` falls within the next 30 days |

All views filter `IsDeleted = 0`. `Down()` drops all four views. Views cannot be used against the EF Core InMemory provider (InMemory does not support raw SQL); they are SQL Server–only and used only at runtime via the `Tests.Integration` project (future, T-D6).

- **Provider:** ASP.NET Core Identity with EF Core store (`IdentityDbContext<ApplicationUser>`)
- **Roles:** 5 roles stored in `AspNetRoles` — `Authorization Specialist`, `Treating Provider`, `Billing Manager`, `Payer Reviewer`, `Administrator`
- **Enforcement:** Two layers:
  1. Blazor `<AuthorizeView Roles="...">` hides UI elements
  2. Service layer re-validates role on every mutating call (defense in depth)
- **Active check:** `ICurrentUserService.IsActive` guards every service operation — deactivated users cannot act even if their Blazor circuit remains live

---

## 5. Serilog Configuration

Development (`appsettings.Development.json`): Debug minimum level, EF Core SQL commands at Information.  
Production (`appsettings.json`): Information minimum level, rolling file sink at `logs/pa-tracker-YYYYMMDD.log`.

Enrichers: `FromLogContext`, `WithMachineName`, `WithThreadId`.

All `PaException` subtypes logged at Error level. `AuthorizationException` additionally logged at Warning for unauthorized access tracking (NFR-006).

### ExceptionHandlingMiddleware (T40)

`ExceptionHandlingMiddleware` runs immediately after HTTPS redirection (before `CorrelationIdMiddleware`) and:
- Catches `AuthorizationException` → logs at **Warning** with user ID, user name, and request path; re-throws so the ASP.NET Core error pipeline produces the correct HTTP response
- Catches all other non-cancellation exceptions → logs at **Error** with method and path; re-throws
- `OperationCanceledException` is intentionally excluded (client disconnected — not an error)

---

## 6. Database Schema (implemented entities)

| Entity | Table | Key Notes |
|---|---|---|
| `ApplicationUser` | `AspNetUsers` (extended) | + FullName, Department, IsActive, CreatedAt |
| `PaRequest` | `PaRequests` | RowVersion concurrency; IsDeleted global filter; 4 indexes |
| `PaStatusHistory` | `PaStatusHistories` | Immutable transition log; FK to SYSTEM user for expiry |
| `PaDocument` | `PaDocuments` | Metadata only in v1; no blob storage |
| `PaComment` | `PaComments` | IsInternalOnly enforced at query layer |
| `InsurancePlan` | `InsurancePlans` | SLA day values per priority per plan |
| `ProcedureCode` | `ProcedureCodes` | RequiresPriorAuth drives BR-001 |
| `DenialReason` | `DenialReasons` | IsAppealable + AppealDeadlineDays drive BR-008 |
| `AuditLog` | `AuditLogs` | Append-only; 2 composite indexes |
| `Notification` | `Notifications` | In-app only; email is a stub in v1 |

Full schema: see [data-model.md](data-model.md).

---

## 3.17 UI/UX Bug Fixes and Polish (T-F3, T-U1, T-U2, T-U3)

**`[APPEAL]` badge in Queue (T-F3, BUG-03):**  
`Queue.razor` now renders an additional `<span class="badge badge-appealed">APPEAL</span>` badge next to the `StatusBadge` for rows with `Status == PaStatus.Appealed`, fulfilling PRD §8.7. Row color (`table-info`) was already present; the text badge was missing.

**Appeal deadline in RequestDetail (T-U1, BLIND-01):**  
`RequestDetail.razor` denial detail section now shows an `Appeal Deadline` row when `DenialReason.IsAppealable == true && DecisionRenderedAt.HasValue`. The deadline is computed as `DecisionRenderedAt + AppealDeadlineDays`. A `Closed` badge is shown when `DateTime.UtcNow > deadline`. This fulfills PRD §8.5 “appeal deadline is calculated and displayed to Specialist.”

**Unread notification badge in NavMenu (T-U2, BLIND-02):**  
`NavMenu.razor` now injects `INotificationService` and `ICurrentUserService`, calls `GetUnreadCountAsync` in `OnInitializedAsync`, and renders a `<span class="badge bg-danger rounded-pill">N</span>` badge next to the Notifications link when `_unreadCount > 0`. Failures are caught silently (notification badge is non-critical).

**Delete Draft button in RequestDetail (T-U3, BLIND-03):**  
`RequestDetail.razor` header now shows a **Delete Draft** button for `Specialist + Admin` roles when `Status == PaStatus.Draft`. The button opens a `ConfirmationModal`; on confirm it calls `PaRequestSvc.DeleteDraftAsync(Id)` and redirects to `/requests`. This matches the existing `EditRequest.razor` Delete flow without requiring users to navigate to the edit page first.

## 3.18 ReportingService — Server-Side Aggregation (T-R1)

All five dashboard aggregation methods previously loaded entire row sets via `ToListAsync()` and counted in memory. Each method now issues individual `CountAsync()` calls on a single `DbContext` instance, pushing aggregation to the DB engine:

| Method | Before | After |
|---|---|---|
| `GetDashboardSummaryAsync` | `ToListAsync()` 7 fields on all requests | 7 sequential `CountAsync()` queries |
| `GetSpecialistDashboardAsync` | `ToListAsync()` 3 fields per specialist's requests | 7 sequential `CountAsync()` queries (incl. `MyOverdueRequests`) |
| `GetProviderDashboardAsync` | `ToListAsync()` 3 fields per provider's requests | 5 sequential `CountAsync()` queries with base filter |
| `GetReviewerDashboardAsync` | `ToListAsync()` 4 fields on a broad status filter | 6 `CountAsync()` queries + 1 small `ToListAsync()` for avg decision days |
| `GetBillingDashboardAsync` | `ToListAsync()` 3 fields on Approved/Denied/Expired | 7 sequential `CountAsync()` queries (incl. `PendingRequests`) |

Using a single `DbContext` per method (sequential calls) avoids EF Core's "one active operation at a time" constraint while still eliminating all data transfer to the application process. The InMemory provider used in tests handles `CountAsync` natively. Tests added: `GetDashboardSummaryAsync_ReturnsCorrectServerSideCounts`, `GetSpecialistDashboardAsync_ReturnsOnlyOwnedRequests`, `GetBillingDashboardAsync_ComputesApprovalRateFromServerSideCounts`.

## 3.19 RequestDetail.razor — Action Buttons Refactored to Razor Markup (T-R2)

`RequestDetail.razor` previously rendered workflow action buttons via a `RenderFragment RenderActionButtons() => __builder => { ... }` method that used the low-level Blazor render builder API (`OpenComponent<AuthorizeView>`, `AddAttribute`, `AddContent`, `CloseComponent`). A `BuildButton()` helper constructed each `<AuthorizeView>` + `<button>` pair via builder calls.

**Removed:**
- `private RenderFragment RenderActionButtons()` method
- `private RenderFragment BuildButton(...)` helper
- `private static readonly string ChildContent = nameof(AuthorizeView.Authorized)` constant
- `[SuppressMessage("Style", "IDE1006")]` attribute on the above constant

**Replaced with:** `@if` + `<AuthorizeView>` Razor markup blocks, one per status/role combination. Preserves all authorization rules and action bindings with readable, maintainable template syntax.

---

## 3.20 RequestDetail.razor — User Name Cache Refresh after Workflow Actions (REF-03)

**Gap:** `RunActionAsync` reloaded `_request` and `_statusHistory` but did not call `LoadUserNamesAsync()`. When a workflow transition sets a new `ReviewerUserID` (e.g., `BeginReviewAsync`), the reviewer's name would not appear in the detail card until a full page reload — the name dictionary (`_userNames`) was stale.

**Fix (`RequestDetail.razor`):** Added `await LoadUserNamesAsync()` immediately after `_statusHistory = await PaRequestSvc.GetStatusHistoryAsync(Id)` inside `RunActionAsync`. `LoadUserNamesAsync` reads all user IDs from the updated `_request`, `_comments`, and `_documents` and batch-queries `ApplicationUser.FullName` in a single `ToDictionaryAsync` call.

**Scope:** `AddCommentAsync` already reloaded `_comments` (existing). `ConfirmUploadAsync` already reloaded `_documents` and called `LoadUserNamesAsync` (existing). The gap was only in `RunActionAsync` (pure workflow transitions).

**No tests needed:** `RequestDetail.razor` is a Blazor component; this is covered by manual workflow smoke tests (Begin Review → reviewer name visible immediately).

---

## 3.21 NavMenu.razor — Periodic Unread Notification Count Refresh (§16.1)

**Gap (T-U2 follow-up):** `NavMenu.razor` loaded `_unreadCount` once in `OnInitializedAsync`. If a notification was created while the user was on the page (e.g., Reviewer approves a request → Specialist receives a notification), the badge count would not update until a page navigation or reload.

**Fix (`NavMenu.razor`):**
- Added `@implements IDisposable`
- Extracted count-load logic into `private async Task RefreshCountAsync()` — checks `CurrentUser.UserId`, queries `NotifSvc.GetUnreadCountAsync`, and calls `StateHasChanged()` only if the count changed
- `OnInitializedAsync` calls `RefreshCountAsync()` then starts a `System.Threading.Timer` with `dueTime = 30 s` and `period = 30 s`
- Timer callback: `_ => InvokeAsync(RefreshCountAsync)` — marshals the refresh onto the Blazor circuit's synchronization context (required for safe `StateHasChanged()` from a background thread)
- `public void Dispose()` disposes the timer when the component is torn down, preventing timer leaks on circuit disconnect

**Thread-safety:** `InvokeAsync` guarantees the count update and `StateHasChanged()` run on the Blazor circuit thread. The `Timer` callback never touches component state directly.

**Failure handling:** Exceptions in `RefreshCountAsync` are silently swallowed — the notification badge is non-critical; a transient DB error should not crash the NavMenu.

**PRD ref:** §16.1 — In-app notifications; badge must reflect current unread state.

**Added (Razor markup):** The `@RenderActionButtons()` call site was replaced with inline `@if` blocks and `<AuthorizeView>` Razor components directly in the template. Each status-contingent group of buttons is expressed as a readable `@if (status == ...) { <AuthorizeView ...><Authorized>...</Authorized></AuthorizeView> }` block. Authorization logic is preserved identically; the `IsActionableStatus()` static helper is retained for the "No actions available" message.

---

## 3.22 NavMenu.razor — Badge Reset on Navigation (LocationChanged)

**Gap:** The 30-second polling timer in `NavMenu.razor` (§3.21) means the badge can lag up to 30 seconds after the user visits `/notifications` and dismisses all items. The badge would not snap to 0 until the next timer tick.

**Fix:**
- Injected `NavigationManager Nav` into `NavMenu.razor`
- In `OnInitializedAsync`, subscribed: `Nav.LocationChanged += OnLocationChanged`
- Handler: `private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => _ = InvokeAsync(RefreshCountAsync)`
- `Dispose()` unsubscribes: `Nav.LocationChanged -= OnLocationChanged` then disposes the timer

**Effect:** Every Blazor client-side navigation (SPA-style route change) immediately triggers `RefreshCountAsync`. For the typical flow — user visits `/notifications`, dismisses all items, navigates away — the badge snaps from N to 0 within a single render cycle without waiting for the polling interval.

**Thread-safety:** `InvokeAsync` ensures the badge refresh runs on the Blazor circuit's synchronization context even when fired from `NavigationManager`'s event (which fires on the circuit thread already in Blazor Server, but the pattern is correct for both Blazor Server and WASM).

---

## 3.23 Home.razor — Admin Dashboard Panel (PRD §12.1)

**Gap:** PRD §12.1 specifies an Admin-specific dashboard view containing "User activity summary" and "Audit log quick-search widget." The Admin role previously fell through to the shared global KPI section only (same tiles shown to Specialist and Reviewer), with no admin-specific data.

**Implemented panel (Admin-only `<AuthorizeView Roles="Administrator">`):**

| Widget | Implementation |
|---|---|
| **Submission Volume This Month** | Calls `ReportingService.GetProviderVolumeAsync(firstOfMonth, today)` at load time; renders top-5 providers by submission count with Approved and Denied columns. |
| **Quick Audit Search** | Two text inputs (User and Entity). Clicking **Search** calls `AdminQuickSearch()` which builds a URL and calls `Nav.NavigateTo("/admin/audit-log?user=…&entity=…")`. |

**AuditLog.razor query-param support:** Added `[SupplyParameterFromQuery]` properties `QueryUser`, `QueryEntity`, and `QueryAction`. `OnInitializedAsync` pre-populates `_filterUser`, `_filterEntity`, `_filterAction` from these values before calling `LoadAsync()`. The existing filter logic then applies them automatically — no change to the query or UI was needed.

**No new service methods needed:** `GetProviderVolumeAsync` already existed. The admin panel reuses it with a month-to-date date range.

**No new tests needed:** `GetProviderVolumeAsync` is already covered. `AdminQuickSearch()` builds a URL string — no business logic to unit-test. `[SupplyParameterFromQuery]` is a Blazor framework feature.

---

## 3.24 Dashboard PRD Compliance Pass (GAP-01–GAP-06, BUG-01) — v3

Six PRD §12.1 dashboard KPI gaps and one security bug were resolved as part of the v3 compliance audit.

### 3.24.1 GAP-01 — Specialist: "Requests past decision due date"

**`SpecialistDashboard` record:** Added `int MyOverdueRequests`.  
**`ReportingService.GetSpecialistDashboardAsync`:** Added `CountAsync` on requests belonging to the specialist where `!TerminalStatuses.Contains(Status) && DecisionDueDate.HasValue && DecisionDueDate.Value.Date < today`.  
**`Home.razor`:** Specialist panel shows a red `alert-danger` banner when `MyOverdueRequests > 0` (inline count, link-free — specialist can use the queue to locate them).

### 3.24.2 GAP-02 — Specialist: "Recent activity feed (last 10 status changes)"

**New DTO:** `RecentActivityItem(int RequestId, string RequestNumber, string PatientName, PaStatus NewStatus, DateTime OccurredAt)` in `ReportingModels.cs`.  
**New interface method:** `IReportingService.GetRecentActivityAsync(string specialistId, int count = 10, CancellationToken ct)`.  
**`ReportingService.GetRecentActivityAsync`:** Queries `PaStatusHistories` joined to `PaRequests` (via navigation property), filters by `PaRequest.SubmittedByUserID == specialistId`, orders by `ChangedAt DESC`, takes `count`, projects to `RecentActivityItem`.  
**`Home.razor`:** `LoadSpecialistDashAsync` now loads both `_specialistDash` and `_recentActivity` sequentially on the same task. The specialist panel renders a `<table>` of up to 10 recent items (Request link, PatientName, `<StatusBadge>`, timestamp) below the KPI cards, conditional on `_recentActivity.Count > 0`.

### 3.24.3 GAP-03 — Billing: "Pending requests count"

**`BillingDashboard` record:** Added `int PendingRequests`.  
**`ReportingService.GetBillingDashboardAsync`:** Added `CountAsync` on `ActiveStatuses` (Submitted, UnderReview, PendingInfo, Appealed).  
**`Home.razor`:** Billing panel adds a "Pending Requests (active)" stat card.

### 3.24.4 GAP-04 — Billing: "Top 5 denial reasons"

**No service change needed** — `GetDenialsByReasonAsync(from, to)` already existed.  
**`Home.razor`:** `LoadBillingDashAsync` now sequentially calls `GetDenialsByReasonAsync` for the current month and stores the top-5 by count descending in `_billingTopDenials`. A small `<table>` (Reason | Count) renders below the other Billing cards when `_billingTopDenials.Count > 0`.

### 3.24.5 GAP-05 — Reviewer: "Requests pending > 5 days"

**`ReviewerDashboard` record:** Added `int PendingMoreThan5Days`.  
**`ReportingService.GetReviewerDashboardAsync`:** Added `CountAsync` on Submitted/UnderReview requests where `SubmittedAt.HasValue && SubmittedAt.Value.Date <= today.AddDays(-5)` (inclusive boundary — exactly 5 days counts as ">5").  
**`Home.razor`:** Reviewer panel adds a "Pending > 5 days" card styled with `border-warning` when count > 0.

### 3.24.6 GAP-06 — Reviewer: "Average decision time (days) this month"

**`ReviewerDashboard` record:** Added `double AvgDecisionDaysThisMonth`.  
**`ReportingService.GetReviewerDashboardAsync`:** Loads decided-this-month requests (Approved+Denied, `DecisionRenderedAt >= monthStart`, both timestamps present) into a small in-memory list, then computes `Average((DecisionRenderedAt - SubmittedAt).TotalDays)`. Returns 0.0 when no decisions exist. Result is rounded to 1 decimal place. This approach avoids `EF.Functions.DateDiffDay` which is unsupported by the EF InMemory provider.  
**`Home.razor`:** Reviewer panel adds an "Avg Decision Time" card alongside the "Pending > 5 days" card.

### 3.24.7 BUG-01 — AUTH number collision resistance

**Before:** `WorkflowService.GenerateAuthorizationNumber` used `Random.Shared.Next(10_000, 99_999)` — 90,000 distinct values per calendar day, with non-cryptographic randomness.  
**After:** Replaced with `RandomNumberGenerator.Fill(Span<byte>)` on 3 bytes, then `Convert.ToHexString` — produces 6-character uppercase hex (e.g. `A3F02C`), yielding ~16.7 million distinct values per day using OS-level CSPRNG.  
**Format change:** `AUTH-YYYYMMDD-XXXXX` (5 numeric digits) → `AUTH-YYYYMMDD-XXXXXX` (6 hex chars). The existing `Assert.StartsWith("AUTH-")` test continues to pass; format is otherwise unconstrained by the PRD.  
**Import added:** `using System.Security.Cryptography;` at top of `WorkflowService.cs`.

**Implemented panel (Admin-only `<AuthorizeView Roles="Administrator">`):**

| Widget | Implementation |
|---|---|
| **Submission Volume This Month** | Calls `ReportingService.GetProviderVolumeAsync(firstOfMonth, today)` at load time; renders top-5 providers by submission count with Approved and Denied columns. |
| **Quick Audit Search** | Two text inputs (User and Entity). Clicking **Search** calls `AdminQuickSearch()` which builds a URL and calls `Nav.NavigateTo("/admin/audit-log?user=…&entity=…")`. |

**AuditLog.razor query-param support:** Added `[SupplyParameterFromQuery]` properties `QueryUser`, `QueryEntity`, and `QueryAction`. `OnInitializedAsync` pre-populates `_filterUser`, `_filterEntity`, `_filterAction` from these values before calling `LoadAsync()`. The existing filter logic then applies them automatically — no change to the query or UI was needed.

**No new service methods needed:** `GetProviderVolumeAsync` already existed. The admin panel reuses it with a month-to-date date range.

**No new tests needed:** `GetProviderVolumeAsync` is already covered. `AdminQuickSearch()` builds a URL string — no business logic to unit-test. `[SupplyParameterFromQuery]` is a Blazor framework feature.

---

## 3.25 BUG-01 Follow-Up: AuthorizationNumber Unique Index + Test Hardening

**Context:** BUG-01 (fixed in §3.24.7) replaced `Random.Shared` with `RandomNumberGenerator`, reducing collision probability to ~1 in 16.7M per day. This section adds a complementary database-level safety net and a stronger format assertion in tests.

### 3.25.1 `UX_PaRequests_AuthorizationNumber` — unique partial index

**File:** `Data/AppDbContext.cs`

```csharp
e.HasIndex(r => r.AuthorizationNumber)
    .IsUnique()
    .HasFilter("[AuthorizationNumber] IS NOT NULL")
    .HasDatabaseName("UX_PaRequests_AuthorizationNumber");
```

**Migration:** `AddAuthorizationNumberUniqueIndex` (20260505173749).

**Filter (`IS NOT NULL`):** Draft, Submitted, PendingInfo, UnderReview, and Denied requests all have `AuthorizationNumber = NULL`. The partial filter excludes these rows from the uniqueness check so they do not conflict. Only Approved requests receive an `AuthorizationNumber`; the unique index therefore enforces that no two Approved requests share the same AUTH reference number.

**Tradeoff:** If a CSPRNG collision occurs (probability ≈ 1.8×10⁻¹⁰ per approval at 1M approvals/day), `ApproveAsync` will propagate a `DbUpdateException`. A retry loop was not added in v1; probability is negligible for the expected volume.

### 3.25.2 AUTH number format assertion hardened

**File:** `Tests.Unit/Services/WorkflowServiceTests.cs` — `ApproveAsync_ValidDecision_GeneratesAuthorizationNumber`

**Before:** `Assert.StartsWith("AUTH-", result.AuthorizationNumber)`

**After:**
```csharp
// BUG-01: format is AUTH-YYYYMMDD-XXXXXX (6 uppercase hex chars from CSPRNG)
Assert.Matches(@"^AUTH-\d{8}-[0-9A-F]{6}$", result.AuthorizationNumber);
```

The regex validates the full format, catching any regression that changes the date format, suffix length, or character set. Tests remain 189/189.

---

## 3.26 Seed Data Quality Pass (§14.2 PRD compliance)

**File:** `Data/DbSeeder.cs`

### 3.26.1 Date anchoring → `DateTime.UtcNow.Date`

`SeedPaRequestsAsync` and `SeedAuditLogsAsync` previously anchored all dates to `new DateTime(2026, 1, 1)`, making approved auth dates and SLA deadlines stale within months. Both methods now use `var baseDate = DateTime.UtcNow.Date` so every fresh install produces demo data current as of the deployment date.

`approvedStart` (used to compute `AuthorizationStartDate`/`AuthorizationEndDate`) is similarly changed from `new DateTime(2026, 1, 15)` to `DateTime.UtcNow.Date`. As a result, several approved authorizations now fall within the 30-day expiration alert window, populating the Specialist/Billing "expiring soon" KPI with realistic counts.

### 3.26.2 Expired request timeline — missing UnderReview→Approved step

The status history generator only added `UnderReview → Approved` rows for `PaStatus.Approved` requests. Expired requests (which transition `Approved → Expired`) were missing the prior `UnderReview → Approved` row, producing an incomplete timeline in `RequestDetail.razor`. Fixed by extending the condition:

```csharp
if (req.Status == PaStatus.Approved || req.Status == PaStatus.Expired)
```

### 3.26.3 Approved request specialist/provider distribution

All 15 Approved requests were previously submitted by `specialist1` and assigned to `provider1`, making the Specialist dashboard and Provider Volume report show all volume concentrated in one user. Changed to:
- odd/even alternation between `specialist1` / `specialist2`
- groups of 5: `provider1` (indexes 0–4), `provider2` (5–9), `provider3` (10–14)

### 3.26.4 Auth number format consistency

Seeded `AuthorizationNumber` values changed from `AUTH-YYYYMMDD-NNNNN` (5-digit decimal) to `AUTH-YYYYMMDD-XXXXXXX` (6-char uppercase hex, `:X6` format), consistent with the CSPRNG format generated by `WorkflowService.ApproveAsync` (BUG-01 fix).

### 3.26.5 Comment seeding expanded (~12 → 72)

Comment seeding replaced `Take(3)` partial coverage with full coverage across all status groups:

| Status | Requests | Comments/request | Total |
|---|---|---|---|
| Submitted | 6 | 1 (specialist note) | 6 |
| UnderReview | 7 | 2 (reviewer internal notes) | 14 |
| PendingInfo | 5 | 2 (reviewer request + specialist response) | 10 |
| Approved | 10 | 2 (approval note + specialist follow-up) | 20 |
| Denied | 7 | 2 (denial note + specialist review) | 14 |
| Appealed | 3 | 2 (appeal packet + secondary review) | 6 |
| Withdrawn | 2 | 1 (closure note) | 2 |
| **Total** | | | **72** |

### 3.26.6 Notification seeding added

New `SeedNotificationsAsync` method (called last in `SeedAsync`) creates in-app notifications for demo accounts, eliminating the empty notification center on first login:

| Type | Source | Read logic |
|---|---|---|
| `DecisionRendered` | One per Approved/Denied request → submitting specialist | Pre-read if `DecisionRenderedAt < today - 14 days` |
| `AppealWindow` | One per Denied request with open appeal window → specialist | Always unread |
| `StatusChanged` | One per PendingInfo request → specialist | Pre-read if moved to PendingInfo > 14 days ago |
| `ExpirationAlert` | One per Approved request expiring within 30 days → specialist | Always unread |

---

## 3.27 FR-004 Compliance: Appealed-State Workflow Transitions Fixed (§8.7)

**Root cause:** `BeginReviewAsync`, `ApproveAsync`, and `DenyAsync` hardcoded the expected from-status via `RequireRole(PaStatus.Submitted, ...)` / `EnsureStatus(request, PaStatus.UnderReview, ...)`. FR-004 defines three valid transitions originating from `Appealed`:

| Transition | Actor | §8.7 intent |
|---|---|---|
| `Appealed → UnderReview` | Reviewer / Admin | Reviewer picks up the appeal for secondary review |
| `Appealed → Approved` | Reviewer / Admin | Reviewer overturns the original denial |
| `Appealed → Denied` | Reviewer / Admin | Reviewer upholds the original denial (appeal dismissed) |

These transitions were listed in the `Transitions` dictionary in `WorkflowService` and in `RequestDetail.razor` action buttons — the UI rendered "Begin Review", "Approve", and "Deny" buttons for Appealed requests — but calling the methods would throw `WorkflowTransitionException` because `EnsureStatus` expected only `Submitted` or `UnderReview`.

**Fix:** All three methods now load the request first, perform a multi-status validation (`status != X && status != Y → throw`), and then call `RequireRole(request.Status, target)` with the actual from-status:

```csharp
// BeginReviewAsync — before
RequireRole(PaStatus.Submitted, PaStatus.UnderReview);  // hardcoded
EnsureStatus(request, PaStatus.Submitted, PaStatus.UnderReview);  // hardcoded

// BeginReviewAsync — after
if (request.Status != PaStatus.Submitted && request.Status != PaStatus.Appealed)
    throw new WorkflowTransitionException(request.Status, PaStatus.UnderReview);
RequireRole(request.Status, PaStatus.UnderReview);  // uses actual from-status
```

The `fromStatus` variable is captured before any mutations and passed to `ApplyTransitionAsync` and `_audit.LogAsync`, so `PaStatusHistory.FromStatus` and `AuditLog.OldValues` are always correct.

**Tests added (6):**
- `IsTransitionValid_KnownPairs` theory: 3 new `[InlineData]` entries for `Appealed → UnderReview/Approved/Denied`
- `BeginReviewAsync_FromAppealed_TransitionsToUnderReview`
- `ApproveAsync_FromAppealed_OverturnsDenial`
- `DenyAsync_FromAppealed_UpholdsDenial`

**Test total:** 189 → 195 (all passing)

---

## 3.28 Phase-2 Remediation (GAP-07, GAP-09, GAP-10, GAP-13)

### 3.28.1 GAP-10 — PatientSearch free-text filter in Queue

**`PaRequestFilter.cs`:** Added `public string? PatientSearch { get; set; }` — null/whitespace means no filter.

**`PaRequestService.GetQueueAsync`:** After all other filter clauses, appends:
```csharp
if (!string.IsNullOrWhiteSpace(filter.PatientSearch))
{
    var term = filter.PatientSearch.Trim();
    query = query.Where(r =>
        (r.PatientName != null && r.PatientName.Contains(term)) ||
        r.PatientMrn.Contains(term));
}
```
EF Core translates `Contains` to `LIKE '%term%'` on SQL Server (case-insensitive by default DB collation).

**`Queue.razor`:** Added a "Patient Name / MRN" `<input type="search">` field in the filter panel (third row, 6-column width). Bound with `@bind="_filter.PatientSearch" @bind:after="ResetAndLoad"` — triggers on focus-out, consistent with the existing date fields. `ClearFilters()` nulls `_filter.PatientSearch`.

### 3.28.2 GAP-07 — Provider dashboard: PendingInfo link

**`Home.razor`:** The "Additional Info Needed" card in the Provider `<AuthorizeView>` section now shows a `<a href="/requests?status=PendingInfo">Respond now →</a>` link when `_providerDash.MyPendingInfo > 0`. Mirrors the identical pattern already present in the Specialist panel.

### 3.28.3 GAP-09 — aria-label on icon-only dismiss buttons

**`RequestDetail.razor`:** The dismissible error alert's close button was missing `aria-label`. Added `aria-label="Dismiss error"`.

All other close buttons (`ConfirmationModal.razor`, `Toast.razor`) already had `aria-label="Close"`.

Sort header buttons in `Queue.razor` include descriptive text ("Patient ▼") and are not icon-only, so no changes required there.

### 3.28.4 GAP-13 — ToastService: per-circuit isolation

**Problem:** `ToastService` was registered as `Singleton`. Blazor Server circuits each mounted a `<Toast>` component and subscribed to the shared `OnChange` event. A toast raised in one browser tab would trigger `StateHasChanged` in every other connected tab's `Toast` component, causing cross-tab toast bleed.

**Fix:** Changed `Program.cs` registration from `AddSingleton<ToastService>()` to `AddScoped<ToastService>()`. Each Blazor circuit now receives its own `ToastService` instance with an isolated `_messages` list and `OnChange` event. `ToastService` is only injected by Razor components (not by any singleton services), so the lifetime change is safe.

**Auto-dismiss tasks:** The background `Task.Delay` inside `Show()` captures the `ToastService` instance. After a circuit is disposed and the `Toast` component unsubscribes from `OnChange`, the scheduled `Dismiss()` call runs harmlessly on the orphaned instance — the task completes without error, and GC collects the instance afterwards.

---

## 7. Project Structure

```
Prior_Authorization_Workflow_Tracker.sln
├── Prior Authorization Workflow Tracker/       ← Main Blazor Server project
│   ├── Components/
│   │   ├── Layout/         MainLayout, NavMenu (role-aware, AuthorizeView)
│   │   ├── Pages/
│   │   │   ├── Home.razor              Dashboard — KPI tiles, status breakdown, expiring auth table
│   │   │   ├── Requests/
│   │   │   │   ├── Queue.razor         Filterable, sortable, paginated request queue
│   │   │   │   ├── SubmitRequest.razor New request form (/requests/new)
│   │   │   │   ├── RequestDetail.razor Request detail + workflow actions + comments + history
│   │   │   │   └── EditRequest.razor   Edit/resubmit/delete draft (/requests/{id}/edit)
│   │   │   ├── Admin/
│   │   │   │   ├── Users.razor         User management — create, deactivate, change role, run expiration check (/admin/users)
│   │   │   │   └── AuditLog.razor      Filterable audit log viewer with detail expand (/admin/audit-log)
│   │   │   ├── Notifications/
│   │   │   │   └── Notifications.razor Notification center (mark-read, dismiss)
│   │   │   └── Reports/
│   │   │       └── Reports.razor       4-tab reporting + CSV export (FR-010)
│   │   └── Shared/         StatusBadge, PriorityBadge, Toast, ConfirmationModal, Breadcrumb
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   ├── DbSeeder.cs
│   │   └── Migrations/     (auto-generated by dotnet-ef)
│   ├── Exceptions/         PaException hierarchy (5 classes)
│   ├── Middleware/         CorrelationIdMiddleware (T10) + ExceptionHandlingMiddleware (T40)
│   ├── Models/             Entity classes + enums (10 entities, 3 enums)
│   ├── Pages/
│   │   └── Account/        Login.cshtml + Logout.cshtml (Razor Pages, T20)
│   ├── Services/
│   │   ├── Abstractions/   ICurrentUserService, IDateTimeProvider
│   │   ├── Models/         Service-layer DTOs + ReportingModels
│   │   ├── AuditService, BusinessRuleService, CsvExportService
│   │   ├── ExpirationJob (IHostedService, T16)
│   │   ├── NotificationService, PaRequestService
│   │   ├── ReportingService + IReportingService (T17)
│   │   ├── ToastService (singleton, T22)
│   │   ├── UserManagementService, WorkflowService
│   │   └── CurrentUserService, SystemDateTimeProvider
│   ├── Program.cs          Full DI wiring + Serilog + Identity + Razor Pages
│   └── appsettings*.json   Serilog + connection strings
└── Tests.Unit/             xUnit + Moq test project (116 tests)
    ├── Fakes/              FakeDateTimeProvider, FakeCurrentUserService
    └── Services/           WorkflowServiceTests, BusinessRuleServiceTests,
                            PaRequestServiceTests, NotificationServiceTests,
                            AuditServiceTests, ReportingServiceTests (T36, 15 tests)
```

---

## 8. Authentication Flow (T20)

Because Blazor Server runs over SignalR (WebSocket), cookies cannot be set from within a Blazor circuit. Login/logout must happen over HTTP using Razor Pages:

1. **Unauthenticated request** → `AuthorizeRouteView` renders JS redirect to `/Account/Login?returnUrl=…`
2. **Login.cshtml (GET)** → renders form; signs out any external session cookie
3. **Login.cshtml (POST)** → `SignInManager.PasswordSignInAsync` → sets `.AspNetCore.Cookies` → `LocalRedirect` to `returnUrl`
4. **Blazor circuit** sees `ClaimsPrincipal` from cookie on reconnect
5. **Logout.cshtml (POST)** → `SignInManager.SignOutAsync` → redirects to `/Account/Login`

The `AntiforgeryToken` tag helper on the logout form prevents CSRF-triggered logouts.
