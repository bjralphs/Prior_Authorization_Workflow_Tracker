# PRD Compliance Analysis & Execution Roadmap v4

**Date:** 2026-05-05  
**State captured:** 197 unit tests + 8 integration tests (205 total), 0 failures, 0 build warnings.

---

## 1. Current State Summary

The application is feature-complete for a first shippable version. All core workflow transitions, business rules, RBAC, audit logging, reporting, notifications, CSV export, and the integration test suite are implemented and green. The remaining open items are refinements, two confirmed bugs, four missing database indexes, three missing unit tests, and documentation/UX polish.

### Test ledger
| Project | Tests | Status |
|---|---|---|
| `Tests.Unit` | 197 | ✅ All passing |
| `Tests.Integration` | 8 | ✅ All passing (Testcontainers SQL Server) |

### Build
```
dotnet build → 0 errors, 0 warnings
dotnet test  → 205 passed, 0 failed
```

---

## 2. PRD Compliance Matrix

| Section | Requirement | Status | Evidence / Gap |
|---|---|---|---|
| §8.1 FR-001 | PA request submission, Draft save, RequestNumber format | ✅ Complete | `PaRequestService.CreateDraftAsync`, `SubmitAsync`; `PA-YYYY-NNNNN` format; `PaRequestSubmitValidator` |
| §8.1 FR-001 | DecisionDueDate from plan SLA + priority | ✅ Complete | `WorkflowService.SubmitAsync` lines 88-98 |
| §8.2 FR-002 | Queue with filter/sort/pagination | ✅ Complete | `Queue.razor`, `PaRequestService.GetQueueAsync`; 25/page default, 6 filters, 4 sort columns |
| §8.2 FR-002 | "Due today" and "Overdue" badges | ✅ Complete | `Queue.razor` — overdue badge rendered inline |
| §8.3 FR-003 | Request detail: full fields, status timeline, comments, documents, action buttons | ✅ Complete | `RequestDetail.razor` — all sections present |
| §8.3 FR-003 | Internal comments hidden from Provider | ✅ Complete | `PaRequestService.GetCommentsAsync` — DB-level filter for Provider role |
| §8.4 FR-004 | All 16 valid state transitions | ✅ Complete | `WorkflowService` transition table; all 16 pairs incl. 3 Appealed-origin paths |
| §8.4 FR-004 | Withdrawn from Submitted/PendingInfo/UnderReview/Appealed | ✅ Complete | UI shows Withdraw button for correct statuses; service enforces via `ValidateCanWithdraw` |
| §8.4 FR-004 | Draft → delete (not withdrawn) | ✅ Complete | `DeleteDraftAsync` (hard delete); `BR-009` rejects Draft in `ValidateCanWithdraw` |
| §8.5 FR-005 | Approve: units, dates, AuthorizationNumber | ✅ Complete | `WorkflowService.ApproveAsync`; CSPRNG format `AUTH-YYYYMMDD-[0-9A-F]{6}` |
| §8.5 FR-005 | Deny: DenialReasonId, notes, appeal deadline display | ✅ Complete | `DenyAsync`; appeal deadline shown in `RequestDetail.razor` when `IsAppealable=true` |
| §8.6 FR-006 | ExpirationJob daily + on-startup | ✅ Complete | `ExpirationJob.ExecuteAsync` with `PeriodicTimer` + immediate startup run |
| §8.6 FR-006 | SYSTEM user for expiration audit | ⚠️ Partial | `PaStatusHistory` uses `SystemUserId` ✅ but `AuditLog.UserID` still reads from `ICurrentUserService` (anonymous in background) → writes empty string. **BUG-001** |
| §8.6 FR-006 | In-app notification on expiration (Specialist + Provider) | ✅ Complete | `WorkflowService.ExpireAsync` sends `ExpirationAlert` to both |
| §8.7 FR-007 | Appeal: deadline check, Appealed badge, reviewer queue | ✅ Complete | `WorkflowService.AppealAsync`; `[APPEAL]` badge in Queue and Detail |
| §8.7 FR-007 | Appealed → UnderReview/Approved/Denied (§8.7 fix §3.27) | ✅ Complete | `BeginReviewAsync`/`ApproveAsync`/`DenyAsync` accept Appealed origin |
| §8.8 FR-008 | User management: create, deactivate, role-change | ✅ Complete | `Users.razor`, `UserManagementService` |
| §8.8 FR-008 | Audit log viewer with filters | ✅ Complete | `AuditLog.razor` — entity/action/user/date filters |
| §8.8 FR-008 | Manual expiration trigger (Admin UI) | ✅ Complete | "Run Expiration Check" button in `Users.razor` → `IExpirationJobTrigger.TriggerAsync` |
| §8.9 FR-009 | Role-specific dashboards (5 roles) | ✅ Complete | `Home.razor` — Specialist, Provider, Billing, Reviewer, Admin panels |
| §8.10 FR-010 | CSV export (Billing + Admin), 18 columns, filename format | ✅ Complete | `CsvExportService`; 18 columns; `PA_Export_YYYYMMDD_HHmmss.csv` |
| §9 BR-001 | RequiresPriorAuth guard | ✅ Complete | `BusinessRuleService.ValidateForSubmissionAsync` |
| §9 BR-002 | DecisionDueDate from plan SLA by priority | ✅ Complete | `WorkflowService.SubmitAsync` |
| §9 BR-003 | ICD-10 regex with ReDoS timeout | ✅ Complete | `PaRequestSubmitValidator`; 1-second timeout |
| §9 BR-004 | Units 1–999 | ✅ Complete | `PaRequestSubmitValidator` |
| §9 BR-005 | Duplicate active request (same patient+proc+plan) | ✅ Complete | `BusinessRuleService.ValidateForSubmissionAsync` |
| §9 BR-006 | EndDate > StartDate | ✅ Complete | `ApprovalDecisionValidator` |
| §9 BR-007 | Granted ≤ Requested | ✅ Complete | `BusinessRuleService.ValidateForApprovalAsync` |
| §9 BR-008 | Appeal within deadline | ✅ Complete | `BusinessRuleService.ValidateAppealEligibilityAsync` |
| §9 BR-009 | Cannot withdraw Approved/Expired/Denied/Draft | ✅ Complete | `ValidateCanWithdraw` |
| §9 BR-010 | Clinical justification ≥ 50 chars | ✅ Complete | `PaRequestSubmitValidator` |
| §9 BR-011 | Auth start date ≥ today | ✅ Complete | `ApprovalDecisionValidator` |
| §9 BR-012 | Granted ≥ 1 | ✅ Complete | `ApprovalDecisionValidator` |
| §9 BR-013 | Resubmission requires new comment or document | ✅ Complete | `BusinessRuleService.ValidateResubmissionAsync` |
| §9 BR-014 | Hard delete Draft only by creator or Admin | ✅ Complete | `BusinessRuleService.ValidateCanHardDelete` |
| §10 RBAC | Permission matrix | ✅ Complete | Service-layer guards + `<AuthorizeView>` in all components |
| §11 Audit | All 13 logged event types | ⚠️ Partial | Login ✅, FailedLogin ✅, Created ✅, StatusChanged ✅, Decision ✅, Comment ✅, Document ✅, Appeal ✅, Withdrawn ✅, Expired ⚠️(BUG-001), User CRUD ✅, RoleChanged ✅, AdminOverride ✅, CSV Export ✅ |
| §12.1 Dashboard | 5 role-specific panels with all KPIs | ✅ Complete | `Home.razor`; `ReportingService` 5 dashboard methods |
| §12.1 Dashboard | Provider view: PendingInfo requiring clinical input | ⚠️ Gap | `ProviderDashboard` shows `MyPendingInfo` count but no direct link to filtered `/requests?status=PendingInfo&provider={id}` — **GAP-07** |
| §12.2 Reports | 6 report tabs (status, denials, volume, expiring, turnaround, provider volume) | ✅ Complete | `Reports.razor` — 7 tabs including Approval Rate |
| §13 Testing | `Tests.Unit` + `Tests.Integration` | ✅ Complete | 205 tests, 0 failures |
| §13.3 Named scenarios | All 15 PRD-named scenarios | ⚠️ Partial | `NotificationService_ExpirationAlert_ShouldCreateNotificationForSpecialistAndProvider` lives in `WorkflowServiceTests` (ExpireAsync_SendsNotificationToBothSpecialistAndProvider) not `NotificationServiceTests`; `PaRequestService_GenerateRequestNumber_ShouldFollowFormat` is covered but under a different name; BR-013 resubmission path has **no unit test** — **GAP-08** |
| §14 Seed data | 8 demo users (5 in §14.1 + extras), 3+ providers, 2+ specialists | ✅ Complete | `DbSeeder` — 8 accounts, 3 providers, 2 specialists |
| §14.2 Seed volume | 50 requests, ~150 history rows, ~80 comments, ~300 audit rows | Needs verification | Seeder logic is present; exact counts not verified — **NEEDS-VER-01** |
| §14.3 Status distribution | Draft:3 Submitted:6 PendingInfo:5 … | Needs verification | **NEEDS-VER-02** |
| §15 NFR-001 | Queue ≤ 2s with 50 records | ✅ Likely | Server-side pagination + indexes; not load-tested — Needs verification |
| §15 NFR-002 | AsNoTracking() on all reads | ✅ Complete | Verified across all service query methods |
| §15 NFR-003 | PBKDF2-SHA256 passwords | ✅ Complete | ASP.NET Core Identity default |
| §15 NFR-004 | No raw SQL string concatenation | ✅ Complete | All raw SQL uses parameterized `SqlQueryRaw<T>` |
| §15 NFR-005 | Anti-forgery tokens on state-changing forms | ⚠️ Partial | `app.UseAntiforgery()` registered; Blazor Server components use SignalR so CSRF is architecturally moot, but Login Razor Page uses `@Html.AntiForgeryToken()` implicitly — no explicit verification — **NEEDS-VER-03** |
| §15 NFR-006 | Unauthorized access attempts logged to Serilog | ✅ Complete | `ExceptionHandlingMiddleware` logs `AuthorizationException` |
| §15 NFR-007 | Graceful error pages, no stack traces | ✅ Complete | `Error.razor`, `ExceptionHandlingMiddleware` |
| §15 NFR-008 | Accessibility: labels on all form fields | ⚠️ Partial | Most fields have labels; inline confirmation modal + some icon-only buttons lack `aria-label` — **GAP-09** |
| §15 NFR-009 | No business logic in Razor components | ✅ Complete | All logic delegated to service layer |
| §15 NFR-010 | EF Core migrations for all schema changes | ✅ Complete | 3 migrations; no manual DDL in startup |
| §15 NFR-011 | SignalR reconnect indicator | ✅ Complete | `App.razor` + `app.css` reconnect overlay |
| §16.1 Layout | NavMenu, breadcrumbs, toasts, confirmation modals | ✅ Complete | `NavMenu.razor`, `Breadcrumb.razor`, `Toast.razor`, `ConfirmationModal.razor` |
| §16.2 Status colors | All 9 statuses have correct Bootstrap colors | ✅ Complete | `StatusBadge.razor`; `badge-appealed` custom class |
| §16.3 Routes | All 9 routes implemented | ✅ Complete | All pages mapped |
| §17.1 Exception hierarchy | 4 exception types | ✅ Complete | `PaException` → `WorkflowTransitionException`, `BusinessRuleViolationException`, `AuthorizationException`, `EntityNotFoundException` |
| §17.2 Serilog | Console + rolling file, structured enrichment | ✅ Complete | `Program.cs` Serilog bootstrap |
| §17.3 Correlation IDs | Support reference ID in error messages | ✅ Complete | `CorrelationIdMiddleware`, `AuditLog.CorrelationId` |

