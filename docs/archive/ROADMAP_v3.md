# Prior Authorization Workflow Tracker — PRD Compliance Roadmap v3

**Audit date:** 2025-05-06  
**Baseline:** 180/180 unit tests passing  
**Scope:** Evidence-based review of every PRD section against the committed codebase.

---

## 1. Compliance Matrix

### 1.1 Functional Requirements

| PRD Section | Requirement | Status | Evidence |
|---|---|---|---|
| §8.1 FR-001 | Create PA request (draft → submit) | ✅ Complete | `PaRequestService.CreateDraftAsync`, `SubmitAsync` + validators |
| §8.2 FR-002 | Queue with filters/sort/pagination | ✅ Complete | `Queue.razor` — 6 filters, 4 sort columns, 25/page pagination |
| §8.3 FR-003 | Request detail view | ✅ Complete | `RequestDetail.razor` — all fields, timeline, comments, documents |
| §8.4 FR-004 | Workflow state machine (9 statuses) | ✅ Complete | `WorkflowService.Transitions` — all 16 edges enforced |
| §8.5 FR-005 | Decision rendering with AUTH number | ✅ Complete | `ApproveAsync` — `AUTH-YYYYMMDD-XXXXX` format |
| §8.6 FR-006 | Expiration background job | ✅ Complete | `ExpirationJob` hosted service, SYSTEM user attribution |
| §8.7 FR-007 | Appeal tracking with deadline | ✅ Complete | BR-008, deadline check, `[APPEAL]` badge in Queue + Detail |
| §8.8 FR-008 | Admin user management + override | ✅ Complete | `Admin/Users.razor`, `WorkflowService.AdminOverrideAsync` |
| §8.9 FR-009 | Notification center | ✅ Complete | `NotificationService`, `Notifications.razor`, 30s badge polling |
| §8.10 FR-010 | CSV export | ✅ Complete | `CsvExportService` — 18 cols, `PA_Export_YYYYMMDD_HHmmss.csv` |

### 1.2 Business Rules

| Rule | Description | Status | Test |
|---|---|---|---|
| BR-001 | RequiresPriorAuth procedure check | ✅ | `BusinessRuleService_ProcedureDoesNotRequirePriorAuth` |
| BR-003 | ICD-10 format validation | ✅ | `ICD10_Regex_CompiledWithTimeout` |
| BR-004 | Units 1–999 | ✅ | `UnitsRequested_Zero_ShouldFail` |
| BR-005 | No duplicate active request (same patient+procedure+plan) | ✅ | `DuplicateActiveProcedureSamePlan_ShouldFailValidation` |
| BR-006 | ApprovedStartDate ≤ ApprovedEndDate | ✅ | `ApprovalDecisionValidator` tests |
| BR-007 | UnitsGranted 1–999 | ✅ | `Approve_ZeroUnitsGranted_ShouldThrow` |
| BR-008 | Appeal deadline (calendar days, inclusive) | ✅ | `Appeal_AfterDeadline_ShouldThrow` |
| BR-009 | Withdraw prohibited from terminal statuses | ✅ | `Withdraw_FromDraftStatus_ShouldThrow` |
| BR-010 | ClinicalJustification ≥ 50 chars | ✅ | `Submission_ShortJustification_ShouldFail` |
| BR-011 | AuthorizationStartDate not in past | ✅ | `AuthorizationStartDateInPast_ShouldFail` |
| BR-012 | UnitsGranted ≤ UnitsRequested | ✅ | `Approve_ExcessiveUnitsGranted_ShouldThrow` |
| BR-013 | Resubmit requires new comment or document | ✅ | `Resubmit_WithoutNewContent_ShouldFail` |
| BR-014 | Hard-delete only Draft; only creator or Admin | ✅ | `Delete_NonDraft_ShouldThrow` |

### 1.3 Dashboard Requirements (§12.1)

