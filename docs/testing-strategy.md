# Testing Strategy

**Last Updated:** 2026-05-06 (V5 remediation — V5-T1 aria-labels, V5-T2–T4 IDateTimeProvider in all Razor components, V5-T5 seeder integration tests; 225 total tests)

---

## Test Projects

| Project | Framework | Status | Purpose |
|---|---|---|---|
| `Tests.Unit` | xUnit 2.9.2 + Moq 4.20.72 | ✅ 200 tests passing | Service layer logic, workflow rules, validation |
| `Tests.Integration` | xUnit + Testcontainers.MsSql 3.10.0 | ✅ 25 tests passing | Concurrency (RowVersion), SQL view smoke tests, unique index enforcement, **seeder volume and status distribution (V5-T5)** |

**Note:** `Tests.Integration` explicitly uses Testcontainers rather than EF Core InMemory, because InMemory does not enforce FK constraints, does not support raw SQL, and cannot execute database views.

---

## Coverage Targets (§13.2)

| Layer | Target | Current |
|---|---|---|
| WorkflowService | ≥ 90% | ✅ 41 tests (all valid transitions incl. 3 Appealed-origin paths — §8.7 fix; invalid transitions; concurrent decision; appeal window; admin override; AUTH format regex hardened — §3.25.2; non-appealable denial — §13.3; AUTH uniqueness under load — ROADMAP_v3 §7; **TEST-003: ExpireAsync uses LogAsSystemAsync**; **BR-013 doc-only resubmission variant**) |
| `PaRequestService` | ≥ 80% | ✅ 25 tests (draft CRUD, queue filters incl. **PatientSearch GAP-10**, comment RBAC, document upload RBAC, BR-014) |
| BusinessRuleValidator | 100% | ✅ 26 tests (BR-001 through BR-014, including Theory variants) |
| AuditService | ≥ 70% | ✅ 6 tests (factory, fault tolerance, user context, old/new values, CorrelationId set, CorrelationId null) |
| NotificationService | ≥ 70% | ✅ 8 tests (create, unread, count, markRead, markAll, expiry) |
| ReportingService | ≥ 60% | ✅ 36 tests (status grouping, overdue counts, denial aggregates, monthly volume, expiring window, dashboard summary, turnaround time by priority, provider volume, specialist dashboard + overdue (GAP-01), provider dashboard, reviewer dashboard + pending>5days (GAP-05) + avg decision time (GAP-06), approval rate, billing dashboard + pending count (GAP-03), recent activity feed (GAP-02)) |
| UserManagementService | — | ✅ 13 tests (RBAC, system-user guard, create, deactivate, reactivate, role-change, not-found) |
| CsvExportService | — | ✅ 7 tests (RBAC, 18-column header, filename format, audit event, status filter, priority filter) |
| ExpirationJob | — | ✅ 5 tests (no candidates, one expired, active skip, double-expiration guard, per-request failure isolation) |

**Total: 200 unit + 25 integration = 225 tests, 0 failures.**

---

## Mocking Strategy (§13.4)

| Dependency | Fake / Mock |
|---|---|
| `IDateTimeProvider` | `FakeDateTimeProvider` — mutable `UtcNow` property |
| `ICurrentUserService` | `FakeCurrentUserService` — role-aware, factory methods `AsSpecialist()`, `AsReviewer()`, etc. |
| `IAuditService` | `Mock<IAuditService>` via Moq — verify interaction on both `LogAsync` and `LogAsSystemAsync`; not persistence |
| `IDbContextFactory<AppDbContext>` | `Mock<IDbContextFactory<AppDbContext>>` with in-memory DB for unit tests only |

**Important:** In-memory `AppDbContext` is used only in `Tests.Unit` where FK enforcement and SQL views are not required. All tests that need FK constraint, RowVersion concurrency, or SQL view correctness are in `Tests.Integration` (Testcontainers SQL Server).

`ExpirationJobTests` mock `IServiceScopeFactory` so both the candidate-ID query (via `IDbContextFactory<AppDbContext>`) and the actual expire call (via `IWorkflowService`) are intercepted without needing real hosted-service infrastructure.

---

## Test Scenarios Checklist (§13.3)