---

## 3. Bug & Blind Spot Register

### Confirmed Bugs (actionable now)

| ID | Severity | Description | Location | PRD Ref |
|---|---|---|---|---|
| **BUG-001** | Medium | `AuditLog.UserID` and `UserName` are empty string for expiration events. `ExpirationJob` resolves `WorkflowService` in a background scope with no HTTP/Blazor context; `ICurrentUserService` → `CurrentUserService` reads from `AuthenticationStateProvider` → returns anonymous `ClaimsPrincipal`. `PaStatusHistory.ChangedByUserID` correctly uses `DbSeeder.SystemUserId` (hardcoded in `ExpireAsync`), but `AuditService.LogAsync` still calls `_currentUser.UserId` → `""`. | `WorkflowService.cs` ~line 440 (ExpireAsync audit call), `AuditService.cs` | §11 |
| **BUG-002** | Low | `RequestDetail.razor` uses `DateTime.UtcNow` directly (not `IDateTimeProvider`) in three inline Razor expressions: overdue decision due date highlight, appeal deadline calculation, and denial status check. This cannot be unit-tested deterministically and contradicts NFR-009. | `RequestDetail.razor` lines ~230, ~260, ~338 | §15 NFR-009 |

### Missing Indexes (schema gap)

| ID | Severity | Description | Migration Required |
|---|---|---|---|
| **IDX-001** | Medium | `IX_PaRequests_ReviewerUserID` is absent. The queue reviewer filter (`WHERE ReviewerUserID = @id`) and reviewer dashboard (`WHERE ReviewerUserID = @id AND Status IN (...)`) both scan the table. PRD §7.3 does not explicitly list it but it is needed for NFR-001 at scale. | Yes — `AddReviewerUserIDIndex` |
| **IDX-002** | Low | `IX_PaRequests_ProviderID` is absent. Provider dashboard queries filter by `ProviderID`; at 500+ records this will table-scan. PRD §7.3 does not list it explicitly. | Yes — `AddProviderIDIndex` |

