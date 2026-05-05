# Codebase Analysis & Execution Roadmap
## Prior Authorization Workflow Tracker

**Analysis Date:** May 4, 2026  
**Codebase State:** 139 unit tests passing, 0 build errors/warnings  
**PRD Version:** 1.0 (May 3, 2026)

---

## 1. Current State Summary

The application is a well-structured Blazor Server application with a clean layered architecture. The service layer, data model, workflow state machine, business rules (BR-001–BR-014), RBAC enforcement, audit logging, notification service, CSV export, reporting (6 tabs), and test suite are all substantially complete. The majority of functional requirements are implemented.

However, several PRD requirements are **missing, incomplete, or buggy** at the UI and service level. The most impactful gaps are:

1. **Dashboard is not role-differentiated** — same view for all 5 roles (PRD §12.1)
2. **Document upload UI is entirely absent** from RequestDetail (PRD §8.3)
3. **Withdraw button missing for UnderReview and Appealed statuses** (PRD §8.4 — workflow bug)
4. **Provider/Comment Author IDs shown as raw GUIDs** in UI instead of display names (PRD §8.3 UX)
5. **CSV export missing SubmittedByUser/ReviewerUser** columns (PRD §8.10)
6. **SQL reporting views not in any migration** (PRD §7.2)
7. **AuditLog.CorrelationId never populated** (PRD §7.1)
8. **Seed data missing ~300 AuditLog rows** (PRD §14.2)
9. **SubmitRequest uses free-text GUID input for Provider** instead of dropdown
10. **N+1 query in UserManagementService.GetAllUsersAsync** (NFR performance issue)

---

## 2. PRD Compliance Matrix

### §8 Feature Requirements

| Requirement | Status | Evidence |
|---|---|---|
| PA Request Submission form | ✅ Complete | `SubmitRequest.razor` + `PaRequestService.CreateDraftAsync` |
| Draft save with minimal fields | ✅ Complete | `DRAFT` validation path in `PaRequestService` |
| Auto-generate RequestNumber `PA-YYYY-NNNNN` | ✅ Complete | `PaRequestService.cs` derives from DB IDENTITY ID |
| DecisionDueDate = SubmittedAt + SLA (priority-adjusted) | ✅ Complete | `WorkflowService.SubmitAsync` |
| Draft → Submitted transition + audit | ✅ Complete | `WorkflowService.SubmitAsync` |
| Queue with all filters (status/priority/payer/date/procedure/reviewer) | ✅ Complete | `Queue.razor` + `PaRequestFilter` |
| Queue sortable by SubmittedAt/DecisionDueDate/PatientName/Priority | ✅ Complete | `Queue.razor` Sort() |
| Row color-coded by status | ✅ Complete | `Queue.razor` |
| Pagination 25/page | ✅ Complete | `Queue.razor: PageSize = 25` |
| "Due today" and "Overdue" badges | ✅ Complete | `Queue.razor` |
| Full request detail view | ⚠️ Partial | `RequestDetail.razor` — provider shown as raw GUID |
| Status timeline in detail view | ✅ Complete | `RequestDetail.razor` right column |
| Comment thread (role-filtered; internal hidden from Provider) | ✅ Complete | `PaRequestService.GetCommentsAsync` RBAC check |
| Comments show raw UserID not display name | ❌ Bug | `c.AuthoredByUserID` rendered directly |
| Attached documents list + upload button | ❌ Missing | No document UI anywhere; no service method |
| Withdraw available from UnderReview + Appealed | ❌ Bug | `withdrawableStatuses` omits these two statuses |
| Workflow state machine — all transitions | ✅ Complete | `WorkflowService.cs` Transitions dictionary |
| Decision rendering (Approve with auth fields) | ✅ Complete | `WorkflowService.ApproveAsync` |
| AUTH number format `AUTH-YYYYMMDD-XXXXX` | ✅ Complete | `GenerateAuthorizationNumber` |
| Decision rendering (Deny with denial reason) | ✅ Complete | `WorkflowService.DenyAsync` |
| Appeal deadline notification to Specialist | ✅ Complete | `WorkflowService.DenyAsync` notification |
| ExpirationJob nightly run | ✅ Complete | `ExpirationJob.cs` |
| ExpirationJob SYSTEM user for audit | ✅ Complete | `DbSeeder.SystemUserId` constant |
| Expiration notifications to Specialist+Provider | ✅ Complete | `WorkflowService.ExpireAsync` |
| Dashboard: expiring within 30 days | ✅ Complete | `Home.razor` expiring table |
| Appeal within deadline (BR-008) | ✅ Complete | `WorkflowService.AppealAsync` + `BusinessRuleService` |
| Disabled "Appeal window closed" button | ✅ Complete | `RequestDetail.razor` |
| Appealed requests `[APPEAL]` badge | ✅ Complete | `RequestDetail.razor` |
| Reviewer can approve/deny appeal | ✅ Complete | `WorkflowService` (Appealed→Approved/Denied) |
| Admin user management (create/deactivate/role) | ✅ Complete | `Users.razor` + `UserManagementService` |
| Admin audit log view with filters | ✅ Complete | `AuditLog.razor` (entity/action/user/date filters) |
| Admin manual expiration trigger | ✅ Complete | `IExpirationJobTrigger` + Users.razor button |
| Admin sees all PA requests | ✅ Complete | `PaRequestService` RBAC (Admin not filtered) |
| Role-differentiated dashboard (§12.1 — 5 distinct views) | ❌ Missing | `Home.razor` single view for all roles |
| CSV export Billing+Admin only | ✅ Complete | `CsvExportService` RBAC check |
| 16-column CSV per PRD spec | ❌ Bug | Missing `SubmittedByUser` + `ReviewerUser` columns |
| File named `PA_Export_YYYYMMDD_HHmmss.csv` | ✅ Complete | `CsvExportService.ExportRequestsAsync` |