### WorkflowService (T32 — complete)

- [x] `WorkflowService_TransitionStatus_ValidTransition_ShouldSucceed` (Theory: 9 valid pairs, incl. Appealed → UnderReview/Approved/Denied)
- [x] `WorkflowService_TransitionStatus_InvalidTransition_ShouldThrowWorkflowException` (Theory: 3 invalid pairs)
- [x] `WorkflowService_Approve_ShouldGenerateAuthorizationNumber` — asserts `^AUTH-\d{8}-[0-9A-F]{6}$` regex (hardened from `StartsWith` in §3.25.2)
- [x] `WorkflowService_Deny_NonAppealableReason_ShouldNotAllowAppeal` (sends AppealWindow notification for appealable)
- [x] `WorkflowService_Appeal_PastDeadline_ShouldThrowBusinessRuleException`
- [x] `BeginReviewAsync_FromAppealed_TransitionsToUnderReview` (§8.7 fix — §3.27)
- [x] `ApproveAsync_FromAppealed_OverturnsDenial` (§8.7 fix — appeal overturned, §3.27)
- [x] `DenyAsync_FromAppealed_UpholdsDenial` (§8.7 fix — appeal upheld, §3.27)
- [x] `DenyAsync_NonAppealableReason_ShouldNotSendAppealWindowNotification` (§13.3 PRD key scenario — non-appealable denial must not trigger AppealWindow notification)
- [x] `GenerateAuthorizationNumber_IsUnique_UnderLoad` (ROADMAP_v3 §7 — BUG-01 CSPRNG uniqueness: 100 burst calls, P(collision) ≈ 0.03%)
- [x] `WorkflowService_ConcurrentDecision_ShouldThrowDbUpdateConcurrencyException` (✅ Tests.Integration — Testcontainers SQL Server, real RowVersion enforcement)
- [x] `WorkflowService_Withdraw_FromDraftStatus_ShouldThrowBusinessRuleException`
- [x] `WorkflowService_Approve_ZeroUnitsGranted_ShouldThrowBusinessRuleException`
- [x] `WorkflowService_Withdraw_FromDeniedStatus_ShouldThrowBusinessRuleException` (BR-009)

### BusinessRuleValidator (T33 — complete)

- [x] BR-001: ProcedureCode.RequiresPriorAuth = false → validation failure
- [x] BR-003: Invalid ICD-10 code fails regex (Theory: lowercase, digits-only, too-short, too-long)
- [x] BR-003: Valid ICD-10 codes (e.g. "M17.11", "G35", "C34.10") pass
- [x] BR-004: Units = 0 → failure; Units = 1000 → failure; Units = 999 → pass
- [x] BR-005: Duplicate active request same InsurancePlanID → failure
- [x] BR-005: Different InsurancePlanID from same payer → pass
- [x] BR-006: EndDate = StartDate → failure; EndDate > StartDate → pass
- [x] BR-007: GrantedUnits > RequestedUnits → failure
- [x] BR-008: Appeal at day 31 (deadline = 30) → failure
- [x] BR-008: Appeal at day 30 → pass (inclusive deadline)
- [x] BR-010: Justification < 50 chars → failure; ≥ 50 → pass
- [x] BR-011: AuthorizationStartDate = yesterday → failure (uses FakeDateTimeProvider)
- [x] BR-012: ApprovedUnitsGranted = 0 → failure
- [x] BR-013: Re-submit with no new content → failure; new comment → pass
- [x] BR-014: Hard delete by non-creator non-admin → failure; creator → pass; admin → pass

### PaRequestService (T34 — complete)

- [x] `PaRequestService_GenerateRequestNumber_ShouldFollowFormat` (`PA-YYYY-NNNNN`)
- [x] Draft save with minimum 3 fields succeeds
- [x] Draft save missing PatientMrn → failure
- [x] Provider role blocked from CreateDraft → AuthorizationException
- [x] Inactive user blocked from CreateDraft → AuthorizationException
- [x] Provider RBAC: only sees own patients in GetQueueAsync
- [x] Specialist RBAC: sees all requests in GetQueueAsync
- [x] Status filter applied correctly in GetQueueAsync
- [x] Provider does not see internal comments (BUG-009 mitigation)
- [x] Specialist sees all comments including internal
- [x] DeleteDraft by creator succeeds (hard delete)
- [x] DeleteDraft by different specialist → BR-014 failure
- [x] TotalCount from GetQueueAsync matches seeded data