| Role | PRD-Specified KPIs | Implemented | Gap |
|---|---|---|---|
| Authorization Specialist | Open requests by status | ✅ MyDrafts, MySubmitted, MyPendingInfo | — |
| Authorization Specialist | Requests past decision due date | ❌ Missing | **GAP-01** |
| Authorization Specialist | Authorizations expiring ≤ 30 days | ✅ MyExpiringSoon | — |
| Authorization Specialist | Recent activity feed (last 10 status changes) | ❌ Missing | **GAP-02** |
| Billing Manager | Total approved / denied this month | ✅ ApprovedThisMonth, DeniedThisMonth | — |
| Billing Manager | Denial rate % | ✅ ApprovalRatePct (inverse) | — |
| Billing Manager | Pending requests count | ❌ Missing | **GAP-03** |
| Billing Manager | Top 5 denial reasons | ❌ Missing | **GAP-04** |
| Treating Provider | Open request count by status | ✅ MySubmitted, MyUnderReview, MyPendingInfo | — |
| Treating Provider | PendingInfo requiring clinical input | ✅ MyPendingInfo with alert | — |
| Treating Provider | Expiring authorizations | ✅ MyExpiringSoon alert | — |
| Payer Reviewer | My assigned requests count | ✅ AwaitingReview + UnderReviewByMe | — |
| Payer Reviewer | Requests pending > 5 days | ❌ Missing | **GAP-05** |
| Payer Reviewer | Average decision time (days) this month | ❌ Missing | **GAP-06** |
| Administrator | Combined view | ✅ Global KPI tiles | — |
| Administrator | User activity summary | ✅ Top-5 provider volume table | — |
| Administrator | Audit log quick-search widget | ✅ Deep-link to AuditLog.razor | — |

### 1.4 Reporting Page (§12.2)

| Report | Status | Tab |
|---|---|---|
| Request Volume by Month | ✅ Complete | `volume` |
| Denial Analysis | ✅ Complete | `denials` |
| Approval Rate | ✅ Complete | `approvalrate` |
| Expiring Authorizations | ✅ Complete | `expiring` |
| Turnaround Time | ✅ Complete | `turnaround` |
| Provider Submission Volume | ✅ Complete | `providervolume` |

### 1.5 Audit Logging (§11)

| Event | Entity | Action string | Status |
|---|---|---|---|
| Request created | PaRequest | Created | ✅ |
| Status changed | PaRequest | StatusChanged | ✅ |
| Decision rendered | PaRequest | DecisionRendered | ✅ |
| Admin override | PaRequest | AdminOverride | ✅ |
| User login | User | Login | ✅ (`Login.cshtml.cs`) |
| User login failed | User | FailedLogin | ✅ (`Login.cshtml.cs`) |
| CSV export downloaded | PaReport | ExportDownloaded | ✅ (`CsvExportService`) |
| CorrelationId on every row | AuditLog | — | ✅ (`AuditService.cs:62`) |
| IpAddress on every row | AuditLog | — | ✅ (`ICurrentUserService.IpAddress`) |

### 1.6 RBAC (§10.1)

| Role | View All | Submit/Edit | Review/Decide | Manage Users | Export | Override |
|---|---|---|---|---|---|---|
| Authorization Specialist | Own only | ✅ | — | — | — | — |
| Treating Provider | Own patients | — | — | — | — | — |
| Billing Manager | All (read-only) | — | — | — | ✅ | — |
| Payer Reviewer | All (read-only) | — | ✅ | — | — | — |
| Administrator | All | ✅ | ✅ | ✅ | ✅ | ✅ |

All enforced at both service layer and UI (`[Authorize(Roles=...)]` + `<AuthorizeView Roles="...">`).

### 1.7 Non-Functional Requirements

| NFR | Requirement | Status |
|---|---|---|
| NFR-001 | EF `CountAsync` not `ToListAsync`+in-memory | ✅ Verified throughout ReportingService |
| NFR-002 | `AsNoTracking()` on all read queries | ✅ Service layer |
| NFR-003 | `IDbContextFactory` — no shared DbContext | ✅ All services use factory |
| NFR-004 | No raw SQL string concatenation | ✅ All queries parameterized |
| NFR-005 | Anti-forgery tokens | ✅ `app.UseAntiforgery()` in Program.cs |
| NFR-006 | Serilog structured logging | ✅ Rolling file sink, correlation enrichment |
| NFR-007 | Global query filter `IsDeleted` | ✅ `HasQueryFilter` in AppDbContext |
| NFR-008 | Optimistic concurrency (RowVersion) | ✅ `IsRowVersion()` on PaRequest |
| NFR-009 | No business logic in Razor | ✅ All logic in service layer |
| NFR-010 | EF Core migrations (no hand-rolled DDL) | ✅ Two migrations exist |