### Missing Tests

| ID | Description | PRD Ref |
|---|---|---|
| **TEST-001** | BR-013 resubmission path: `PaRequestService.ResubmitAsync` → `ValidateResubmissionAsync` — no unit test exists. PRD §13.3 names `WorkflowService_Withdraw_*` and `BR-013` as key scenarios; the latter is untested at service level. | §13.3 |
| **TEST-002** | `PaRequestService_GenerateRequestNumber_ShouldFollowFormat` exists under a different test name (`CreateDraft_GeneratesRequestNumberFromIdentityId`) — naming mismatch vs. PRD §13.3; low impact but creates a docs/test traceability gap. | §13.3 |
| **TEST-003** | `AuditService` has no test verifying that `ExpireAsync` audit entries have `UserID = SystemUserId`. This is the test that would have caught BUG-001. | §11, §13.3 |

### UX / Blind Spots

| ID | Severity | Description | PRD Ref |
|---|---|---|---|
| **GAP-07** | Low | Provider dashboard shows `MyPendingInfo` count but has no clickable link to a pre-filtered queue (`/requests?status=PendingInfo&provider=…`). PRD §12.1 says "Requests in PendingInfo status requiring my clinical input". | §12.1 |
| **GAP-08** | Low | `NotificationService_ExpirationAlert_ShouldCreateNotificationForSpecialistAndProvider` is implemented in `WorkflowServiceTests` (as `ExpireAsync_SendsNotificationToBothSpecialistAndProvider`) not `NotificationServiceTests`. The PRD §13.3 scenario name implies it belongs in the notification-focused test class. Functional gap: zero, but traceability gap exists. | §13.3 |
| **GAP-09** | Low | Several icon-only buttons and the `ConfirmationModal` close button lack `aria-label` attributes. PRD §15 NFR-008 requires accessible labels on all form controls. | §15 NFR-008 |
| **GAP-10** | Low | `Queue.razor` has no search-by-patient-name or search-by-MRN text field. PRD §8.2 says "filterable by…"; it does not explicitly list free-text patient search, but MRN is an indexed column and a natural usability expectation. The `PaRequestFilter` model has no `PatientMrn` field. | §8.2 |
| **GAP-11** | Low | `AuditLog.razor` bypasses `IAuditService` and queries `AppDbContext` directly via `IDbContextFactory`. This is intentional (read-only, no service abstraction needed) but means audit reads have no service-layer RBAC wrapper — fine for v1 since the page already requires `[Authorize(Roles="Administrator")]`. | §10, §11 |
| **GAP-12** | Info | `DbSeeder` is called only in Development environment (`Program.cs` line 97 `if (app.Environment.IsDevelopment())`). Production migrations are applied by `db.Database.MigrateAsync()` inside `SeedAsync()`, which is never called in Production. A production startup migration step is absent. | §15 NFR-010 |
| **GAP-13** | Info | `ToastService` is registered as `Singleton` but Blazor Server circuits are per-connection. A toast triggered in one circuit will not appear in another, and there is no cross-circuit communication. This is acceptable behavior for v1 but could cause confusion if an admin triggers expiration and expects a toast in the same window. The service's in-memory queue is circuit-scoped effectively. | §16.1 |