### AuditService (T35 — partial coverage)

- [x] Factory called on successful log (test: `LogAsync_CreatesDbContextAndSaves`)
- [x] Exception in factory swallowed (test: `LogAsync_DoesNotThrow_WhenContextFactoryFails`)
- [x] User context passed through (test: `LogAsync_UsesCurrentUserContext`)
- [x] OldValues/NewValues stored correctly (`LogAsync_PersistsOldValuesAndNewValues`)
- [x] CorrelationId read from `IHttpContextAccessor` when HttpContext has value in Items (`LogAsync_SetsCorrelationId_WhenHttpContextItemsContainsId`) — **T-D5**
- [x] CorrelationId is null when HttpContext is null (`LogAsync_CorrelationId_IsNull_WhenHttpContextIsNull`) — **T-D5**
- [ ] Append-only: no UPDATE/DELETE operations on AuditLogs (integration test)

### NotificationService (complete)

- [x] `NotificationService_ExpirationAlert_ShouldCreateNotificationForSpecialistAndProvider`
- [x] `NotificationType` stored correctly; `IsRead = false` on creation
- [x] `CreateAsync` silently skips empty userId (no exception)
- [x] `GetUnreadAsync` returns only unread notifications for the target user
- [x] `GetUnreadCountAsync` counts correctly
- [x] `MarkReadAsync` sets `IsRead = true` and `ReadAt`
- [x] `MarkReadAsync` with wrong owner is silently ignored (security: no enumeration attack)
- [x] `MarkAllReadAsync` marks all unread for a user in one save

### WorkflowService — ResubmitAsync (T-D3)

- [x] `ResubmitAsync_FromPendingInfo_TransitionsToSubmitted` — happy path with PaStatusHistory row + comment satisfying BR-013
- [x] `ResubmitAsync_WrongStatus_ThrowsWorkflowTransitionException` — called from Submitted
- [x] `ResubmitAsync_WrongRole_ThrowsAuthorizationException` — Reviewer is not authorized
- [x] `ResubmitAsync_NoNewContent_ThrowsBusinessRuleViolation` — BR-013 no comment/document

### PaRequestService — UpdateDraft and AddComment RBAC (T-D4)

- [x] `UpdateDraft_UpdatesFieldsOnOwnDraft` — owning Specialist can edit Draft
- [x] `UpdateDraft_NonOwnerSpecialist_ThrowsAuthorizationException` — other Specialist is rejected
- [x] `UpdateDraft_NonDraftStatus_ThrowsBusinessRuleViolation` — UnderReview request rejected
- [x] `AddComment_Provider_CannotAddInternalComment` — Provider blocked from `isInternal=true`
- [x] `AddComment_Provider_CanAddPublicComment` — Provider allowed for `isInternal=false`
- [x] `AddComment_Specialist_CanAddInternalComment` — Specialist can post internal comment

### PaRequestService — AddDocumentAsync / GetDocumentsAsync (T-C4)

- [x] `AddDocumentAsync_Specialist_AttachesDocumentAndWritesAudit` — happy path; verifies `PaDocument` fields and audit mock called with `Uploaded`
- [x] `AddDocumentAsync_BillingManager_ThrowsAuthorizationException` — Billing Manager is not in the allowed upload role set
- [x] `GetDocumentsAsync_ReturnsDocsOrderedByUploadedAt` — two docs verified in chronological order
- [x] `AddDocumentAsync_EmptyFileName_ThrowsBusinessRuleViolation` — whitespace-only name rejected

### ExpirationJob (T-T1 — new)