### §9 Business Rules

| Rule | Status | Evidence |
|---|---|---|
| BR-001 RequiresPriorAuth check | ✅ Complete | `BusinessRuleService.ValidateForSubmissionAsync` |
| BR-002 SLA-based DecisionDueDate | ✅ Complete | `WorkflowService.SubmitAsync` |
| BR-003 ICD-10 regex validation | ✅ Complete | `PaRequestSubmitValidator` (compiled regex, ReDoS-safe) |
| BR-004 Units 1–999 | ✅ Complete | `PaRequestSubmitValidator` |
| BR-005 Duplicate active request | ✅ Complete | `BusinessRuleService` — by MRN+ProcedureCode+Plan |
| BR-006 EndDate > StartDate | ✅ Complete | `ApprovalDecisionValidator` |
| BR-007 Granted ≤ Requested | ✅ Complete | `BusinessRuleService.ValidateForApprovalAsync` |
| BR-008 Appeal within deadline (inclusive) | ✅ Complete | `BusinessRuleService.ValidateAppealEligibilityAsync` |
| BR-009 Withdraw prohibited statuses | ✅ Complete | `BusinessRuleService.ValidateCanWithdraw` |
| BR-010 Justification ≥ 50 chars | ✅ Complete | `PaRequestSubmitValidator` |
| BR-011 AuthStartDate ≥ today | ✅ Complete | `ApprovalDecisionValidator` |
| BR-012 GrantedUnits ≥ 1 | ✅ Complete | `BusinessRuleService.ValidateForApprovalAsync` |
| BR-013 Resubmit requires new content | ✅ Complete | `BusinessRuleService.ValidateResubmissionAsync` |
| BR-014 Hard delete only by creator or Admin | ✅ Complete | `BusinessRuleService.ValidateCanHardDelete` |

### §10 RBAC

| Permission | Status | Evidence |
|---|---|---|
| Provider sees own patients only | ✅ Complete | `PaRequestService.GetQueueAsync` + `GetByIdAsync` |
| Internal comments hidden from Provider | ✅ Complete | `PaRequestService.GetCommentsAsync` RBAC check |
| Service-layer role re-validation | ✅ Complete | All WorkflowService methods call `RequireActive` + `RequireRole` |
| Admin override workflow with justification | ❌ Missing | No `AdminOverrideAsync`; audit action `"AdminOverride"` never logged |