### Dead / Residual Code

| ID | Description |
|---|---|
| **DEAD-001** | `ANALYSIS_AND_ROADMAP.md`, `EXECUTION_ROADMAP.md`, `ROADMAP_v2.md`, `ROADMAP_v3.md` — four superseded roadmap documents in the workspace root. They contain stale task IDs and completed items marked incomplete. They should be archived or deleted to reduce confusion. |
| **DEAD-002** | `Prior Authorization Workflow Tracker/Components/Pages/Counter.razor` and `Weather.razor` — default Blazor scaffolding pages still present. Not linked from `NavMenu.razor` but accessible by URL. |
| **DEAD-003** | `ToastService` has an `Items` list and `OnChanged` event for broadcasting toasts. However, `Toast.razor` is never mounted in `App.razor` or `MainLayout.razor` — only `ToastService.Show()` is called. Needs verification of where `<Toast />` is rendered. |

---

## 4. Cleanup / Refactor Recommendations

| ID | Area | Recommendation | Complexity |
|---|---|---|---|
| **REF-001** | `AuditService.LogAsync` | Add an overloaded `LogAsSystemAsync(string entityName, string entityId, string action, ...)` that accepts explicit `userId`/`userName` strings instead of reading from `ICurrentUserService`. Use this in `WorkflowService.ExpireAsync`. Fixes BUG-001 cleanly without coupling `ExpirationJob` further. | Small |
| **REF-002** | `RequestDetail.razor` | Replace the three `DateTime.UtcNow` usages with injected `IDateTimeProvider`. Eliminates BUG-002 and satisfies NFR-009. | Small |
| **REF-003** | `PaRequestFilter` model | Add `PatientMrn` and `PatientName` string fields; wire up in `Queue.razor` and `PaRequestService.GetQueueAsync`. Addresses GAP-10 and makes the queue genuinely searchable. | Small |
| **REF-004** | `DbSeeder` | Add a separate `ApplyMigrationsAsync()` step called from `Program.cs` in all environments; decouple seeding from migration. Addresses GAP-12. | Small |
| **REF-005** | Dead pages | Delete `Counter.razor` and `Weather.razor`. They are scaffolding artifacts with no PRD mapping. | Trivial |
| **REF-006** | Stale docs | Move `ANALYSIS_AND_ROADMAP.md`, `EXECUTION_ROADMAP.md`, `ROADMAP_v2.md`, `ROADMAP_v3.md` to `docs/archive/`. Keep only this document and the current `docs/` folder as authoritative. | Trivial |
| **REF-007** | `ToastService` registration | Change from `Singleton` to `Scoped` so each Blazor circuit has its own toast queue. Alternatively, verify `<Toast />` is rendered in `MainLayout.razor` and document that the service is intentionally singleton with per-circuit rendering. | Small |
| **REF-008** | `AppDbContext` — missing indexes | Add `ReviewerUserID` and `ProviderID` indexes via a new EF Core migration. Required for NFR-001 at realistic scale. | Small |