- [x] `TriggerAsync_NoExpiredRequests_ReturnsZero` — empty database returns 0
- [x] `TriggerAsync_OneExpiredRequest_CallsExpireAndReturnsOne` — past-end-date Approved request triggers `WorkflowService.ExpireAsync`
- [x] `TriggerAsync_ActiveApprovedRequest_IsNotExpired` — end date = today (strict `<` boundary) is not selected
- [x] `TriggerAsync_WorkflowTransitionException_SkipsAndReturnsZeroExpired` — double-expiration race guard: `WorkflowTransitionException` caught, counted as skip
- [x] `TriggerAsync_PerRequestFailure_ContinuesOtherRequests` — unexpected failure on req1 does not prevent req2 from expiring

### UserManagementService (T-T2 — additions)

- [x] `ReactivateUserAsync_NonAdmin_ThrowsAuthorizationException`
- [x] `ReactivateUserAsync_HappyPath_SetsIsActiveTrue` — verifies `IsActive = true` and audit `Reactivated` event
- [x] `ReactivateUserAsync_UnknownUser_ThrowsEntityNotFoundException`

### CsvExportService (T-T3 — additions)

- [x] `ExportRequestsAsync_PriorityFilter_OnlyExportsMatchingRows` — seeds Urgent + Routine; filters by Urgent; asserts 1 data row returned (validates BUG-02 fix)

### WorkflowService — AdminOverrideAsync (T-C5)

- [x] `AdminOverrideAsync_Admin_ForcesStatusAndWritesHistory` — status changed, PaStatusHistory row has `[Admin Override]` prefix, audit mock called with `AdminOverride`
- [x] `AdminOverrideAsync_NonAdmin_ThrowsAuthorizationException` — Specialist cannot override
- [x] `AdminOverrideAsync_EmptyJustification_ThrowsBusinessRuleViolation` — whitespace-only justification rejected
- [x] `AdminOverrideAsync_RequestNotFound_ThrowsEntityNotFoundException` — non-existent request ID

### ExpirationJob (T-T1 — new)

- [x] `TriggerAsync_NoExpiredRequests_ReturnsZero` — empty database returns 0; `ExpireAsync` never called
- [x] `TriggerAsync_OneExpiredRequest_CallsExpireAndReturnsOne` — past-end-date Approved request triggers `WorkflowService.ExpireAsync`
- [x] `TriggerAsync_ActiveApprovedRequest_IsNotExpired` — end date = today (strict `<` boundary) is not selected
- [x] `TriggerAsync_WorkflowTransitionException_SkipsAndReturnsZeroExpired` — double-expiration race guard: `WorkflowTransitionException` caught, counted as skip (not failure)
- [x] `TriggerAsync_PerRequestFailure_ContinuesOtherRequests` — unexpected failure on request 1 does not prevent request 2 from expiring; total = 1

### UserManagementService — ReactivateUserAsync (T-T2 — new)

- [x] `ReactivateUserAsync_NonAdmin_ThrowsAuthorizationException`
- [x] `ReactivateUserAsync_HappyPath_SetsIsActiveTrue` — verifies `IsActive = true` and audit `Reactivated` event written
- [x] `ReactivateUserAsync_UnknownUser_ThrowsEntityNotFoundException`

### CsvExportService — Priority filter (T-T3 — new)

- [x] `ExportRequestsAsync_PriorityFilter_OnlyExportsMatchingRows` — seeds Urgent + Routine requests; filters by Urgent; asserts exactly 1 data row returned (regression test for BUG-02 fix)

### ReportingService — Dashboard CountAsync refactor (T-R1 — new)

- [x] `GetDashboardSummaryAsync_ReturnsCorrectServerSideCounts` — seeds 7 known requests across statuses; asserts all 7 KPI counts exactly
- [x] `GetSpecialistDashboardAsync_ReturnsOnlyOwnedRequests` — seeds specialist's own requests + another user's; asserts all counts reflect only the specialist's requests
- [x] `GetBillingDashboardAsync_ComputesApprovalRateFromServerSideCounts` — seeds 3 Approved + 1 Denied this month; asserts `ApprovedActive`, `ExpiringSoon`, `ExpiredThisMonth`, `DeniedThisMonth`, `ApprovedThisMonth`, `ApprovalRatePct = 75.0`

### ReportingService — Dashboard parity (GAP-01–GAP-06, v3)