---

## 2. Bug Register

| ID | Description | Severity | Location | Fix |
|---|---|---|---|---|
| BUG-01 | `GenerateAuthorizationNumber` uses `Random.Shared.Next(10000,99999)` — 90,000 possible values per day; collisions possible at scale | Low (demo) / Med (prod) | `WorkflowService.cs:533` | Replace with `Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()` or add a DB unique index + retry loop |
| BUG-02 | `WorkflowService_ConcurrentDecision_ShouldThrowDbUpdateConcurrencyException` cannot be tested in EF InMemory (RowVersion not enforced) | Test coverage gap | — | Requires `Tests.Integration` with real SQL (deferred T-D6) |

---

## 3. Gap Definitions

### GAP-01 — Specialist Dashboard: "Requests past decision due date"

**PRD §12.1:** "Requests past decision due date" listed as a Specialist KPI.  
**Current:** `SpecialistDashboard` has `MyDrafts, MySubmitted, MyPendingInfo, MyApprovedThisMonth, MyDeniedThisMonth, MyExpiringSoon`. No overdue count scoped to the specialist's own requests.  
**Fix:** Add `int MyOverdueRequests` to `SpecialistDashboard` record. In `GetSpecialistDashboardAsync`, add:  
```csharp
var myOverdue = await db.PaRequests
    .Where(r => r.CreatedByUserID == specialistId
             && r.DecisionDueDate < today
             && !TerminalStatuses.Contains(r.Status))
    .CountAsync(ct);
```
Add a red KPI card to `Home.razor` Specialist panel.

### GAP-02 — Specialist Dashboard: "Recent activity feed"

**PRD §12.1:** "Recent activity feed (last 10 status changes on my requests)."  
**Current:** No `GetRecentActivityAsync` method exists in `IReportingService` or `ReportingService`.  
**Fix:**  
1. Add `record RecentActivityItem(string RequestNumber, string PatientName, string NewStatus, DateTime OccurredAt)` to `ReportingModels.cs`.  
2. Add `Task<IReadOnlyList<RecentActivityItem>> GetRecentActivityAsync(string specialistId, int count = 10, CancellationToken ct = default)` to `IReportingService`.  
3. Implement in `ReportingService` — join `StatusHistories` on `PaRequests` where `CreatedByUserID == specialistId`, order by `OccurredAt DESC`, take `count`.  
4. Load in `Home.razor` `OnInitializedAsync` alongside the specialist dashboard and render as a small `<table>` below the KPI cards.  
5. Add unit test `GetRecentActivityAsync_ReturnsLastNStatusChanges_OrderedByDescending`.

### GAP-03 — Billing Dashboard: "Pending requests count"

**PRD §12.1:** "Pending requests count" for Billing Manager view.  
**Current:** `BillingDashboard` has no pending-requests field.  
**Fix:** Add `int PendingRequests` to `BillingDashboard` record. In `GetBillingDashboardAsync`, add:  
```csharp
var pending = await db.PaRequests
    .Where(r => ActiveStatuses.Contains(r.Status))
    .CountAsync(ct);
```
Where `ActiveStatuses = { Draft, Submitted, UnderReview, PendingInfo }`.  
Add a stat card to the Billing panel in `Home.razor`.

### GAP-04 — Billing Dashboard: "Top 5 denial reasons"

**PRD §12.1:** "Top 5 denial reasons (bar chart or table)" for Billing Manager.  
**Current:** `GetDenialsByReasonAsync(from, to)` exists in `ReportingService` and is used on the Reports page, but is not called from the Billing dashboard path in `Home.razor`.  
**Fix:**  
1. Add `private IReadOnlyList<DenialReasonSummary> _billingTopDenials = [];` field to `Home.razor`.  
2. In `LoadBillingAsync`, call `GetDenialsByReasonAsync(DateOnly.FromDateTime(firstOfMonth), DateOnly.FromDateTime(today))` and take the top 5 by count.  
3. Render as a `<table class="table table-sm">` below the existing Billing KPI cards with columns Reason | Count.  
**No service changes needed** — the method already exists.