---

## 5. Phased Remediation Plan

### Phase 1 — Critical Correctness (must ship with v1)
Target: fix the two bugs and the two missing indexes before any production deployment.

| Task | Description | Effort |
|---|---|---|
| **P1-T1** | Fix BUG-001: audit SYSTEM attribution in ExpireAsync | S |
| **P1-T2** | Fix BUG-002: replace `DateTime.UtcNow` in RequestDetail with `IDateTimeProvider` | S |
| **P1-T3** | Add missing database indexes (ReviewerUserID + ProviderID) via migration | S |
| **P1-T4** | Add unit test for BR-013 resubmission (TEST-001) | S |

### Phase 2 — Completeness & Quality (polish before external review)
Target: close the named-scenario traceability gaps, usability gaps, and dead code.

| Task | Description | Effort |
|---|---|---|
| **P2-T1** | Add audit-SYSTEM test (TEST-003) verifying ExpireAsync writes SystemUserId | S |
| **P2-T2** | Add `PatientMrn` / `PatientName` free-text filter to queue (GAP-10) | S |
| **P2-T3** | Add clickable PendingInfo link on Provider dashboard (GAP-07) | S |
| **P2-T4** | Fix aria-labels on icon buttons and ConfirmationModal (GAP-09) | S |
| **P2-T5** | Delete Counter.razor + Weather.razor (DEAD-002) | Trivial |
| **P2-T6** | Verify / fix ToastService registration and mounting (DEAD-003, GAP-13) | S |
| **P2-T7** | Decouple migration from seed in Program.cs (GAP-12, REF-004) | S |

### Phase 3 — Future Improvements (PRD §19)
Items explicitly listed as out-of-scope for v1 but valuable as next steps.

| Task | Description | PRD Ref |
|---|---|---|
| **P3-T1** | Real document storage (Azure Blob or local FS) | §18, §19.2 |
| **P3-T2** | Durable background jobs (Hangfire) replacing IHostedService | §19.1 |
| **P3-T3** | Email/SMS notifications (SendGrid) | §19.5 |
| **P3-T4** | Advanced charting (Chart.js via JS interop) | §19.7 |
| **P3-T5** | Claims-based fine-grained permissions | §19.9 |
| **P3-T6** | Multi-tenancy / Organization dimension | §19.6 |

---

## 6. DAG Task List (Full, All Phases)

Each task below has a unique ID, explicit dependencies, complexity (S/M/L), risk (Low/Med/High), and parallelizability.

```
P1-T1 ──→ P2-T1
P1-T2
P1-T3
P1-T4
        ↓
P2-T2 (independent)
P2-T3 (independent)
P2-T4 (independent)
P2-T5 (independent)
P2-T6 (independent)
P2-T7 (independent)
```

### Detailed Task Cards

---

#### P1-T1 — Fix ExpirationJob audit SYSTEM attribution (BUG-001)

| Field | Value |
|---|---|
| **PRD requirement** | §11 Audit — "Logged Events: Request expired (system)" |
| **Dependencies** | None |
| **Files affected** | `Services/AuditService.cs`, `Services/IAuditService.cs`, `Services/WorkflowService.cs` |
| **Complexity** | S — add one overload, change one call site |
| **Risk** | Low — purely additive; no existing callers change |
| **Parallel** | Yes |

**Implementation:**  
Add `LogAsSystemAsync(string entityName, string entityId, string action, string? oldValues = null, string? newValues = null, CancellationToken ct = default)` to `IAuditService` and `AuditService`. Internally uses `DbSeeder.SystemUserId` and `"SYSTEM"` instead of `_currentUser`. In `WorkflowService.ExpireAsync`, replace the existing `_audit.LogAsync(...)` call with `_audit.LogAsSystemAsync(...)`.