- [x] `GetSpecialistDashboardAsync_CountsOverdueRequests` — seeds 1 overdue, 1 future-due, 1 approved-overdue (terminal), 1 other-user-overdue; asserts `MyOverdueRequests == 1`
- [x] `GetSpecialistDashboardAsync_OverdueRequests_IsZero_WhenNoneOverdue` — no overdue requests; asserts `MyOverdueRequests == 0`
- [x] `GetReviewerDashboardAsync_CountsPendingMoreThan5Days` — seeds requests at -6, -5, -3 days and UnderReview at -7 days; asserts count = 3 (boundary inclusive)
- [x] `GetReviewerDashboardAsync_ComputesAvgDecisionDaysThisMonth` — seeds 3 decided this month (2, 4, 6 day turnaround); asserts `AvgDecisionDaysThisMonth == 4.0`
- [x] `GetReviewerDashboardAsync_AvgDecisionDays_IsZero_WhenNoDecisionsThisMonth` — open requests only; asserts `AvgDecisionDaysThisMonth == 0.0`
- [x] `GetBillingDashboardAsync_ReturnsPendingRequestsCount` — seeds 3 active + 2 terminal; asserts `PendingRequests == 3`
- [x] `GetRecentActivityAsync_ReturnsLatestNItems_OrderedDescending` — seeds 12 history rows; asserts only 10 returned, in descending order
- [x] `GetRecentActivityAsync_IsScopedToSpecialist` — seeds 1 row for target specialist + 1 for another; asserts only target's row returned
- [x] `GetRecentActivityAsync_ReturnsEmpty_WhenNoHistory` — no history; asserts empty list

### T-D6: Integration Tests (Testcontainers SQL Server)

- [x] `WorkflowService_ConcurrentDecision_ShouldThrowDbUpdateConcurrencyException` — loads the same `UnderReview` request in two independent `DbContext` instances; Reviewer A approves (SaveChanges → RowVersion bumped); Reviewer B's concurrent deny throws `DbUpdateConcurrencyException` (SQL Server enforces ROWVERSION)
- [x] `WorkflowService_AfterConcurrentConflict_WinnerDataIsPreserved` — after B's save is rejected, reads the committed row; confirms Approved status, `AuthorizationNumber`, and `ApprovedUnitsGranted` match A's winning write
- [x] `vw_PaRequestSummary_ReturnsRows_WhenRequestsExist` — view queryable via raw SQL after `MigrateAsync()`
- [x] `vw_RequestVolumeByMonth_ReturnsRows_WhenRequestsExist` — aggregation view returns at least one row
- [x] `vw_ExpiringAuthorizations_ReturnsRows_WhenExpiringAuthsExist` — seeded request expires in 20 days; view returns ≥ 1 row
- [x] `vw_DenialsByReason_IsQueryable_WithoutError` — denial aggregation view executes without exception (0 rows is valid)
- [x] `AuthorizationNumber_UniqueIndex_PreventsDuplicates` — two approved requests with identical non-null `AuthorizationNumber` throw `DbUpdateException` (SQL Server unique index violation)
- [x] `AuthorizationNumber_UniqueIndex_AllowsMultipleNulls_ForDraftRequests` — two draft requests with `AuthorizationNumber = null` both insert without error (partial index `WHERE IS NOT NULL`)

### RequestDetail.razor — Action buttons refactored (T-R2 — no new tests)

T-R2 is a pure markup refactoring (no behavior change). Existing manual workflow tests (approve/deny/appeal/withdraw/override) validate that the new Razor markup renders identical button sets to the former RenderFragment builder approach. No new unit tests added; behavior verified by existing WorkflowService + PaRequestService tests.

### Seeder Volume & Distribution (V5-T5 — integration, 17 tests)