### GAP-05 — Reviewer Dashboard: "Requests pending > 5 days"

**PRD §12.1:** "Requests pending > 5 days."  
**Current:** `ReviewerDashboard` has `OverdueRequests` (past `DecisionDueDate`), which is a different metric. "Pending > 5 days" = requests in `Submitted` or `UnderReview` status for more than 5 calendar days with no decision.  
**Fix:** Add `int PendingMoreThan5Days` to `ReviewerDashboard`. In `GetReviewerDashboardAsync`, add:  
```csharp
var cutoff = today.AddDays(-5);
var pending5 = await db.PaRequests
    .Where(r => (r.Status == PaStatus.Submitted || r.Status == PaStatus.UnderReview)
             && r.SubmittedAt.HasValue && r.SubmittedAt.Value.Date <= cutoff)
    .CountAsync(ct);
```
Add a KPI card to the Reviewer panel.

### GAP-06 — Reviewer Dashboard: "Average decision time (days) this month"

**PRD §12.1:** "Average decision time (days) this month."  
**Current:** `GetTurnaroundTimeAsync` exists for the Reports page but `ReviewerDashboard` has no average decision time field.  
**Fix:** Add `double AvgDecisionDaysThisMonth` to `ReviewerDashboard`. In `GetReviewerDashboardAsync`, add:  
```csharp
var decided = await db.PaRequests
    .Where(r => (r.Status == PaStatus.Approved || r.Status == PaStatus.Denied)
             && r.DecisionRenderedAt.HasValue
             && r.DecisionRenderedAt.Value >= firstOfMonth)
    .Select(r => EF.Functions.DateDiffDay(r.SubmittedAt!.Value, r.DecisionRenderedAt!.Value))
    .ToListAsync(ct);  // small set — this month only
var avg = decided.Count > 0 ? decided.Average() : 0.0;
```
Add a stat card to the Reviewer panel (e.g., "Avg Decision Time" with value "@_reviewerDash.AvgDecisionDaysThisMonth:F1 days").

---

## 4. Task DAG

```
GAP-01 ─────────────────────────────────────────────┐
GAP-02 ─┬─ new service method (GetRecentActivity) ──┤
GAP-03 ─┤                                            ├─ TEST-NEW ─┐
GAP-04 ─┤─ (no service change needed)               │            │
GAP-05 ─┤                                            │            ├─ DONE
GAP-06 ─┘────────────────────────────────────────────┘            │
BUG-01 ─────────────────────────────────────────────────────────┘
```

All GAP tasks are **independent of each other** (they each touch different records and dashboard panels). They can be implemented in any order or in parallel branches.

---

## 5. Phased Execution Plan

### Phase 1 — Dashboard parity (all independent, target: ≤ 1 day)

| Task | Files Changed | Complexity | Parallel? |
|---|---|---|---|
| **GAP-01** Specialist: add `MyOverdueRequests` stat | `ReportingModels.cs`, `ReportingService.cs`, `Home.razor`, `ReportingServiceTests.cs` | S | Yes |
| **GAP-03** Billing: add `PendingRequests` stat | `ReportingModels.cs`, `ReportingService.cs`, `Home.razor`, `ReportingServiceTests.cs` | S | Yes |
| **GAP-04** Billing: show top 5 denial reasons table | `Home.razor` only (+ Home.razor code-behind block) | XS | Yes |
| **GAP-05** Reviewer: add `PendingMoreThan5Days` stat | `ReportingModels.cs`, `ReportingService.cs`, `Home.razor`, `ReportingServiceTests.cs` | S | Yes |
| **GAP-06** Reviewer: add `AvgDecisionDaysThisMonth` | `ReportingModels.cs`, `ReportingService.cs`, `Home.razor`, `ReportingServiceTests.cs` | S | Yes |