**Validation:**  
`ExpirationJob_ExpireAsync_AuditEntry_HasSystemUserId` — seed an Approved request, call `WorkflowService.ExpireAsync` with an anonymous `FakeCurrentUserService` (userId = ""), verify the `AuditLog` row has `UserID = "00000000-0000-0000-0000-000000000001"` and `UserName = "SYSTEM"`.

---

#### P1-T2 — Fix DateTime.UtcNow usage in RequestDetail (BUG-002)

| Field | Value |
|---|---|
| **PRD requirement** | §15 NFR-009 — no business logic in Razor components |
| **Dependencies** | None |
| **Files affected** | `Components/Pages/Requests/RequestDetail.razor` |
| **Complexity** | S — inject `IDateTimeProvider`, replace 3 usages |
| **Risk** | Low — pure refactor, no behavior change |
| **Parallel** | Yes |

**Implementation:**  
Add `@inject IDateTimeProvider Clock` to `RequestDetail.razor`. Replace `DateTime.UtcNow` with `Clock.UtcNow` in the three inline expressions: (1) overdue due-date highlight, (2) appeal deadline calculation in action buttons, (3) appeal deadline "Closed" badge in denial section.

**Validation:**  
Existing tests unaffected. Manual: verify overdue styling and appeal closed badge still render correctly.

---

#### P1-T3 — Add missing database indexes

| Field | Value |
|---|---|
| **PRD requirement** | §7.3 (implied by NFR-001 performance) |
| **Dependencies** | None |
| **Files affected** | `Data/AppDbContext.cs`, new migration `AddReviewerAndProviderIndexes` |
| **Complexity** | S — two `HasIndex` calls + `dotnet-ef migrations add` |
| **Risk** | Low — non-breaking; additive DDL only |
| **Parallel** | Yes |

**Implementation:**  
In `AppDbContext.OnModelCreating`, inside the `PaRequest` entity block:
```csharp
e.HasIndex(r => r.ReviewerUserID).HasDatabaseName("IX_PaRequests_ReviewerUserID");
e.HasIndex(r => r.ProviderID).HasDatabaseName("IX_PaRequests_ProviderID");
```
Then `dotnet-ef migrations add AddReviewerAndProviderIndexes`.

**Validation:**  
`dotnet-ef database update` applies cleanly. Run `dotnet test` — all 205 tests still pass. Verify index names in SQL Server Management Studio or via migration snapshot.

---

#### P1-T4 — Unit test BR-013 resubmission (TEST-001)

| Field | Value |
|---|---|
| **PRD requirement** | §13.3 named scenarios — BR-013 is listed in §9 and referenced in §13.3 indirectly |
| **Dependencies** | None |
| **Files affected** | `Tests.Unit/Services/PaRequestServiceTests.cs` |
| **Complexity** | S — 3 test methods |
| **Risk** | Low — tests only |
| **Parallel** | Yes |

**Test methods to add:**
```
ResubmitAsync_WithNewComment_Succeeds
ResubmitAsync_WithNewDocument_Succeeds
ResubmitAsync_WithoutNewContent_ThrowsBusinessRuleViolation_BR013
```

**Validation:**  
`dotnet test Tests.Unit --filter PaRequestService` — all 3 new tests pass. Total rises to 200 unit tests.

---

#### P2-T1 — Audit-SYSTEM test for ExpireAsync (TEST-003)

| Field | Value |
|---|---|
| **PRD requirement** | §11, §13.3 |
| **Dependencies** | P1-T1 (fix must exist before test can pass) |
| **Files affected** | `Tests.Unit/Services/WorkflowServiceTests.cs` |
| **Complexity** | S — 1 test method |
| **Risk** | Low |
| **Parallel** | No (depends on P1-T1) |

---

#### P2-T2 — Free-text patient filter in queue (GAP-10)

| Field | Value |
|---|---|
| **PRD requirement** | §8.2 FR-002 — filterable queue |
| **Dependencies** | None |
| **Files affected** | `Services/Models/PaRequestFilter.cs`, `Services/PaRequestService.cs`, `Components/Pages/Requests/Queue.razor` |
| **Complexity** | S |
| **Risk** | Low |
| **Parallel** | Yes |

**Implementation:**  
Add `string? PatientSearch` to `PaRequestFilter`. In `GetQueueAsync`, add:
```csharp
if (!string.IsNullOrWhiteSpace(filter.PatientSearch))
    query = query.Where(r => r.PatientName.Contains(filter.PatientSearch)
                          || r.PatientMrn.Contains(filter.PatientSearch));
```
Add a text input in `Queue.razor` filter bar bound to `_filter.PatientSearch`.

**Validation:**  
Manual: search for "Webb" — only Dr. Webb's patients appear. Search for "MRN-0001" — only that patient's requests appear.