### §11 Audit Logging

| Event | Status | Evidence |
|---|---|---|
| Request created | ✅ Complete | `PaRequestService.CreateDraftAsync` |
| Status transition | ✅ Complete | `WorkflowService.ApplyTransitionAsync` |
| Decision rendered | ✅ Complete | `WorkflowService.ApproveAsync/DenyAsync` |
| Comment added | ✅ Complete | `PaRequestService.AddCommentAsync` |
| Document uploaded | ❌ Missing | No upload service exists |
| Appeal filed | ✅ Complete | `WorkflowService.AppealAsync` |
| Request withdrawn | ✅ Complete | `WorkflowService.WithdrawAsync` |
| Request expired | ✅ Complete | `WorkflowService.ExpireAsync` |
| User created/deactivated | ✅ Complete | `UserManagementService` |
| Role changed | ✅ Complete | `UserManagementService.ChangeRoleAsync` |
| Admin override | ❌ Missing | `AdminOverrideAsync` not implemented |
| User login | ✅ Complete | `Login.cshtml.cs` |
| Failed login | ✅ Complete | `Login.cshtml.cs` |
| CSV export downloaded | ✅ Complete | `CsvExportService.ExportRequestsAsync` |
| `AuditLog.CorrelationId` populated | ❌ Missing | `AuditService.LogAsync` never sets the field; middleware only enriches Serilog |

### §12 Reporting

| Report | Status | Evidence |
|---|---|---|
| Request Volume by Month | ✅ Complete | `Reports.razor` "volume" tab |
| Denial Analysis | ✅ Complete | `Reports.razor` "denials" tab |
| Approval Rate | ⚠️ Partial | No dedicated tab; partially addressed by Status Summary tab |
| Expiring Authorizations | ✅ Complete | `Reports.razor` "expiring" tab |
| Turnaround Time | ✅ Complete | `Reports.razor` "turnaround" tab |
| Provider Submission Volume | ✅ Complete | `Reports.razor` "providervolume" tab |
| SQL views in migrations | ❌ Missing | Only `InitialCreate` migration; no `AddReportingViews` |

### §13 Testing

| Layer | PRD Target | Tests | Gap |
|---|---|---|---|
| WorkflowService | ≥ 90% | 26 tests — all transitions except concurrent | Missing concurrent decision test |
| PaRequestService | ≥ 80% | 13 tests — core flows covered | UpdateDraft, AddComment RBAC not tested |
| BusinessRuleService | 100% | 25 tests — all BR-001–BR-014 | ✅ Complete |
| AuditService | ≥ 70% | 4 tests — basic coverage | CorrelationId never set (bug blocks test) |
| ReportingService | ≥ 60% | 15 tests — status/denials/volume/expiring/dashboard | GetTurnaroundTimeAsync, GetProviderVolumeAsync uncovered |

### §14 Seed Data

| Entity | PRD Target | Actual | Gap |
|---|---|---|---|
| Insurance Plans | 8 | 8 | ✅ |
| Procedure Codes | 25 | ≥25 | ✅ |
| Denial Reasons | 12 | 12 | ✅ |
| PA Requests | 50 | 50 | ✅ |
| AuditLog rows | ~300 | **0** | ❌ Critical for demo |
| Demo accounts | 5 min | 8 (2 Specialists, 3 Providers, 1 Billing, 1 Reviewer, 1 Admin) | ✅ Exceeds PRD |

### §15 NFRs