**Dependency:** None. All five can be executed in sequence or in parallel git worktrees.  
**Validation:** After each task, run `dotnet test` — count must stay ≥ 180. Visually verify with seeded demo data.

### Phase 2 — Specialist activity feed (target: ≤ 2 days)

| Task | Files Changed | Complexity | Parallel? |
|---|---|---|---|
| **GAP-02** Specialist: recent activity feed | `ReportingModels.cs`, `IReportingService.cs`, `ReportingService.cs`, `Home.razor`, `ReportingServiceTests.cs` | M | No (new interface method) |

**Dependency:** GAP-01 should be done first (model file already touched) to avoid merge conflict on `ReportingModels.cs`.

### Phase 3 — Bug and polish (target: ≤ 1 day)

| Task | Files Changed | Complexity |
|---|---|---|
| **BUG-01** AUTH number uniqueness | `WorkflowService.cs` (one line) + `WorkflowServiceTests.cs` (update format assertion) | XS |

### Phase 4 — Deferred

| Task | Reason |
|---|---|
| `Tests.Integration` (Testcontainers) | Requires Docker in CI; `WorkflowService_ConcurrentDecision_ShouldThrowDbUpdateConcurrencyException` is the primary motivator. Explicitly deferred (T-D6). |

---

## 6. Detailed Task Specifications

### TASK-01 · GAP-01: Specialist "My Overdue Requests" dashboard stat

**PRD Requirement:** §12.1 — "Requests past decision due date" for Specialist view  
**Dependencies:** None  
**Affected Files:**
- `Prior Authorization Workflow Tracker/Services/Models/ReportingModels.cs` — add `int MyOverdueRequests` to `SpecialistDashboard`
- `Prior Authorization Workflow Tracker/Services/ReportingService.cs` — add `CountAsync` in `GetSpecialistDashboardAsync`
- `Prior Authorization Workflow Tracker/Components/Pages/Home.razor` — add KPI card to Specialist panel
- `Tests.Unit/Services/ReportingServiceTests.cs` — add test `GetSpecialistDashboardAsync_CountsOverdueRequests`

**Complexity:** S  
**Risk:** Low — additive change, no existing code modified  
**Validation:** Seed a Submitted request with `DecisionDueDate` in the past; assert `MyOverdueRequests == 1`.

---

### TASK-02 · GAP-02: Specialist "Recent Activity Feed"

**PRD Requirement:** §12.1 — "Recent activity feed (last 10 status changes on my requests)"  
**Dependencies:** TASK-01 (same model file)  
**Affected Files:**
- `ReportingModels.cs` — add `record RecentActivityItem(string RequestNumber, string PatientName, PaStatus NewStatus, DateTime OccurredAt)`
- `IReportingService.cs` — add `GetRecentActivityAsync(string specialistId, int count = 10, CancellationToken ct = default)`
- `ReportingService.cs` — implement the method
- `Home.razor` — add `_recentActivity` field, load in `LoadSpecialistAsync`, render as table
- `ReportingServiceTests.cs` — add `GetRecentActivityAsync_ReturnsLatestNItems_OrderedDescending`

**Implementation note:** Query `StatusHistories` joined to `PaRequests` where `PaRequest.CreatedByUserID == specialistId`. Use `AsNoTracking().OrderByDescending(h => h.OccurredAt).Take(count)`.

**Complexity:** M  
**Risk:** Low  
**Validation:** Seed 15 status history rows for specialist A and 5 for specialist B. Assert only A's top 10 are returned, in descending order.

---

### TASK-03 · GAP-03: Billing "Pending Requests Count"

**PRD Requirement:** §12.1 — "Pending requests count" for Billing Manager  
**Dependencies:** None  
**Affected Files:**
- `ReportingModels.cs` — add `int PendingRequests` to `BillingDashboard`
- `ReportingService.cs` — add `CountAsync` in `GetBillingDashboardAsync`
- `Home.razor` — add KPI card to Billing panel
- `ReportingServiceTests.cs` — update `GetBillingDashboardAsync_ComputesApprovalRateFromServerSideCounts` to also assert `PendingRequests`