---

#### P2-T3 — PendingInfo link on Provider dashboard (GAP-07)

| Field | Value |
|---|---|
| **PRD requirement** | §12.1 — "Requests in PendingInfo status requiring my clinical input" |
| **Dependencies** | None |
| **Files affected** | `Components/Pages/Home.razor` |
| **Complexity** | S — add anchor tag |
| **Risk** | Low |
| **Parallel** | Yes |

**Implementation:**  
In the Provider panel where `MyPendingInfo > 0` is shown, add a link:
```razor
@if (_providerDash.MyPendingInfo > 0)
{
    <a href="/requests?status=PendingInfo" class="small">Respond now →</a>
}
```
Note: the queue already filters by provider for the Provider role, so no additional URL parameter is needed.

**Validation:**  
Manual: log in as Provider with a PendingInfo request → dashboard shows count with link → link navigates to filtered queue showing only that provider's PendingInfo requests.

---

#### P2-T4 — Accessibility: aria-labels on icon buttons (GAP-09)

| Field | Value |
|---|---|
| **PRD requirement** | §15 NFR-008 |
| **Dependencies** | None |
| **Files affected** | `Components/Shared/ConfirmationModal.razor`, `Components/Pages/Requests/RequestDetail.razor`, `Components/Pages/Requests/Queue.razor`, `Components/Pages/Notifications/Notifications.razor` |
| **Complexity** | S — markup-only changes |
| **Risk** | Low |
| **Parallel** | Yes |

**Implementation:**  
- `ConfirmationModal.razor`: add `aria-label="Close"` to the close button (already present — verify it matches the visual label)  
- `RequestDetail.razor`: audit all `<button>` tags without text for `aria-label`  
- `Toast.razor`: verify close button has `aria-label="Dismiss notification"`

---

#### P2-T5 — Delete scaffold pages (DEAD-002)

| Field | Value |
|---|---|
| **PRD requirement** | Codebase hygiene |
| **Dependencies** | None |
| **Files affected** | `Components/Pages/Counter.razor`, `Components/Pages/Weather.razor` |
| **Complexity** | Trivial |
| **Risk** | Low — verify not linked anywhere |
| **Parallel** | Yes |

---

#### P2-T6 — Fix ToastService registration and mounting (DEAD-003, GAP-13)

| Field | Value |
|---|---|
| **PRD requirement** | §16.1 — toasts for success/error |
| **Dependencies** | None |
| **Files affected** | `Program.cs`, `Components/Layout/MainLayout.razor`, `Services/ToastService.cs` |
| **Complexity** | S — verify mounting, change registration if needed |
| **Risk** | Low |
| **Parallel** | Yes |

**Implementation:**  
Verify `<Toast />` is rendered in `MainLayout.razor`. If absent, add it. Change `AddSingleton<ToastService>()` to `AddScoped<ToastService>()` so each Blazor circuit has its own toast queue. Update any places that resolve `ToastService` from a singleton scope.

---

#### P2-T7 — Decouple migration from seed (GAP-12, REF-004)

| Field | Value |
|---|---|
| **PRD requirement** | §15 NFR-010 |
| **Dependencies** | None |
| **Files affected** | `Program.cs`, `Data/DbSeeder.cs` |
| **Complexity** | S |
| **Risk** | Low |
| **Parallel** | Yes |

**Implementation:**  
In `Program.cs`, outside the `if (app.Environment.IsDevelopment())` block, add:
```csharp
using var migrationScope = app.Services.CreateScope();
await migrationScope.ServiceProvider
    .GetRequiredService<AppDbContext>()
    .Database.MigrateAsync();
```
Remove `await _db.Database.MigrateAsync()` from `DbSeeder.SeedAsync()`. `SeedAsync()` remains Development-only.

---

## 7. Directed Acyclic Graph (Dependencies)

```
                 ┌─────────┐
                 │  P1-T1  │ Fix audit SYSTEM (BUG-001)
                 └────┬────┘
                      │
                      ▼
                 ┌─────────┐
                 │  P2-T1  │ Test audit SYSTEM (TEST-003)
                 └─────────┘

┌─────────┐   ┌─────────┐   ┌─────────┐   ┌─────────┐
│  P1-T2  │   │  P1-T3  │   │  P1-T4  │   │  P2-T2  │
│(BUG-002)│   │(indexes)│   │(BR-013) │   │(search) │
└─────────┘   └─────────┘   └─────────┘   └─────────┘
     │              │             │             │
     └──────────────┴─────────────┴─────────────┘
                         │
                  (all independent — parallel)

┌─────────┐   ┌─────────┐   ┌─────────┐   ┌─────────┐
│  P2-T3  │   │  P2-T4  │   │  P2-T5  │   │  P2-T6  │
│(GAP-07) │   │(aria)   │   │(dead pg)│   │(Toast)  │
└─────────┘   └─────────┘   └─────────┘   └─────────┘
     │              │             │             │
     └──────────────┴─────────────┴─────────────┘
                         │
                  (all independent — parallel)

┌─────────┐
│  P2-T7  │ Decouple migration from seed (independent)
└─────────┘
```