| NFR | Status | Notes |
|---|---|---|
| NFR-001 Queue load ≤ 2s | ✅ Likely met | AsNoTracking + indexes on Status/SubmittedAt |
| NFR-002 AsNoTracking on reads | ✅ Complete | All service read queries use AsNoTracking |
| NFR-003 PBKDF2 password hashing | ✅ Complete | ASP.NET Core Identity defaults |
| NFR-004 No raw SQL concatenation | ✅ Complete | EF Core parameterized queries throughout |
| NFR-005 Anti-forgery on state-changing forms | ✅ Complete | Blazor circuit-level protection + Razor Pages AntiForgery |
| NFR-006 Unauthorized access logged | ✅ Complete | `ExceptionHandlingMiddleware` + `AuthorizationException` |
| NFR-007 Graceful error pages | ✅ Complete | `Error.razor` + `ExceptionHandlingMiddleware` |
| NFR-008 Bootstrap semantic HTML with labels | ✅ Complete | All forms use `<label for=...>` |
| NFR-009 No business logic in Razor | ✅ Complete | All BR logic in service layer |
| NFR-010 EF Core migrations | ⚠️ Partial | SQL views not in any migration |
| NFR-011 SignalR reconnect indicator | ✅ Complete | `App.razor` reconnect UI |
| N+1 in UserManagementService | ❌ Bug | `foreach GetRolesAsync` = 1 DB query per user |

---

## 3. Bug List

| ID | Severity | Description | File / Location | PRD Ref |
|---|---|---|---|---|
| BUG-A | **High** | Withdraw button not shown for `UnderReview` or `Appealed` — `withdrawableStatuses` in RequestDetail only includes `Draft`, `Submitted`, `PendingInfo` | `RequestDetail.razor` ~line 479 | §8.4 |
| BUG-B | **High** | Raw GUID displayed for Provider field in request detail instead of user `FullName` | `RequestDetail.razor` ~line 88 (`_request.ProviderID`) | §8.3 |
| BUG-C | **High** | Raw GUID displayed for comment author instead of user `FullName` | `RequestDetail.razor` ~line 290 (`c.AuthoredByUserID`) | §8.3 |
| BUG-D | **High** | CSV export missing `SubmittedByUser` and `ReviewerUser` display-name columns | `CsvExportService.cs` WriteHeader/WriteRow | §8.10 |
| BUG-E | **Medium** | `Draft` is in `withdrawableStatuses` for the UI button, but `BusinessRuleService.ValidateCanWithdraw` (BR-009) rejects Draft — results in a service exception instead of a clean disabled-button UX | `RequestDetail.razor` ~line 479 | §8.4 |
| BUG-F | **Medium** | `AuditLog.CorrelationId` field exists in schema and model but `AuditService.LogAsync` never sets it — correlation with Serilog logs is broken | `AuditService.cs` CreateEntry block | §7.1 |
| BUG-G | **Medium** | SubmitRequest Provider field is a plain `<InputText>` expecting a raw GUID; no user can realistically type their Provider's ID | `SubmitRequest.razor` ProviderID field | §8.1 UX |
| BUG-H | **Low** | N+1 query: `UserManagementService.GetAllUsersAsync` calls `_userManager.GetRolesAsync(user)` in a foreach loop — 1 extra Identity DB query per user | `UserManagementService.cs` GetAllUsersAsync | NFR-001 |

---

## 4. Missing Functionality

| ID | Feature | PRD Ref | Complexity |
|---|---|---|---|
| MISS-01 | Role-differentiated dashboard (5 distinct views: Specialist, Billing Manager, Provider, Reviewer, Admin) | §12.1 | High |
| MISS-02 | Document list + upload UI in RequestDetail; `AddDocumentAsync` service method | §8.3, §11 | High |
| MISS-03 | Admin workflow override: `AdminOverrideAsync(requestId, newStatus, justification)` + UI button | §10.1 | Medium |
| MISS-04 | SQL reporting views in an EF Core migration (`AddReportingViews`) | §7.2 | Medium |
| MISS-05 | Seed ~300 AuditLog rows in `DbSeeder` | §14.2 | Low |
| MISS-06 | Approval Rate dedicated report tab | §12.2 | Low |

---

## 5. Cleanup / Refactor Recommendations