**Complexity:** S  
**Risk:** Low  
**Validation:** Existing test updated; assert pending count matches seeded data.

---

### TASK-04 · GAP-04: Billing "Top 5 Denial Reasons" table

**PRD Requirement:** §12.1 — "Top 5 denial reasons (bar chart or table)"  
**Dependencies:** None (existing service method reused)  
**Affected Files:**
- `Home.razor` only — add `_billingTopDenials` field, call `GetDenialsByReasonAsync` in `LoadBillingAsync`, render table

**Implementation:**
```csharp
// In Home.razor @code — LoadBillingAsync:
var thisMonthFrom = DateOnly.FromDateTime(new DateTime(_now.Year, _now.Month, 1));
var thisMonthTo   = DateOnly.FromDateTime(_now);
_billingTopDenials = (await ReportingSvc.GetDenialsByReasonAsync(thisMonthFrom, thisMonthTo))
    .OrderByDescending(d => d.Count).Take(5).ToList();
```

**Complexity:** XS — pure UI addition using existing service method  
**Risk:** None  
**Validation:** Visual check with seeded denial data (12 denial reasons in `DbSeeder.cs`).

---

### TASK-05 · GAP-05: Reviewer "Pending > 5 Days" stat

**PRD Requirement:** §12.1 — "Requests pending > 5 days"  
**Dependencies:** None  
**Affected Files:**
- `ReportingModels.cs` — add `int PendingMoreThan5Days` to `ReviewerDashboard`
- `ReportingService.cs` — add `CountAsync` in `GetReviewerDashboardAsync`
- `Home.razor` — add KPI card to Reviewer panel
- `ReportingServiceTests.cs` — add `GetReviewerDashboardAsync_CountsPendingMoreThan5Days`

**Complexity:** S  
**Risk:** Low  
**Validation:** Seed one Submitted request with `SubmittedAt` = 6 days ago, one with `SubmittedAt` = 3 days ago. Assert `PendingMoreThan5Days == 1`.

---

### TASK-06 · GAP-06: Reviewer "Average Decision Time This Month"

**PRD Requirement:** §12.1 — "Average decision time (days) this month"  
**Dependencies:** None  
**Affected Files:**
- `ReportingModels.cs` — add `double AvgDecisionDaysThisMonth` to `ReviewerDashboard`
- `ReportingService.cs` — add average calculation in `GetReviewerDashboardAsync`
- `Home.razor` — add stat card (e.g., "Avg Decision: @_reviewerDash.AvgDecisionDaysThisMonth:F1 days")
- `ReportingServiceTests.cs` — add `GetReviewerDashboardAsync_ComputesAvgDecisionTime`

**Implementation note:** Load decided requests this month into memory (small set — this-month only), compute `Average(DecisionRenderedAt - SubmittedAt)` in C# to avoid `EF.Functions.DateDiffDay` InMemory incompatibility. Return 0.0 when no decisions this month.

**Complexity:** S  
**Risk:** Low — in-memory average on a small set is correct for this scope  
**Validation:** Seed 3 decided requests with known turnaround days (2, 4, 6). Assert `AvgDecisionDaysThisMonth ≈ 4.0`.

---

### TASK-07 · BUG-01: AUTH number collision resistance

**PRD Requirement:** §8.5 — AUTH number must be a unique reference  
**Dependencies:** None  
**Affected Files:**
- `WorkflowService.cs:533` — replace `Random.Shared.Next(10_000, 99_999)` with `Convert.ToHexString(RandomNumberGenerator.GetBytes(3))` (gives 6-char uppercase hex, ~16.7M values/day)
- `WorkflowServiceTests.cs` — update the format assertion regex from `\d{5}` to `[0-9A-F]{6}`

**Complexity:** XS  
**Risk:** Low — format change only; PRD specifies `AUTH-YYYYMMDD-XXXXX` (5 alphanumeric), hex satisfies "XXXXX" with 6 chars (still a short suffix). Alternatively keep 5 digits and add a DB unique index with retry.

