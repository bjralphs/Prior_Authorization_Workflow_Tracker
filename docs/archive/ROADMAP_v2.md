# Prior Authorization Workflow Tracker — PRD Compliance Analysis & Execution Roadmap v2

**Generated:** 2026-05-04  
**Codebase state:** 165 unit tests passing · 0 build errors/warnings  
**PRD version:** 1.0 (May 3, 2026)  
**Previous tasks complete:** T-A1–A5, T-B1–B8, T-C1–C5, T-D1–D5, T-E1

---

## Table of Contents

1. [Current State Summary](#1-current-state-summary)
2. [PRD Compliance Matrix](#2-prd-compliance-matrix)
3. [Bug and Blind Spot List](#3-bug-and-blind-spot-list)
4. [Missing Test Coverage](#4-missing-test-coverage)
5. [Cleanup and Refactor Recommendations](#5-cleanup-and-refactor-recommendations)
6. [Phased Roadmap](#6-phased-roadmap)
7. [DAG Task List](#7-dag-task-list)
8. [Parallel Worktree Plan](#8-parallel-worktree-plan)
9. [Recommended First 3–5 Tasks](#9-recommended-first-35-tasks)

---

## 1. Current State Summary

The application is substantially complete. The entire service layer, workflow state machine, all 14 business rules, role-aware UI, 7-tab reporting, role-differentiated dashboard, admin user management, audit logging, notification system, CSV export, document upload, admin override, SQL reporting views, and seed data are all implemented and passing 165 unit tests.

### What is fully implemented

| Area | Files | Status |
|---|---|---|
| Entity models (all 10 tables + Identity) | `Models/` | ✅ |
| EF Core migrations (InitialCreate + AddReportingViews) | `Migrations/` | ✅ |
| ASP.NET Core Identity (5 roles + SYSTEM user) | `DbSeeder.cs`, `Program.cs` | ✅ |
| Serilog + CorrelationId middleware | `Program.cs`, `Middleware/` | ✅ |
| FluentValidation (BR-003/004/006/007/011/012) | `Services/Validation/` | ✅ |
| All 14 BR rules (BR-001–BR-014) | `BusinessRuleService.cs` | ✅ |
| Full state machine + all transitions | `WorkflowService.cs` | ✅ |
| PA request CRUD, queue, documents, comments | `PaRequestService.cs` | ✅ |
| Expiration background job + SYSTEM user | `ExpirationJob.cs` | ✅ |
| Audit logging with CorrelationId | `AuditService.cs` | ✅ |
| In-app notifications | `NotificationService.cs` | ✅ |
| Full reporting (7 tabs + role dashboards) | `ReportingService.cs` | ✅ |
| User management (create/deactivate/reactivate/role) | `UserManagementService.cs` | ✅ |
| CSV export (18 columns, Billing+Admin, audit) | `CsvExportService.cs` | ✅ |
| Admin workflow override | `WorkflowService.AdminOverrideAsync` | ✅ |
| All UI pages (Queue, Detail, Submit, Edit, Reports, Admin, Notifications) | `Components/Pages/` | ✅ |
| Role-differentiated dashboard (5 panels) | `Home.razor` | ✅ |
| Login/logout with audit events | `Pages/Account/Login.cshtml.cs` | ✅ |
| SignalR reconnect overlay (NFR-011) | `wwwroot/app.css` | ✅ |
| Anti-forgery (NFR-005) | `Program.cs` `UseAntiforgery()` | ✅ |
| Exception hierarchy (5 types) | `Exceptions/` | ✅ |
| 165 unit tests | `Tests.Unit/` | ✅ |
| Seed data (8 plans, 25 codes, 12 denials, 50 requests, ~300 audit logs) | `DbSeeder.cs` | ✅ |

### Remaining gaps (concrete evidence only)

| Gap | Severity | PRD ref | Location |
|---|---|---|---|
| AuditLogs for system-initiated expiration have no UserID | Medium | §11 | `ExpirationJob → AuditService` |
| CsvExportService ignores `filter.Priority` | Low | §8.10 | `CsvExportService.cs` lines 55–75 |
| Queue.razor shows no `[APPEAL]` text badge on Appealed rows | Low | §8.7 | `Queue.razor` |
| RequestDetail doesn't display appeal deadline date for Denied requests | Low | §8.5 | `RequestDetail.razor` |
| NavMenu Notifications link has no unread count badge | Low | §16.1 | `NavMenu.razor` |
| No Delete Draft button in RequestDetail (only in EditRequest) | Low | §8.3 | `RequestDetail.razor` |
| No ExpirationJob unit tests | Low | §13.1 | `Tests.Unit/Services/` |
| No `ReactivateUserAsync` test | Low | §13 | `UserManagementServiceTests.cs` |
| `Tests.Integration` project (Testcontainers SQL Server) | Future | §13.1 | — |

---

## 2. PRD Compliance Matrix

### §7 — Database Schema

| Requirement | Status | Evidence |
|---|---|---|
| All 10 entity tables | ✅ Complete | `InitialCreate` migration |
| SQL reporting views (4 views) | ✅ Complete | `AddReportingViews` migration |
| All 8 recommended indexes (§7.3) | ✅ Complete | `AppDbContext.OnModelCreating` |
| RowVersion optimistic concurrency | ✅ Complete | `e.Property(r => r.RowVersion).IsRowVersion()` |
| Global soft-delete filter | ✅ Complete | `HasQueryFilter(r => !r.IsDeleted)` |

### §8 — Feature Requirements

| Requirement | Status | Evidence |
|---|---|---|
| PA Request Submission form (FR-001) | ✅ | `SubmitRequest.razor` + `PaRequestService.CreateDraftAsync` |
| Draft save with 3-field minimum | ✅ | `DRAFT` validation path in `PaRequestService` |
| RequestNumber `PA-YYYY-NNNNN` from IDENTITY | ✅ | `PaRequestService` post-save number assignment |
| DecisionDueDate = SubmittedAt + SLA × priority | ✅ | `WorkflowService.SubmitAsync` |
| Queue: all filters + sort + pagination (FR-002) | ✅ | `Queue.razor` + `PaRequestFilter` |
| Queue: "Due today" + "Overdue" badges | ✅ | `Queue.razor` |
| Queue: Appealed rows colored | ✅ | `Queue.razor` line 378 — `table-info` |
| Queue: `[APPEAL]` text badge on Appealed rows | ❌ Bug | Missing from `Queue.razor` (only row color, no badge) |
| Request Detail: full fields + timeline + comments + docs (FR-003) | ✅ | `RequestDetail.razor` |
| RequestDetail: provider/submitter/reviewer shown as names (not GUIDs) | ✅ | `LoadUserNamesAsync()` batch query |
| RequestDetail: appeal deadline displayed for Denied+IsAppealable | ❌ Partial | Button shows state; deadline date not shown in the detail card |
| Delete Draft button in RequestDetail | ❌ Missing | Only available via `/requests/{id}/edit` |
| Full state machine — all 17 transitions (FR-004) | ✅ | `WorkflowService.Transitions` dictionary |
| Decision rendering: approve with auth fields + AUTH number (FR-005) | ✅ | `WorkflowService.ApproveAsync` |
| Decision rendering: deny with denial reason + appeal notification | ✅ | `WorkflowService.DenyAsync` |
| Expiration job (nightly + immediate + SYSTEM user) (FR-006) | ✅ | `ExpirationJob.cs` |
| Admin manual expiration trigger | ✅ | `IExpirationJobTrigger` + `Users.razor` |
| Appeal tracking with deadline enforcement (FR-007, BR-008) | ✅ | `WorkflowService.AppealAsync` |
| Appealed `[APPEAL]` badge in RequestDetail | ✅ | `RequestDetail.razor` line 41 |
| Admin user management: create/deactivate/reactivate/role (FR-008) | ✅ | `UserManagementService` + `Users.razor` |
| Admin audit log with filters | ✅ | `AuditLog.razor` |
| Admin workflow override with justification | ✅ | `WorkflowService.AdminOverrideAsync` |
| Dashboard: role-differentiated (§12.1 — 5 views) (FR-009) | ✅ | `Home.razor` 5-panel structure |
| CSV export: 18 columns, Billing+Admin (FR-010) | ✅ | `CsvExportService.cs` 18-column WriteHeader |
| CSV export: Priority filter applied | ❌ Bug | `CsvExportService.ExportRequestsAsync` lines 55–75 omit Priority filter |
| CSV export: `PA_Export_YYYYMMDD_HHmmss.csv` filename | ✅ | `CsvExportService.cs` |

### §9 — Business Rules

| Rule | Status | Evidence |
|---|---|---|
| BR-001 RequiresPriorAuth | ✅ | `BusinessRuleService.ValidateForSubmissionAsync` |
| BR-002 SLA-based DecisionDueDate (plan-specific, no hardcoding) | ✅ | `WorkflowService.SubmitAsync` |
| BR-003 ICD-10 regex (compiled, ReDoS-safe) | ✅ | `PaRequestSubmitValidator` |
| BR-004 Units 1–999 | ✅ | `PaRequestSubmitValidator` |
| BR-005 Duplicate active request (by patient+procedure+plan, not payer) | ✅ | `BusinessRuleService` |
| BR-006 AuthEndDate > AuthStartDate | ✅ | `ApprovalDecisionValidator` |
| BR-007 GrantedUnits ≤ RequestedUnits | ✅ | `BusinessRuleService.ValidateForApprovalAsync` |
| BR-008 Appeal within deadline (inclusive) | ✅ | `BusinessRuleService.ValidateAppealEligibilityAsync` |
| BR-009 Withdraw prohibited from Approved/Expired/Denied/Draft | ✅ | `BusinessRuleService.ValidateCanWithdraw` |
| BR-010 Clinical justification ≥ 50 chars | ✅ | `PaRequestSubmitValidator` |
| BR-011 AuthStartDate ≥ today | ✅ | `ApprovalDecisionValidator` |
| BR-012 GrantedUnits ≥ 1 | ✅ | `ApprovalDecisionValidator` |
| BR-013 Resubmit requires new comment or document post-PendingInfo | ✅ | `BusinessRuleService.ValidateResubmissionAsync` |
| BR-014 Hard delete only by creating Specialist or Admin | ✅ | `BusinessRuleService.ValidateCanHardDelete` |

### §10 — RBAC

| Permission | Status | Evidence |
|---|---|---|
| Provider sees only own patients | ✅ | `PaRequestService.GetQueueAsync` + `GetByIdAsync` |
| Internal comments hidden from Provider | ✅ | `PaRequestService.GetCommentsAsync` RBAC check |
| Billing Manager cannot upload documents | ✅ | `PaRequestService.AddDocumentAsync` role check |
| Service-layer role re-validation on all mutations | ✅ | `RequireActive()` + `RequireRole()` in WorkflowService |
| Admin override requires role + justification | ✅ | `WorkflowService.AdminOverrideAsync` |

### §11 — Audit Logging

| Event | Status | Evidence |
|---|---|---|
| Request created | ✅ | `PaRequestService.CreateDraftAsync` → `"Created"` |
| Status transition | ✅ | All WorkflowService methods → `"StatusChanged"` |
| Decision rendered | ✅ | `ApproveAsync`/`DenyAsync` → `"DecisionRendered"` |
| Comment added | ✅ | `AddCommentAsync` → `"Created"` |
| Document uploaded | ✅ | `AddDocumentAsync` → `"Uploaded"` |
| Appeal filed | ✅ | `AppealAsync` → `"AppealFiled"` |
| Request withdrawn | ✅ | `WithdrawAsync` → `"Withdrawn"` |
| Request expired (system) | ⚠️ Bug | `ExpireAsync` → `"Expired"` written but `UserID`/`UserName` is `""` (background thread, no HTTP context) |
| User created/deactivated | ✅ | `UserManagementService` → `"Created"` / `"Deactivated"` |
| Role changed | ✅ | `ChangeRoleAsync` → `"RoleChanged"` |
| Admin override | ✅ | `AdminOverrideAsync` → `"AdminOverride"` |
| User login | ✅ | `Login.cshtml.cs` → `"Login"` |
| Failed login | ✅ | `Login.cshtml.cs` → `"FailedLogin"` |
| CSV export downloaded | ✅ | `CsvExportService` → `"ExportDownloaded"` |

### §12 — Reporting and Dashboard

| Report | Status | Evidence |
|---|---|---|
| Status Summary (with overdue %) | ✅ | `ReportingService.GetStatusSummaryAsync` |
| Denial Analysis (by reason, date range) | ✅ | `ReportingService.GetDenialsByReasonAsync` |
| Approval Rate (by priority, date range) | ✅ | `ReportingService.GetApprovalRateAsync` |
| Expiring Authorizations (configurable window) | ✅ | `ReportingService.GetExpiringAuthorizationsAsync` |
| Turnaround Time (avg days, by priority) | ✅ | `ReportingService.GetTurnaroundTimeAsync` |
| Provider Submission Volume | ✅ | `ReportingService.GetProviderVolumeAsync` |
| Monthly Volume (trend by 3/6/12 months) | ✅ | `ReportingService.GetMonthlyVolumeAsync` |
| Specialist dashboard | ✅ | `ReportingService.GetSpecialistDashboardAsync` |
| Provider dashboard | ✅ | `ReportingService.GetProviderDashboardAsync` |
| Reviewer dashboard | ✅ | `ReportingService.GetReviewerDashboardAsync` |
| Billing Manager dashboard | ✅ | `ReportingService.GetBillingDashboardAsync` |

### §13 — Unit Testing

| Target | Coverage | Current | Gap |
|---|---|---|---|
| `WorkflowService` | ≥ 90% | 31 tests — all transitions, concurrency, appeal, admin override | Complete |
| `PaRequestService` | ≥ 80% | 25 tests — CRUD, queue, comments, documents | Complete |
| `BusinessRuleValidator` | 100% | 26 tests — all BR-001–BR-014 | Complete |
| `AuditService` | ≥ 70% | 6 tests | Complete |
| `NotificationService` | ≥ 70% | 8 tests | Complete |
| `ReportingService` | ≥ 60% | 21 tests | Complete |
| `UserManagementService` | — | 10 tests | Missing: `ReactivateUserAsync` |
| `CsvExportService` | — | 5 tests | Missing: Priority filter test |
| `ExpirationJob` | — | 0 tests | Entirely untested |

### §14 — Seed Data

| Entity | Target | Status |
|---|---|---|
| Insurance Plans | 8 | ✅ 8 seeded |
| Procedure Codes | 25 | ✅ 25 seeded (1 without RequiresPriorAuth for BR-001 demo) |
| Denial Reasons | 12 | ✅ 12 seeded |
| PA Requests | 50 | ✅ 50 seeded across all 9 statuses |
| Distinct Provider accounts | ≥ 3 | ✅ 3 providers (provider1/2/3) |
| Distinct Specialist accounts | ≥ 2 | ✅ 2 specialists (specialist1/specialist2) |
| PA Status History | ~150 | ✅ Seeded per request lifecycle |
| PA Comments | ~80 | ✅ Seeded in `SeedPaRequestsAsync` |
| Audit Logs | ~300 | ✅ `SeedAuditLogsAsync` with CorrelationIds |

### §15 — Non-Functional Requirements

| NFR | Status | Evidence |
|---|---|---|
| NFR-001 Queue loads ≤ 2 s (50 records) | ✅ | LINQ queries with pagination + AsNoTracking |
| NFR-002 `AsNoTracking()` on all reads | ✅ | Applied in all service query methods |
| NFR-003 PBKDF2-SHA256 passwords | ✅ | Identity default, not overridden |
| NFR-004 No raw SQL string concatenation | ✅ | All queries use parameterized LINQ/EF |
| NFR-005 Anti-forgery | ✅ | `UseAntiforgery()` + `<AntiforgeryToken>` on logout form |
| NFR-006 Unauthorized access logged | ✅ | `ExceptionHandlingMiddleware` + `AuthorizationException` |
| NFR-007 Graceful error pages | ✅ | `Error.razor` + `app.UseExceptionHandler` |
| NFR-008 Accessible form labels | ✅ | All form fields have `<label>` in Razor pages |
| NFR-009 No business logic in components | ✅ | Components call services only |
| NFR-010 EF migrations for all schema changes | ✅ | No manual SQL DDL in startup code |
| NFR-011 SignalR reconnect indicator | ✅ | CSS classes `#components-reconnect-modal` in `app.css` |

---

## 3. Bug and Blind Spot List

### BUG-01 — ExpirationJob audit entries have no user attribution (Medium)

**Severity:** Medium (compliance gap — AuditLogs incomplete)  
**Location:** `WorkflowService.ExpireAsync` → `AuditService.LogAsync`  
**Evidence:** `WorkflowService.ExpireAsync` correctly writes `ChangedByUserID = DbSeeder.SystemUserId` in `PaStatusHistory`. However, `AuditService.LogAsync` reads `_currentUser.UserId` and `_currentUser.UserName` from `ICurrentUserService`, which is backed by `CurrentUserService` → `AuthenticationStateProvider`. In `ExpirationJob.ExpireSingleAsync`, the service is resolved from a background DI scope with no active HTTP context or Blazor circuit; `AuthenticationStateProvider.GetAuthenticationStateAsync()` returns an anonymous `ClaimsPrincipal`, so `UserId` returns `string.Empty` and `UserName` returns `string.Empty`.  
**Result:** `AuditLogs.UserID` = `""`, `AuditLogs.UserName` = `""` for every expiration event.  
**Fix:** Pass `DbSeeder.SystemUserId` / `"SYSTEM"` directly in `WorkflowService.ExpireAsync` when writing the audit entry, or have `ExpirationJob` write its own audit log entry using a directly-resolved `IAuditService` with a system-context wrapper.

### BUG-02 — CsvExportService ignores Priority filter (Low)

**Severity:** Low (functional inconsistency — exported data doesn't match visible filter)  
**Location:** `CsvExportService.ExportRequestsAsync`, lines 55–75  
**Evidence:** `PaRequestFilter` has a `Priority` field. `GetQueueAsync` applies `filter.Priority.HasValue` correctly (line ~190 of `PaRequestService.cs`). But `CsvExportService.ExportRequestsAsync` builds its own query and only applies Status, InsurancePlanID, ProcedureCodeID, SubmittedFrom, SubmittedTo — Priority is not applied. A Billing Manager who filters by "Urgent" then exports will receive all-priority records.  
**Fix:** Add `if (filter.Priority.HasValue) query = query.Where(r => r.Priority == filter.Priority.Value);` in `CsvExportService` after the InsurancePlanID filter, matching the pattern in `GetQueueAsync`.

### BUG-03 — Queue.razor: Appealed rows have no `[APPEAL]` text badge (Low)

**Severity:** Low (UX — PRD §8.7 explicitly requires the badge)  
**Location:** `Queue.razor` (table rows, around the StatusBadge rendering)  
**Evidence:** PRD §8.7: "Appealed requests re-enter reviewer queue with `[APPEAL]` badge." `Queue.razor` line 378 assigns `table-info` row color for Appealed status, and `StatusBadge.razor` shows "Appealed" text. But no additional `[APPEAL]` visual indicator (e.g., a separate badge) is shown in the queue list — in contrast to `RequestDetail.razor` which does show the `<span class="badge badge-appealed text-white">APPEAL</span>` indicator.  
**Fix:** Add a conditional badge after `<StatusBadge>` in the queue table row: `@if (item.Status == PaStatus.Appealed) { <span class="badge badge-appealed text-white ms-1">APPEAL</span> }`.

### BLIND-01 — RequestDetail doesn't display appeal deadline date for Denied requests (Low)

**Severity:** Low (UX — PRD §8.5 says "displayed to Specialist")  
**Location:** `RequestDetail.razor` denial detail section  
**Evidence:** PRD §8.5: "If IsAppealable = true, appeal deadline is calculated and displayed to Specialist." `RequestDetail.razor` computes `denialDecision.Value.AddDays(deadlineDays)` only inside `RenderActionButtons()` to decide whether to enable the appeal button. The detail card (`<dl class="row">`) for Denied requests shows `DenialReason.Code` and `DenialNotes`, but not the deadline date itself. A Specialist reading the detail page doesn't see "Appeal by: 2026-06-04".  
**Fix:** In the denial section of the detail card, add: `@if (_request.DenialReason?.IsAppealable == true && _request.DecisionRenderedAt.HasValue) { <dt>Appeal Deadline</dt><dd>@_request.DecisionRenderedAt.Value.AddDays(_request.DenialReason.AppealDeadlineDays).ToString("yyyy-MM-dd")</dd> }`.

### BLIND-02 — NavMenu Notifications link shows no unread count badge (Low)

**Severity:** Low (UX convenience)  
**Location:** `NavMenu.razor`, Notifications nav item  
**Evidence:** `Notifications.razor` correctly shows `<span class="badge bg-danger ms-2">@_unread.Count</span>` in the page header. But `NavMenu.razor` renders only: `<NavLink ... href="/notifications">Notifications</NavLink>`. Users must navigate to the page to discover they have unread notifications.  
**Fix:** Inject `INotificationService` and `ICurrentUserService` into `NavMenu.razor`, call `GetUnreadCountAsync` in `OnInitializedAsync`, and render a small danger badge after the "Notifications" text when `_unreadCount > 0`. Use `[CascadingParameter]` or a scoped state service to avoid double loading.

### BLIND-03 — Delete Draft accessible only via Edit page, not RequestDetail (Low)

**Severity:** Low (discoverability UX)  
**Location:** `RequestDetail.razor` Actions card  
**Evidence:** `EditRequest.razor` has a `<ConfirmationModal>` and `DeleteDraftAsync()` handler. `RequestDetail.razor` shows an "Edit" link for Draft/PendingInfo requests but no "Delete" button. A Specialist viewing a Draft request must click Edit → then Delete to remove it, which is not obvious.  
**Fix:** Add a "Delete Draft" button in `RequestDetail.razor`'s Actions card, visible only for Draft status and only for the creating Specialist or Admin, wired to `PaRequestSvc.DeleteDraftAsync(Id)` with a `ConfirmationModal`.

---

## 4. Missing Test Coverage

| ID | Test to add | Priority | File |
|---|---|---|---|
| TEST-01a | `ExpirationJob_RunCheck_ExpiresOverdueRequests` — seeds 2 Approved requests (1 expired, 1 active); verifies expired request transitions to Expired, active request untouched | Medium | New: `Tests.Unit/Services/ExpirationJobTests.cs` |
| TEST-01b | `ExpirationJob_DoubleExpiration_SkipsAlreadyExpired` — seeds 1 Expired request; verifies no exception and `skipped++` (not `expired++`) | Medium | Same |
| TEST-01c | `ExpirationJob_PerRequestFailure_DoesNotStopOtherRequests` — first request throws; second request expires successfully | Medium | Same |
| TEST-01d | `ExpirationJob_TriggerAsync_ReturnsCount` — admin-triggered manual check returns correct expired count | Low | Same |
| TEST-02 | `ReactivateUserAsync_HappyPath_SetsIsActiveTrue` + audit event | Low | `UserManagementServiceTests.cs` |
| TEST-03 | `ExportRequestsAsync_WithPriorityFilter_OnlyExportsMatchingRows` — verifies BUG-02 | Low | `CsvExportServiceTests.cs` |

---

## 5. Cleanup and Refactor Recommendations

### REF-01 — `GetDashboardSummaryAsync` client-side aggregation (Performance, Low priority)

**Location:** `ReportingService.GetDashboardSummaryAsync`  
**Issue:** Loads all `PaRequest` rows via `.ToListAsync()` then groups in memory. At 50 seed records + ≤500 records per NFR-001, this is acceptable. However, 7 scalar counts (open, overdue, expiring, etc.) could each be a server-side `CountAsync()` query, eliminating the full table scan.  
**Recommendation:** Replace the single `ToListAsync()` with 7 parallel `CountAsync()` calls. Same approach applies to `GetSpecialistDashboardAsync`, `GetProviderDashboardAsync`, etc. which also materialize rows before filtering client-side.  
**Risk:** Low. Pure read-only change; no behavior change at current scale.

### REF-02 — `RequestDetail.razor RenderActionButtons()` complexity (Maintainability, Low priority)

**Location:** `RequestDetail.razor` `RenderActionButtons()` method  
**Issue:** Uses `RenderFragment` with manual builder calls (`__builder.OpenComponent`, `b.AddAttribute`) to inject `AuthorizeView` wrappers. This is fragile and hard to read. A simpler pattern would be `@if` + `<AuthorizeView>` markup in the template.  
**Recommendation:** Replace the `RenderFragment` builder approach with a plain `@code` method returning nothing; move action buttons into conditional Razor markup blocks. Already a tech debt note in the file (`[SuppressMessage("Style", "IDE1006")]`).  
**Risk:** Medium (careful refactoring needed to preserve authorization logic).

### REF-03 — `RequestDetail.razor`: Load comments/history on action, not full reload (UX, Low priority)

**Location:** `RequestDetail.razor RunActionAsync()`  
**Issue:** After every action (approve, deny, comment, upload), `RunActionAsync` reloads only the request object and status history but not comments or documents. If a user posts a comment and immediately sees the page, the comment list may not update without a full reload.  
**Recommendation:** After `AddCommentAsync` or `AddDocumentAsync`, reload the relevant collection (`_comments = await PaRequestSvc.GetCommentsAsync(Id)` or `_documents = await PaRequestSvc.GetDocumentsAsync(Id)`).  
**Risk:** Low. Self-contained change per action handler.

### REF-04 — `CsvExportService` comment says "16 columns" but exports 18 (Documentation debt)

**Location:** `CsvExportService.cs` line ~110: `// 16 columns per PRD §8.10`  
**Issue:** The comment is stale. There are 18 columns in `WriteHeader`.  
**Fix:** Update comment to `// 18 columns: 16 per PRD §8.10 + SubmittedByUser + Reviewer (both required by §8.10)`.  
**Risk:** None.

---

## 6. Phased Roadmap

### Phase F — Bug Fixes (unblocked, high ROI, short tasks)

Critical correctness fixes. All can run in parallel (different files).

| ID | Description | Effort |
|---|---|---|
| T-F1 | Fix ExpirationJob audit log SYSTEM user attribution | S |
| T-F2 | Add Priority filter to CsvExportService | XS |
| T-F3 | Add [APPEAL] badge to Queue.razor rows | XS |

### Phase U — UI/UX Polish (unblocked, low risk)

| ID | Description | Effort |
|---|---|---|
| T-U1 | Show appeal deadline date in RequestDetail for Denied+IsAppealable | XS |
| T-U2 | Add unread notification count badge to NavMenu | S |
| T-U3 | Add Delete Draft button to RequestDetail | S |

### Phase T — Test Coverage (unblocked, parallel)

| ID | Description | Effort |
|---|---|---|
| T-T1 | ExpirationJob unit tests (4 scenarios) | M |
| T-T2 | ReactivateUserAsync test | XS |
| T-T3 | CsvExportService Priority filter test (validates BUG-02 fix) | XS |

### Phase R — Refactoring (optional, low priority)

| ID | Description | Effort |
|---|---|---|
| T-R1 | GetDashboardSummaryAsync server-side CountAsync | M |
| T-R2 | RequestDetail RenderActionButtons → markup refactor | M |
| T-R3 | Stale comment cleanup (CsvExportService, REF-04) | XS |

### Phase D — Deferred / Future

| ID | Description | Effort |
|---|---|---|
| T-D6 | Tests.Integration: Testcontainers SQL Server (views, FK constraints, raw SQL) | XL |

---

## 7. DAG Task List

Dependencies are expressed as `[depends on T-xxx]`. Tasks with no dependency entry are unblocked.

| ID | Description | PRD Ref | Deps | Complexity | Risk | Validation / Test Plan | Parallel? |
|---|---|---|---|---|---|---|---|
| **T-F1** | **Fix ExpirationJob audit SYSTEM attribution.** In `WorkflowService.ExpireAsync`, replace the `_audit.LogAsync(...)` call with a direct call that passes `DbSeeder.SystemUserId` and `"SYSTEM"` instead of relying on `ICurrentUserService`. Option A: add a `LogAsSystemAsync` overload to `IAuditService`. Option B: in `ExpireAsync`, detect that caller is background context and skip calling through `ICurrentUserService`. Simplest: pass extra params to the existing `LogAsync` overload signature. | §11 | none | S | Low | Verify `AuditLog.UserID = DbSeeder.SystemUserId` and `UserName = "SYSTEM"` after calling `ExpireAsync` in a test with anonymous user context. Add to `WorkflowServiceTests` or new `ExpirationJobTests`. | Yes |
| **T-F2** | **Add Priority filter to CsvExportService.** In `CsvExportService.ExportRequestsAsync`, add `if (filter.Priority.HasValue) query = query.Where(r => r.Priority == filter.Priority.Value);` after the existing `InsurancePlanID` filter. | §8.10 | none | XS | None | `ExportRequestsAsync_WithPriorityFilter_OnlyExportsMatchingRows`: seed Urgent + Routine requests; filter by Urgent; assert only 1 row in CSV. | Yes |
| **T-F3** | **[APPEAL] badge in Queue.razor.** After the `<StatusBadge Status="item.Status" />` in the queue table row, add a conditional `<span class="badge badge-appealed text-white ms-1">APPEAL</span>` when `item.Status == PaStatus.Appealed`. | §8.7 | none | XS | None | Manual: log in as Reviewer; verify Appealed rows show both the "Appealed" StatusBadge and the "APPEAL" overlay badge. | Yes |
| **T-U1** | **Show appeal deadline in RequestDetail.** In the denial detail `<dl>` section of `RequestDetail.razor`, add a new `<dt>Appeal Deadline</dt><dd>@deadline.ToString("yyyy-MM-dd")</dd>` row when `_request.Status == PaStatus.Denied && _request.DenialReason?.IsAppealable == true && _request.DecisionRenderedAt.HasValue`. Compute `deadline = _request.DecisionRenderedAt.Value.AddDays(_request.DenialReason.AppealDeadlineDays)`. | §8.5 | none | XS | None | Manual: navigate to a Denied+IsAppealable request; verify deadline date shown. | Yes |
| **T-U2** | **Unread notification badge in NavMenu.** Inject `INotificationService` and `ICurrentUserService` into `NavMenu.razor`. In `OnInitializedAsync`, call `GetUnreadCountAsync(CurrentUser.UserId)` and cache in `_unreadCount`. Render `<span class="badge bg-danger ms-1">@_unreadCount</span>` when `_unreadCount > 0`. Add `AuthorizeView` wrapper to only call the service when authenticated. | §16.1 | none | S | Low | Manual: create a notification for a user; sign in; verify badge appears in NavMenu. Dismiss notification; verify badge disappears (may require page reload or polling). | Yes |
| **T-U3** | **Delete Draft button in RequestDetail.** In `RequestDetail.razor`, inside `RenderActionButtons()`, for Draft status and Specialist+Admin roles, add a "Delete Draft" button that shows a `ConfirmationModal` and calls `PaRequestSvc.DeleteDraftAsync(Id)` followed by `Nav.NavigateTo("/requests")`. Re-use the existing `ConfirmationModal` component. | §8.3, BR-014 | none | S | Low | Manual: as Specialist, navigate to own Draft; click Delete Draft; confirm; verify redirected to queue and request is gone. As different Specialist, verify button absent. Add test: `DeleteDraftAsync_FromDetail_NonOwner_ThrowsAuthorizationException` (already covered in service tests, so UI test sufficient). | Yes |
| **T-T1** | **ExpirationJob unit tests.** Create `Tests.Unit/Services/ExpirationJobTests.cs`. Use `IDbContextFactory<AppDbContext>` mock pattern (same as WorkflowServiceTests). Seed Approved+expired, Approved+not-expired, and Expired requests. Test scenarios: (a) `RunCheck_ExpiresOverdue`, (b) `RunCheck_SkipsAlreadyExpired` (double-expiration guard), (c) `RunCheck_PerRequestFailure_ContinuesOthers`, (d) `TriggerAsync_ReturnCount`. | §13.1, §8.6 | T-F1 (fix SYSTEM attribution first for accurate audit assertions) | M | Low | All 4 tests green; no regressions in existing 165 tests. | Yes (after T-F1) |
| **T-T2** | **ReactivateUserAsync unit test.** Add to `UserManagementServiceTests.cs`: `ReactivateUserAsync_HappyPath_SetsIsActiveTrue` — mock `FindByIdAsync` returning inactive user, mock `UpdateAsync` succeeding; assert `IsActive == true` and audit mock called with `"Reactivated"`. Also add `ReactivateUserAsync_NonAdmin_ThrowsAuthorizationException`. | §13 | none | XS | None | New tests green; total count increases by 2. | Yes |
| **T-T3** | **CsvExportService Priority filter test.** Add to `CsvExportServiceTests.cs`: `ExportRequestsAsync_WithPriorityFilter_OnlyExportsMatchingRows` — seed 1 Urgent + 1 Routine request; call `ExportRequestsAsync(new PaRequestFilter { Priority = PaPriority.Urgent })`; parse CSV; assert 1 data row returned. This validates the BUG-02 fix. | §8.10 | T-F2 | XS | None | Test green; no regressions. | Yes (after T-F2) |
| **T-R1** | **GetDashboardSummaryAsync server-side aggregation.** Replace the `ToListAsync()` + in-memory grouping in `GetDashboardSummaryAsync` with 7 parallel `CountAsync()` calls using `Task.WhenAll`. Apply the same pattern to `GetSpecialistDashboardAsync`, `GetProviderDashboardAsync`, `GetReviewerDashboardAsync`, `GetBillingDashboardAsync`. | §15 NFR-001 | none | M | Medium | Existing `ReportingServiceTests` must stay green. Add `GetDashboardSummaryAsync_ReturnsCorrectCounts` test that seeds known data and asserts exact values. | No (touches multiple methods) |
| **T-R2** | **RequestDetail action buttons → Razor markup.** Rewrite `RenderActionButtons()` from a C# `RenderFragment` builder to plain `@if` + `<AuthorizeView>` Razor markup. Preserve all authorization logic and action bindings. | Maintainability | none | M | Medium | All existing manual workflow tests (approve/deny/appeal/withdraw) still work. No new behavior added. | No |
| **T-R3** | **Fix stale CsvExportService comment.** Change `// 16 columns per PRD §8.10` to `// 18 columns: 16 per PRD §8.10 + SubmittedByUser + Reviewer`. | Docs | none | XS | None | Build green. | Yes |
| **T-D6** | **Tests.Integration project (Testcontainers).** Create `Tests.Integration/` xUnit project. Add `Testcontainers.MsSql` NuGet package. Write tests that spin up a real SQL Server container, apply migrations, and validate: SQL reporting views return correct data, FK constraints enforce referential integrity, `LIKE`/`DATEADD` SQL functions in views work correctly. | §13.1 | All migrations stable | XL | High | All integration tests green in CI. | Separate worktree |

---

## 8. Parallel Worktree Plan

Tasks with no shared file conflicts can run in parallel git worktrees.

### Worktree 1 — Bug Fixes (`fix/bugs`)
Tasks: **T-F1, T-F2, T-F3**  
Files touched:
- `Services/WorkflowService.cs` (T-F1) — ExpireAsync audit call
- `Services/CsvExportService.cs` (T-F2) — Priority filter
- `Components/Pages/Requests/Queue.razor` (T-F3) — APPEAL badge  

No conflicts between these three tasks; can be committed as three separate PRs.

### Worktree 2 — UI/UX Polish (`feature/ux-polish`)
Tasks: **T-U1, T-U2, T-U3**  
Files touched:
- `Components/Pages/Requests/RequestDetail.razor` (T-U1, T-U3) — appeal date + delete button
- `Components/Layout/NavMenu.razor` (T-U2) — notification badge  

T-U1 and T-U3 both touch `RequestDetail.razor` — sequence them (U1 then U3), or batch in one commit.

### Worktree 3 — Test Coverage (`test/coverage`)
Tasks: **T-T1 (after T-F1), T-T2, T-T3 (after T-F2)**  
Files touched (new):
- `Tests.Unit/Services/ExpirationJobTests.cs` (T-T1) — new file
- `Tests.Unit/Services/UserManagementServiceTests.cs` (T-T2) — append
- `Tests.Unit/Services/CsvExportServiceTests.cs` (T-T3) — append  

No conflicts within worktree. T-T1 must follow T-F1 being merged; T-T3 must follow T-F2 being merged.

### Worktree 4 — Refactoring (`refactor/internals`)
Tasks: **T-R1, T-R2, T-R3**  
Files touched:
- `Services/ReportingService.cs` (T-R1) — refactor dashboard methods
- `Components/Pages/Requests/RequestDetail.razor` (T-R2) — RenderActionButtons
- `Services/CsvExportService.cs` (T-R3) — comment only  

T-R1 is independent. T-R2 conflicts with T-U1/T-U3 (same file). Either:
- Merge Worktree 2 (UX polish) first, then T-R2 in Worktree 4
- Or include T-R2 in Worktree 2

### Worktree 5 — Integration Tests (`feature/integration-tests`)
Task: **T-D6**  
Files touched (new):
- `Tests.Integration/` — entire new project  

Completely isolated; zero conflicts with any other worktree.

---

## 9. Recommended First 3–5 Tasks

Ordered by impact/effort ratio:

### 1. T-F2 — Add Priority filter to CsvExportService (XS, 5 min, zero risk)

Single `if` statement. Fixes a functional filter inconsistency that affects every Billing Manager CSV export. Unblocked.

```csharp
// CsvExportService.ExportRequestsAsync — add after InsurancePlanID filter:
if (filter.Priority.HasValue)
    query = query.Where(r => r.Priority == filter.Priority.Value);
```

**Then:** Add T-T3 to lock in the fix with a test.

### 2. T-F3 — Add [APPEAL] badge to Queue.razor (XS, 5 min, zero risk)

Single conditional span. Fulfills explicit PRD §8.7 requirement. Unblocked.

```razor
@if (item.Status == PaStatus.Appealed)
{
    <span class="badge badge-appealed text-white ms-1">APPEAL</span>
}
```

### 3. T-U1 — Show appeal deadline date in RequestDetail (XS, 10 min, zero risk)

Adds the date PRD §8.5 says must be "displayed to Specialist." Unblocked. No service changes.

### 4. T-F1 — Fix ExpirationJob audit SYSTEM attribution (S, 30–60 min, Low risk)

Medium-severity compliance fix. AuditLogs for expired events currently store no user. Two implementation options (see task description). Choosing Option B (pass explicit userId to LogAsync) requires the least interface change.

### 5. T-T1 — ExpirationJob unit tests (M, 1–2 hours, Low risk)

Depends on T-F1. ExpirationJob is the only service with zero test coverage, yet it runs on every app startup and nightly. The double-expiration guard (`WorkflowTransitionException` swallowed) is important to verify.

---

## Appendix: Complexity Legend

| Size | Effort estimate |
|---|---|
| XS | < 30 min, ≤ 10 lines changed |
| S | 30–90 min, ≤ 50 lines changed |
| M | 2–4 hours, ≤ 200 lines changed |
| L | 4–8 hours, significant files affected |
| XL | 1–3 days, new project or major architecture change |