**Only one sequential dependency exists:** P2-T1 must wait for P1-T1 (the bug fix must be in place before the test that verifies it can pass).

All other tasks are fully parallel with no ordering constraint between them.

---

## 8. Parallel Worktree Plan

All P1 and P2 tasks except P2-T1 can run in parallel worktrees. Group them by file-touch surface to minimize merge conflicts:

### Worktree A — Service layer & tests (high-value)
```
P1-T1  Fix AuditService + WorkflowService.ExpireAsync (LogAsSystemAsync)
P1-T4  Add BR-013 resubmission tests to PaRequestServiceTests
```
Files: `Services/AuditService.cs`, `Services/IAuditService.cs`, `Services/WorkflowService.cs`, `Tests.Unit/Services/PaRequestServiceTests.cs`

### Worktree B — Database & migrations
```
P1-T3  Add ReviewerUserID + ProviderID indexes, new migration
P2-T7  Decouple migration from seed in Program.cs
```
Files: `Data/AppDbContext.cs`, `Data/DbSeeder.cs`, `Program.cs`, new `Migrations/` files

### Worktree C — UI refinements (Queue + RequestDetail)
```
P1-T2  Replace DateTime.UtcNow with IDateTimeProvider in RequestDetail
P2-T2  Add PatientSearch filter to queue
P2-T4  Aria-labels on icon buttons
```
Files: `Components/Pages/Requests/RequestDetail.razor`, `Components/Pages/Requests/Queue.razor`, `Services/Models/PaRequestFilter.cs`, `Services/PaRequestService.cs`

### Worktree D — Dashboard + Cleanup (low-risk)
```
P2-T3  Provider PendingInfo link
P2-T5  Delete Counter.razor + Weather.razor
P2-T6  ToastService registration fix
```
Files: `Components/Pages/Home.razor`, `Components/Layout/MainLayout.razor`, `Program.cs`, scaffold page deletions

After all worktrees complete, merge into `main` and run `dotnet test` to confirm 0 regressions.

---

## 9. Recommended First 3–5 Tasks

Execute in this order for maximum impact with minimum risk:

### 1. P1-T1 — Fix BUG-001 (AuditService SYSTEM attribution)
**Why first:** It is a correctness bug in §11 audit compliance. It is small, safe (additive overload), and unblocks P2-T1. Affects every nightly expiration run in production.

### 2. P1-T3 — Add missing indexes
**Why second:** Zero behavioral risk (pure DDL add), can run in parallel with P1-T1. At 50 seed records the missing indexes are invisible, but at 500+ records the reviewer-assigned queue filter and provider dashboard will degrade. Adding them now is cheaper than a hotfix later.

### 3. P1-T4 — BR-013 resubmission test
**Why third:** The code path (`ValidateResubmissionAsync`) is implemented and used in production but has no test. This is a pure test addition that takes 20 minutes and raises the unit test count to 200. Validates a PRD §9 requirement that was previously tested only manually.

### 4. P1-T2 — Fix BUG-002 (RequestDetail DateTime.UtcNow)
**Why fourth:** Correctness refinement. Very small change (inject + 3 replacements). Removes the last `DateTime.UtcNow` usage from Razor components, satisfying NFR-009 fully.

### 5. P2-T2 — Free-text patient search in queue
**Why fifth:** The most user-facing gap. A specialist searching for a specific patient's request must currently scroll through the queue or know the exact MRN. A name/MRN text field is a natural usability feature. The `PatientMrn` column is already indexed, so this is also performant.

---

## 10. Verification Checklist (post-remediation)

After completing all Phase 1 tasks:

```bash
# Build
dotnet build --nologo   # expect: 0 errors, 0 warnings

# All tests
dotnet test --nologo    # expect: 200+ unit + 8 integration = 208+ passed, 0 failed

# Verify migration applies cleanly
dotnet-ef database update --project "Prior Authorization Workflow Tracker"

# Manual smoke
# 1. Log in as system@pademo.internal → should be rejected (IsActive=false)
# 2. Trigger expiration check from Admin UI → verify AuditLog shows UserID=00000000-…-0001
# 3. As Provider, open Dashboard → verify MyPendingInfo shows link when count > 0
# 4. As Specialist, filter queue with patient name search → verify results
# 5. Open an UnderReview request as Reviewer → verify Withdraw button absent (not Reviewer's role)
```