| ID | Description | Risk |
|---|---|---|
| REF-01 | **Fix N+1 in `UserManagementService.GetAllUsersAsync`** — batch role lookup via single join on `AspNetUserRoles`+`AspNetRoles` instead of one `GetRolesAsync` call per user | Low |
| REF-02 | **`WorkflowService.Snapshot` helper** uses string concatenation to build JSON — safe now (all scalar values), but should migrate to `JsonSerializer.Serialize` for robustness | Low |
| REF-03 | **Provider GUID on `PaRequest`** — `ProviderID` is stored as the raw Identity user ID string. Consider a FK to `ApplicationUser` with a Navigation property so EF can join without extra queries | Low |
| REF-04 | **`ReportingService.GetProviderVolumeAsync`** loads rows client-side before grouping; acceptable for InMemory + small datasets but add a comment explaining why (test compatibility) | Low |

---

## 6. Missing Tests

| ID | Test Case | Status | Notes |
|---|---|---|---|
| TEST-01 | `WorkflowService_ConcurrentDecision_ShouldThrowDbUpdateConcurrencyException` | ❌ Missing | Requires Testcontainers SQL Server (EF InMemory ignores RowVersion) |
| TEST-02 | `ReportingService.GetTurnaroundTimeAsync` — 2–3 cases | ❌ Missing | Method exists since prior session, zero tests |
| TEST-03 | `ReportingService.GetProviderVolumeAsync` — 2–3 cases | ❌ Missing | Method exists since prior session, zero tests |
| TEST-04 | `PaRequestService.UpdateDraftAsync` — field update + non-owner RBAC rejection | ❌ Missing | |
| TEST-05 | `PaRequestService.AddCommentAsync` — Provider cannot add internal comment | ❌ Missing | |
| TEST-06 | `AuditService` — CorrelationId set when IHttpContextAccessor provides value | ❌ Blocked | Blocked on BUG-F fix |
| TEST-07 | `WorkflowService.ResubmitAsync` — happy path Denied → Submitted transition | ❌ Missing | |
| TEST-08 | `WorkflowService.ResubmitAsync` — BR-013 rejection (no new content) | ❌ Missing | |

---

## 7. Task DAG

**Complexity:** S = <2h, M = 2–4h, L = 4–8h, XL = >8h  
**Risk:** Low = isolated change; Med = shared code; High = schema/migration  
**P:** Parallel-safe (can run in separate worktree)

---

### Phase A — Bug Fixes (All Parallel-Safe, No Schema Changes)

| Task ID | Title | Deps | Complexity | Risk | Parallel |
|---|---|---|---|---|---|
| **T-A1** | Fix `withdrawableStatuses`: replace `{Draft, Submitted, PendingInfo}` with `{Submitted, PendingInfo, UnderReview, Appealed}` | — | S | Low | ✅ |
| **T-A2** | Load user `FullName` map in `RequestDetail.OnInitializedAsync`; display provider name instead of GUID | — | S | Low | ✅ |
| **T-A3** | Display comment author `FullName` using map from T-A2 (implement together) | T-A2 | S | Low | ✅ |
| **T-A4** | Fix N+1 in `UserManagementService.GetAllUsersAsync` — batch role lookup | — | S | Low | ✅ |

**Implementation notes:**
- **T-A1 + BUG-E:** Remove `PaStatus.Draft` from the array simultaneously. `BusinessRuleService.ValidateCanWithdraw` already enforces allowed statuses at service level; the UI button is cosmetic gating.
- **T-A2/A3:** Collect unique user IDs from `_request.ProviderID`, `_request.SubmittedByUserID`, `_request.ReviewerUserID`, and all `c.AuthoredByUserID` in a `HashSet<string>`. Batch-query `db.Users.Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.FullName })`. Store as `Dictionary<string, string> _userNames`. Use `_userNames.GetValueOrDefault(id, "—")` in markup.
- **T-A4:** Replace the `foreach GetRolesAsync` loop with: `db.UserRoles.Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name }).GroupBy(x => x.UserId).ToDictionary(...)`.

---

### Phase B — UI Completions