**Recommended fix:**
```csharp
private static string GenerateAuthorizationNumber(DateTime utcNow)
{
    var datePart = utcNow.ToString("yyyyMMdd");
    Span<byte> buf = stackalloc byte[3];
    RandomNumberGenerator.Fill(buf);
    var suffix = Convert.ToHexString(buf); // 6 uppercase hex chars
    return $"AUTH-{datePart}-{suffix}";
}
```
Add `using System.Security.Cryptography;` at top of file.

---

## 7. New Unit Tests Required

| Test Name | Covers | Complexity |
|---|---|---|
| `GetSpecialistDashboardAsync_CountsOverdueRequests` | GAP-01, §12.1 | S |
| `GetRecentActivityAsync_ReturnsLatestNItems_OrderedDescending` | GAP-02, §12.1 | S |
| `GetRecentActivityAsync_IsScopedToSpecialist` | GAP-02, §12.1 | S |
| `GetBillingDashboardAsync_ReturnsPendingRequestsCount` | GAP-03, §12.1 | S |
| `GetReviewerDashboardAsync_CountsPendingMoreThan5Days` | GAP-05, §12.1 | S |
| `GetReviewerDashboardAsync_ComputesAvgDecisionTime` | GAP-06, §12.1 | S |
| `GetReviewerDashboardAsync_AvgDecisionTime_IsZero_WhenNoDecisionsThisMonth` | GAP-06 | XS |
| `GenerateAuthorizationNumber_IsUnique_UnderLoad` | BUG-01 | XS |

**Projected test count after Phase 1–3:** ≥ 188

---

## 8. Files Not Requiring Changes

The following files are complete and PRD-compliant as-is:

| File | Reason |
|---|---|
| `WorkflowService.cs` | All 16 transitions + AdminOverride + ExpireAsync |
| `BusinessRuleService.cs` | All 14 rules, BR-001–BR-014 |
| `PaRequestService.cs` | Full CRUD, queue, comments, documents |
| `CsvExportService.cs` | 18 columns, correct filename, `ExportDownloaded` audit event |
| `AuditService.cs` | CorrelationId, IpAddress, swallows errors |
| `NotificationService.cs` | All 4 notification types |
| `ExpirationJob.cs` | SYSTEM user, hosted service registration |
| `UserManagementService.cs` | Create/deactivate/reactivate/role-change |
| `Queue.razor` | All 6 filters, 4 sort columns, `[APPEAL]` badge |
| `Reports.razor` | All 6 report tabs, CSV export button |
| `Admin/Users.razor` | Full CRUD + expiration trigger |
| `Admin/AuditLog.razor` | Filters + deep-link support |
| `StatusBadge.razor` | All 9 statuses, `badge-appealed`, `badge-withdrawn` |
| `CorrelationIdMiddleware.cs` | `Items["CorrelationId"]` set |
| `Migrations/` | Both migrations present, 4 SQL views |
| `DbSeeder.cs` | All §14 seed data |
| `Program.cs` | All services registered |

---

## 9. First 5 Recommended Tasks

Execute in this order (each takes ≤ 2 hours):

1. **TASK-04 (GAP-04)** — Billing top-5 denial reasons: zero service changes, pure Home.razor addition. Highest visible impact for least code.

2. **TASK-03 (GAP-03)** — Billing pending count: one `CountAsync` line in service, one field in record, one card in UI. Unblocks the billing dashboard being fully PRD-compliant.

3. **TASK-05 (GAP-05)** — Reviewer pending > 5 days: same pattern as TASK-03.

4. **TASK-06 (GAP-06)** — Reviewer avg decision time: same pattern; in-memory average avoids EF InMemory compatibility concern.

5. **TASK-01 (GAP-01)** — Specialist overdue count: same pattern; then TASK-02 (GAP-02) can follow with the service interface change.

All tasks produce zero breaking changes to existing tests.

---

## 10. Deferred Work

| ID | Description | Blocker |
|---|---|---|
| T-D6 | `Tests.Integration` project with Testcontainers SQL Server | Docker required in CI; EF InMemory ignores RowVersion |
| NFR-Reconnect | Verify `<ReconnectingOverlay>` or Blazor reconnect indicator | Low priority UX polish |