- [x] `Seeder_PaRequestCount_IsExactly50` — total PA request count matches PRD §14.2
- [x] `Seeder_StatusHistoryCount_IsAtLeast80` — history rows ≥ 80 (actual ~120)
- [x] `Seeder_CommentCount_IsAtLeast60` — comments ≥ 60 (actual 72)
- [x] `Seeder_DraftCount_IsExactly3` — §14.3 status distribution
- [x] `Seeder_SubmittedCount_IsExactly6` — §14.3
- [x] `Seeder_PendingInfoCount_IsExactly5` — §14.3
- [x] `Seeder_UnderReviewCount_IsExactly7` — §14.3
- [x] `Seeder_ApprovedCount_IsExactly15` — §14.3
- [x] `Seeder_DeniedCount_IsExactly7` — §14.3
- [x] `Seeder_AppealedCount_IsExactly3` — §14.3
- [x] `Seeder_WithdrawnCount_IsExactly2` — §14.3
- [x] `Seeder_ExpiredCount_IsExactly2` — §14.3
- [x] `Seeder_DemoUserCount_IsAtLeast9` — 8 demo + SYSTEM reserved (§14.1)
- [x] `Seeder_SystemUser_ExistsAndIsInactive` — SYSTEM account IsActive=false, cannot log in (§8.6)
- [x] `Seeder_InsurancePlanCount_IsExactly8` — reference data (§14.2)
- [x] `Seeder_ProcedureCodeCount_IsExactly25` — reference data (§14.2)
- [x] `Seeder_DenialReasonCount_IsExactly12` — reference data (§14.2)

---

## Running Tests

```bash
# Run all tests (unit + integration)
dotnet test

# Run only integration tests (requires Docker Engine running)
dotnet test Tests.Integration

# Run only unit tests
dotnet test Tests.Unit

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run a specific test class
dotnet test --filter "FullyQualifiedName~ExceptionHierarchyTests"

# Verbose output
dotnet test --verbosity normal
```

---

## Test Infrastructure Files

| File | Purpose |
|---|---|
| `Tests.Unit/Services/BusinessRuleServiceTests.cs` | 26 tests covering BR-001 through BR-014 |
| `Tests.Unit/Services/WorkflowServiceTests.cs` | 24 tests covering state machine transitions, role guards, BeginReview, RequestAdditionalInfo |
| `Tests.Unit/Services/PaRequestServiceTests.cs` | 13 tests covering CRUD, RBAC, comment filtering |
| `Tests.Unit/Services/NotificationServiceTests.cs` | 8 tests covering notification lifecycle |
| `Tests.Unit/Services/AuditServiceTests.cs` | Tests including OldValues/NewValues persistence (T-D1) |
| `Tests.Unit/Services/ExpirationJobTests.cs` | 5 tests covering candidate discovery, active-request skip, double-expiration guard, per-request failure isolation (T-T1) |
| `Tests.Unit/Services/UserManagementServiceTests.cs` | 13 tests covering RBAC, system-user guards, create, deactivate, reactivate, role-change, not-found (T-D3, T-T2) |
| `Tests.Unit/Services/CsvExportServiceTests.cs` | 7 tests covering RBAC, 18-column header, filename format, audit event, status filter, priority filter (T-D4, T-T3) |
| `Tests.Unit/Services/ReportingServiceTests.cs` | 36 tests covering status grouping, overdue, denial aggregates, monthly volume, expiring window, dashboard (all 5 role variants), turnaround time, provider volume, approval rate by priority, GAP-01–GAP-06 new KPIs, `GetRecentActivityAsync` (T-R1, v3) |
| `Tests.Integration/SqlServerFixture.cs` | Testcontainers `MsSqlContainer` IAsyncLifetime fixture; runs `MigrateAsync()` once per test run; shared via `[Collection("SqlServer")]` |
| `Tests.Integration/WorkflowConcurrencyTests.cs` | 2 tests: concurrent RowVersion conflict + winner-data preservation (T-D6) |
| `Tests.Integration/SqlViewSmokeTests.cs` | 4 tests: each of the four SQL reporting views queryable after migration |
| `Tests.Integration/UniqueIndexTests.cs` | 2 tests: duplicate non-null `AuthorizationNumber` rejected; multiple NULLs allowed (partial index) |
| `Tests.Integration/SeederIntegrationTests.cs` | **17 tests (V5-T5)**: exact PA request count (50), status distribution (Draft:3, Submitted:6, PendingInfo:5, UnderReview:7, Approved:15, Denied:7, Appealed:3, Withdrawn:2, Expired:2), status-history ≥80 rows, comments ≥60, demo user count ≥9, SYSTEM user IsActive=false, insurance plan count (8), procedure code count (25), denial reason count (12) |