| Task ID | Title | Deps | Complexity | Risk | Parallel |
|---|---|---|---|---|---|
| **T-B1** | Provider picker dropdown in `SubmitRequest.razor` | — | M | Low | ✅ |
| **T-B2** | Add Approval Rate tab to `Reports.razor` + `GetApprovalRateAsync` in service | — | M | Low | ✅ |
| **T-B3** | Role-differentiated dashboard — service layer (new methods per role) | — | L | Med | ✅ |
| **T-B4** | Role-differentiated dashboard — Specialist UI panel in `Home.razor` | T-B3 | M | Med | — |
| **T-B5** | Role-differentiated dashboard — Billing Manager UI panel | T-B3 | M | Med | — |
| **T-B6** | Role-differentiated dashboard — Provider UI panel | T-B3 | M | Med | — |
| **T-B7** | Role-differentiated dashboard — Payer Reviewer UI panel | T-B3 | M | Med | — |
| **T-B8** | Role-differentiated dashboard — Admin combined view | T-B4–T-B7 | M | Med | — |

**Implementation notes:**
- **T-B3:** Add to `IReportingService`: `GetSpecialistDashboardAsync(userId)`, `GetBillingDashboardAsync()`, `GetProviderDashboardAsync(userId)`, `GetReviewerDashboardAsync(userId)`. Return typed records (e.g., `SpecialistDashboard { int MyOpenRequests, int PendingInfoRequiringResponse, ... }`). Keep existing `GetDashboardSummaryAsync` as Admin fallback.
- **T-B4–T-B8:** In `Home.razor`, use `<AuthorizeView Roles="...">` blocks to switch rendered panels. Each panel is a separate `RenderFragment` or partial component.

---

### Phase C — Missing Features

| Task ID | Title | Deps | Complexity | Risk | Parallel |
|---|---|---|---|---|---|
| **T-C1** | Fix CSV export: add `SubmittedByUser` + `ReviewerUser` columns | — | S | Low | ✅ |
| **T-C2** | Populate `AuditLog.CorrelationId` via `IHttpContextAccessor` in `AuditService` | — | S | Med | ✅ |
| **T-C3** | Seed ~300 AuditLog rows in `DbSeeder.SeedAuditLogsAsync` | — | M | Low | ✅ |
| **T-C4** | Document upload UI + `AddDocumentAsync` service method | — | L | Med | ✅ |
| **T-C5** | Admin workflow override: `AdminOverrideAsync` + UI button in `RequestDetail` | — | M | Med | ✅ |

**Implementation notes:**
- **T-C1:** In `CsvExportService`, load a `Dictionary<string, string>` of userId→FullName for all `SubmittedByUserID` and `ReviewerUserID` values in the result set. Add two new columns (`SubmittedByUser`, `ReviewerUser`) to `WriteHeader` and `WriteRow`. Update the 16-column count comment.
- **T-C2:** In `CorrelationIdMiddleware.InvokeAsync`, before calling `_next`, add `context.Items["CorrelationId"] = correlationId`. In `AuditService`, inject `IHttpContextAccessor` and set `CorrelationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"] as string`.
- **T-C3:** `SeedAuditLogsAsync()` in `DbSeeder` should create AuditLog entries for: each PaRequest creation (action=`Create`, entity=`PaRequest`), each PaStatusHistory transition (action matches transition name), plus ~50 login entries for demo user accounts. Aim for ~300 total rows spread across the seeded date range.
- **T-C4:** Add to `IPaRequestService`: `Task<PaDocument> AddDocumentAsync(int requestId, string fileName, string contentType, long fileSizeBytes, string documentType, CancellationToken ct)` and `Task<IReadOnlyList<PaDocument>> GetDocumentsAsync(int requestId, CancellationToken ct)`. Implement RBAC (Specialist/Provider/Reviewer/Admin can upload). Add a "Documents" card to `RequestDetail.razor` below comments, with a list of existing documents and an upload form (simulated — store metadata only, no actual file bytes per PRD §18 scope boundary).
- **T-C5:** Add `Task<PaRequest> AdminOverrideAsync(int requestId, PaStatus newStatus, string justification, CancellationToken ct)` to `IWorkflowService`. Bypass normal Transitions; validate caller role = Admin; log audit action `"AdminOverride"` with `OldValues = current status`, `NewValues = new status`, `Justification = justification`. Add Admin-only "Override Status" section in `RequestDetail.razor` with a `<select>` for status and required justification textarea.

---

### Phase D — Test Coverage

| Task ID | Title | Deps | Complexity | Risk | Parallel |
|---|---|---|---|---|---|
| **T-D1** | Add `ReportingServiceTests` for `GetTurnaroundTimeAsync` (3 cases) | — | S | Low | ✅ |
| **T-D2** | Add `ReportingServiceTests` for `GetProviderVolumeAsync` (3 cases) | — | S | Low | ✅ |
| **T-D3** | Add `WorkflowServiceTests` for `ResubmitAsync` happy path + BR-013 rejection | — | M | Low | ✅ |
| **T-D4** | Add `PaRequestServiceTests` for `UpdateDraftAsync` + `AddCommentAsync` RBAC | — | M | Low | ✅ |
| **T-D5** | Add `AuditServiceTests` for CorrelationId population | T-C2 | S | Low | — |
| **T-D6** | Create `Tests.Integration` project with Testcontainers — concurrent decision test | — | XL | Med | ✅ |

---

### Phase E — Infrastructure

| Task ID | Title | Deps | Complexity | Risk | Parallel |
|---|---|---|---|---|---|
| **T-E1** | Add `AddReportingViews` EF Core migration with 4 SQL views from PRD §7.2 | — | M | High | ✅ |

**T-E1 note:** The 4 views are: `vw_PaRequestSummary`, `vw_DenialsByReason`, `vw_RequestVolumeByMonth`, `vw_ExpiringAuthorizations`. These supplement the existing LINQ queries (no application code needs to change). Add `migrationBuilder.Sql("CREATE VIEW ...")` for Up and `migrationBuilder.Sql("DROP VIEW IF EXISTS ...")` for Down.

---

## 8. Directed Acyclic Graph

```
PHASE A (all can start immediately in parallel):
  T-A1 (withdraw statuses fix)
  T-A2 + T-A3 (user display names — T-A3 depends on T-A2)
  T-A4 (N+1 fix)

PHASE B:
  T-B1 (provider picker) — independent
  T-B2 (approval rate tab) — independent
  T-B3 (dashboard service layer) ──┐
  T-B4 (Specialist panel) ─────────┤ depend on T-B3
  T-B5 (Billing panel) ────────────┤
  T-B6 (Provider panel) ───────────┤
  T-B7 (Reviewer panel) ───────────┤
         └────────────────────────► T-B8 (Admin combined — depends on T-B4–T-B7)

PHASE C (all independent of each other):
  T-C1 (CSV fix)
  T-C2 (CorrelationId fix) ──► T-D5 (CorrelationId test)
  T-C3 (AuditLog seeds)
  T-C4 (document upload)
  T-C5 (admin override)

PHASE D (all independent except T-D5):
  T-D1 (turnaround tests)
  T-D2 (provider volume tests)
  T-D3 (resubmit tests)
  T-D4 (request service tests)
  T-D5 (correlationId test) — depends on T-C2
  T-D6 (integration project) — independent

PHASE E:
  T-E1 (SQL views migration) — independent
```

---

## 9. Parallel Worktree Plan

| Worktree | Branch | Tasks | Key Files Changed |
|---|---|---|---|
| **WT-1** | `fix/withdraw-and-names` | T-A1 + T-A2 + T-A3 | `RequestDetail.razor` |
| **WT-2** | `fix/usermanagement-n1` | T-A4 | `UserManagementService.cs`, `UserManagementServiceTests.cs` |
| **WT-3** | `fix/csv-columns` | T-C1 | `CsvExportService.cs`, `CsvExportServiceTests.cs` |
| **WT-4** | `fix/correlationid` | T-C2, T-D5 | `CorrelationIdMiddleware.cs`, `AuditService.cs`, `AuditServiceTests.cs` |
| **WT-5** | `feat/provider-picker` | T-B1 | `SubmitRequest.razor` |
| **WT-6** | `feat/approval-rate` | T-B2 | `Reports.razor`, `IReportingService.cs`, `ReportingService.cs`, `ReportingModels.cs` |
| **WT-7** | `feat/role-dashboard` | T-B3–T-B8 | `Home.razor`, `IReportingService.cs`, `ReportingService.cs`, `ReportingModels.cs` |
| **WT-8** | `feat/documents` | T-C4 | `IPaRequestService.cs`, `PaRequestService.cs`, `RequestDetail.razor` |
| **WT-9** | `feat/admin-override` | T-C5 | `IWorkflowService.cs`, `WorkflowService.cs`, `RequestDetail.razor` |
| **WT-10** | `feat/seed-auditlogs` | T-C3 | `DbSeeder.cs` |
| **WT-11** | `test/reporting-gaps` | T-D1 + T-D2 | `ReportingServiceTests.cs` |
| **WT-12** | `test/workflow-gaps` | T-D3 | `WorkflowServiceTests.cs` |
| **WT-13** | `test/requestservice-gaps` | T-D4 | `PaRequestServiceTests.cs` |
| **WT-14** | `test/integration` | T-D6 | New `Tests.Integration/` project |
| **WT-15** | `infra/reporting-views` | T-E1 | New migration file |

**Merge conflict warnings:**
- WT-6 and WT-7 both touch `IReportingService.cs` and `ReportingService.cs` — merge WT-6 before WT-7, or keep methods in separate regions.
- WT-8 and WT-9 both touch `RequestDetail.razor` — merge sequentially (documents first, then override).

---

## 10. Recommended First 5 Tasks

### 1. T-A1 — Fix Withdraw Button Statuses (BUG-A + BUG-E)
**Impact:** Functional workflow defect — users cannot withdraw UnderReview/Appealed requests despite PRD §8.4 requiring it. Single-expression change. Zero risk.  
**Files:** `RequestDetail.razor` ~line 479.  
**Validation:** Create an UnderReview request in the UI → confirm Withdraw button appears → confirm service accepts it.

### 2. T-A2 + T-A3 — Fix Provider and Comment Author Display Names (BUG-B + BUG-C)
**Impact:** Every user seeing any request detail sees raw GUIDs for the treating provider and all comment authors. High-visibility UX defect.  
**Files:** `RequestDetail.razor` only.  
**Validation:** Open any request detail as any role → confirm provider name appears → confirm comment authors show names.

### 3. T-C1 — Fix CSV Export Columns (BUG-D)
**Impact:** The primary export artifact for Billing Managers is missing two required user columns. PRD §8.10 compliance gap.  
**Files:** `CsvExportService.cs`.  
**Validation:** Export CSV → confirm `SubmittedByUser` and `ReviewerUser` header columns present → confirm values are full names, not GUIDs.

### 4. T-D1 + T-D2 — ReportingService Tests for GetTurnaroundTime/GetProviderVolume
**Impact:** Two service methods added in the prior session have zero test coverage. These are the lowest-effort tests (follow existing `ReportingServiceTests` patterns).  
**Files:** `ReportingServiceTests.cs`.  
**Validation:** `dotnet test` passes; new test count visible in output.

### 5. T-C3 — Seed AuditLog Rows
**Impact:** The Admin AuditLog viewer is empty on every fresh database. PRD §14.2 requires ~300 rows. Critical for demo viability and for showing the audit trail feature.  
**Files:** `DbSeeder.cs`.  
**Validation:** Run app from clean DB → navigate to `/admin/auditlog` → confirm rows appear with realistic distribution.

---

## 11. Open Questions

| Question | Context |
|---|---|
| Does the dev/CI environment have Docker available? | Required for T-D6 (Testcontainers concurrent-decision test) |
| Should Admin dashboard be a union of all 4 role panels, or a distinct operational view? | Affects T-B8 scope |
| Are SQL reporting views strictly required for demo, or is LINQ sufficient? | Affects T-E1 priority |
| For CSV: should `SubmittedByUser`/`ReviewerUser` replace `PatientDob`/`DiagnosisCode`, or should all be included (18 columns)? | PRD §8.10 states 15+1=16 columns but lists 15 by name |
| For Admin Override (T-C5): bypass all business rules, or only bypass state machine transitions? | Affects `AdminOverrideAsync` design |
