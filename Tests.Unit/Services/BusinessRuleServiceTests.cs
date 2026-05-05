using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services;
using Prior_Authorization_Workflow_Tracker.Services.Models;
using Tests.Unit.Fakes;

namespace Tests.Unit.Services;

/// <summary>
/// Unit tests for BusinessRuleService (T14, T33 — 100% BR coverage target).
/// Uses EF Core InMemory for DB-dependent checks (BR-001, BR-005, BR-008, BR-013).
/// Field-level validators are also exercised because BusinessRuleService calls them internally.
/// </summary>
public class BusinessRuleServiceTests
{
    // ── Test helpers ──────────────────────────────────────────────────────────

    private static AppDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(opts);
    }

    private static (BusinessRuleService svc, AppDbContext db) Build(
        string dbName, FakeDateTimeProvider? clock = null)
    {
        var db = CreateDb(dbName);
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => CreateDb(dbName));
        clock ??= new FakeDateTimeProvider();
        return (new BusinessRuleService(factory.Object, clock), db);
    }

    private static ProcedureCode ProcWithAuth(int id = 1) =>
        new() { ID = id, Code = $"CPT-{id}", Description = "Test", RequiresPriorAuth = true, IsActive = true };

    private static PaRequest ValidRequest(int procId = 1, int planId = 1) => new()
    {
        ID                     = 0,  // not yet saved
        PatientMrn             = "MRN001",
        PatientName            = "Test Patient",
        ProviderID             = "provider-id",
        ProcedureCodeID        = procId,
        InsurancePlanID        = planId,
        DiagnosisCode          = "M17.11",
        ClinicalJustification  = new string('x', 55),  // BR-010 ≥ 50
        ApprovedUnitsRequested = 5,                    // BR-004 1–999
        Status                 = PaStatus.Draft,
    };

    // ── BR-003: ICD-10 format ─────────────────────────────────────────────────

    [Theory]
    [InlineData("M17.11")]   // valid: 1 letter, 2 digits, dot, 2 digits
    [InlineData("G35")]      // valid: no dot
    [InlineData("C34.10")]   // valid: digit + letter sub-code
    public async Task BR003_ValidIcd10_ShouldPass(string code)
    {
        var (svc, db) = Build($"br003_valid_{code}");
        await db.ProcedureCodes.AddAsync(ProcWithAuth());
        await db.SaveChangesAsync();

        var req = ValidRequest();
        req.DiagnosisCode = code;

        // Should not throw
        await svc.ValidateForSubmissionAsync(req);
    }

    [Theory]
    [InlineData("m17.11")]   // lowercase first letter
    [InlineData("1234")]     // starts with digit
    [InlineData("X")]        // too short (only one char)
    [InlineData("TOOLONG1234")]  // too long
    public async Task BR003_InvalidIcd10_ShouldThrowBusinessRuleViolation(string code)
    {
        var (svc, _) = Build($"br003_invalid_{code}");

        var req = ValidRequest();
        req.DiagnosisCode = code;

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateForSubmissionAsync(req));
        Assert.StartsWith("BR-003", ex.Message);
    }

    // ── BR-004: Units 1–999 ───────────────────────────────────────────────────

    [Fact]
    public async Task BR004_ZeroUnits_ShouldThrow()
    {
        var (svc, _) = Build("br004_zero");
        var req = ValidRequest();
        req.ApprovedUnitsRequested = 0;

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateForSubmissionAsync(req));
        Assert.StartsWith("BR-004", ex.Message);
    }

    [Fact]
    public async Task BR004_ThousandUnits_ShouldThrow()
    {
        var (svc, _) = Build("br004_thousand");
        var req = ValidRequest();
        req.ApprovedUnitsRequested = 1000;

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateForSubmissionAsync(req));
        Assert.StartsWith("BR-004", ex.Message);
    }

    [Fact]
    public async Task BR004_999Units_ShouldPass()
    {
        var (svc, db) = Build("br004_999");
        await db.ProcedureCodes.AddAsync(ProcWithAuth());
        await db.SaveChangesAsync();

        var req = ValidRequest();
        req.ApprovedUnitsRequested = 999;

        await svc.ValidateForSubmissionAsync(req); // must not throw
    }

    // ── BR-010: Justification ≥ 50 chars ─────────────────────────────────────

    [Fact]
    public async Task BR010_Short_Justification_ShouldThrow()
    {
        var (svc, _) = Build("br010_short");
        var req = ValidRequest();
        req.ClinicalJustification = "Too short.";

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateForSubmissionAsync(req));
        Assert.StartsWith("BR-010", ex.Message);
    }

    // ── BR-001: RequiresPriorAuth ─────────────────────────────────────────────

    [Fact]
    public async Task BR001_ProcedureNotRequiringPA_ShouldThrow()
    {
        var (svc, db) = Build("br001_noauth");
        await db.ProcedureCodes.AddAsync(new ProcedureCode
        {
            ID = 1, Code = "CPT-001", Description = "No PA needed",
            RequiresPriorAuth = false, IsActive = true,
        });
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateForSubmissionAsync(ValidRequest()));
        Assert.Equal("BR-001", ex.RuleId);
    }

    [Fact]
    public async Task BR001_ProcedureRequiringPA_ShouldPass()
    {
        var (svc, db) = Build("br001_pass");
        await db.ProcedureCodes.AddAsync(ProcWithAuth());
        await db.SaveChangesAsync();

        await svc.ValidateForSubmissionAsync(ValidRequest()); // must not throw
    }

    // ── BR-005: Duplicate active request ─────────────────────────────────────

    [Fact]
    public async Task BR005_DuplicateActiveSamePlanAndProcedure_ShouldThrow()
    {
        var (svc, db) = Build("br005_dup");
        await db.ProcedureCodes.AddAsync(ProcWithAuth());
        // Existing active request for same patient + procedure + plan
        await db.PaRequests.AddAsync(new PaRequest
        {
            ID = 1, RequestNumber = "PA-2026-00001",
            PatientMrn = "MRN001", ProcedureCodeID = 1, InsurancePlanID = 1,
            Status = PaStatus.Submitted, SubmittedByUserID = "u1", ProviderID = "p1",
        });
        await db.SaveChangesAsync();

        var req = ValidRequest(); // ID=0, same MRN+procedure+plan

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateForSubmissionAsync(req));
        Assert.Equal("BR-005", ex.RuleId);
    }

    [Fact]
    public async Task BR005_DifferentPlan_ShouldNotThrow()
    {
        var (svc, db) = Build("br005_diffplan");
        await db.ProcedureCodes.AddAsync(ProcWithAuth());
        // Existing active request on a DIFFERENT plan (plan 2)
        await db.PaRequests.AddAsync(new PaRequest
        {
            ID = 1, RequestNumber = "PA-2026-00001",
            PatientMrn = "MRN001", ProcedureCodeID = 1, InsurancePlanID = 2,
            Status = PaStatus.Submitted, SubmittedByUserID = "u1", ProviderID = "p1",
        });
        await db.SaveChangesAsync();

        var req = ValidRequest(planId: 1); // same patient + procedure, but plan 1 ≠ plan 2
        await svc.ValidateForSubmissionAsync(req); // must not throw
    }

    // ── BR-006: EndDate after StartDate ──────────────────────────────────────

    [Fact]
    public async Task BR006_EndDateBeforeStart_ShouldThrow()
    {
        var (svc, _) = Build("br006");
        var req = ValidRequest();
        var decision = new ApprovalDecisionModel
        {
            ApprovedUnitsGranted  = 1,
            AuthorizationStartDate = new DateTime(2026, 6, 1),
            AuthorizationEndDate   = new DateTime(2026, 5, 31),  // before start
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateForApprovalAsync(req, decision));
        Assert.Equal("BR-006", ex.RuleId);
    }

    // ── BR-007: Granted ≤ Requested ──────────────────────────────────────────

    [Fact]
    public async Task BR007_GrantedExceedsRequested_ShouldThrow()
    {
        var clock = new FakeDateTimeProvider(); // 2026-05-04
        var (svc, _) = Build("br007", clock);
        var req = ValidRequest();
        req.ApprovedUnitsRequested = 3;

        var decision = new ApprovalDecisionModel
        {
            ApprovedUnitsGranted  = 5,  // exceeds 3
            AuthorizationStartDate = clock.UtcNow.Date,
            AuthorizationEndDate   = clock.UtcNow.AddDays(30),
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateForApprovalAsync(req, decision));
        Assert.Equal("BR-007", ex.RuleId);
    }

    // ── BR-011: StartDate ≥ today ─────────────────────────────────────────────

    [Fact]
    public async Task BR011_StartDateInPast_ShouldThrow()
    {
        var clock = new FakeDateTimeProvider(); // fixed 2026-05-04
        var (svc, _) = Build("br011", clock);
        var req = ValidRequest();

        var decision = new ApprovalDecisionModel
        {
            ApprovedUnitsGranted  = 1,
            AuthorizationStartDate = clock.UtcNow.AddDays(-1).Date, // yesterday
            AuthorizationEndDate   = clock.UtcNow.AddDays(30),
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateForApprovalAsync(req, decision));
        Assert.Equal("BR-011", ex.RuleId);
    }

    // ── BR-012: Granted ≥ 1 ──────────────────────────────────────────────────

    [Fact]
    public async Task BR012_ZeroGranted_ShouldThrow()
    {
        var clock = new FakeDateTimeProvider();
        var (svc, _) = Build("br012", clock);
        var req = ValidRequest();

        var decision = new ApprovalDecisionModel
        {
            ApprovedUnitsGranted  = 0,  // invalid
            AuthorizationStartDate = clock.UtcNow.Date,
            AuthorizationEndDate   = clock.UtcNow.AddDays(30),
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateForApprovalAsync(req, decision));
        Assert.Equal("BR-012", ex.RuleId);
    }

    // ── BR-008: Appeal deadline ───────────────────────────────────────────────

    [Fact]
    public async Task BR008_AppealAtDay31_WithDeadline30_ShouldThrow()
    {
        var decisionDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        // Clock is set to day 32 → past the 30-day deadline
        var clock = new FakeDateTimeProvider { UtcNow = decisionDate.AddDays(32) };
        var (svc, db) = Build("br008_past", clock);

        await db.DenialReasons.AddAsync(new DenialReason
        {
            ID = 1, Code = "MED-NOT-NECESSARY", Description = "Not medically necessary",
            IsAppealable = true, AppealDeadlineDays = 30,
        });
        await db.SaveChangesAsync();

        var req = new PaRequest
        {
            Status = PaStatus.Denied,
            DenialReasonID = 1,
            DecisionRenderedAt = decisionDate,
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateAppealEligibilityAsync(req));
        Assert.Equal("BR-008", ex.RuleId);
    }

    [Fact]
    public async Task BR008_AppealOnDeadlineDay_ShouldPass()
    {
        var decisionDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        // Clock is exactly on day 30 — inclusive deadline
        var clock = new FakeDateTimeProvider { UtcNow = decisionDate.AddDays(30) };
        var (svc, db) = Build("br008_onday", clock);

        await db.DenialReasons.AddAsync(new DenialReason
        {
            ID = 1, Code = "MED-NOT-NECESSARY", Description = "Not medically necessary",
            IsAppealable = true, AppealDeadlineDays = 30,
        });
        await db.SaveChangesAsync();

        var req = new PaRequest
        {
            Status = PaStatus.Denied,
            DenialReasonID = 1,
            DecisionRenderedAt = decisionDate,
        };

        await svc.ValidateAppealEligibilityAsync(req); // must not throw
    }

    [Fact]
    public async Task BR008_NonAppealableReason_ShouldThrow()
    {
        var (svc, db) = Build("br008_noappeal");
        await db.DenialReasons.AddAsync(new DenialReason
        {
            ID = 1, Code = "ADMIN-DENY", Description = "Administrative denial",
            IsAppealable = false, AppealDeadlineDays = 0,
        });
        await db.SaveChangesAsync();

        var req = new PaRequest
        {
            Status = PaStatus.Denied,
            DenialReasonID = 1,
            DecisionRenderedAt = DateTime.UtcNow.AddDays(-1),
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateAppealEligibilityAsync(req));
        Assert.Equal("BR-008", ex.RuleId);
    }

    // ── BR-009: Cannot withdraw from terminal states ──────────────────────────

    [Theory]
    [InlineData(PaStatus.Approved)]
    [InlineData(PaStatus.Expired)]
    [InlineData(PaStatus.Denied)]
    [InlineData(PaStatus.Draft)]
    [InlineData(PaStatus.Withdrawn)]
    public void BR009_WithdrawFromProhibitedStatus_ShouldThrow(PaStatus status)
    {
        var (svc, _) = Build("br009");
        var req = new PaRequest { Status = status };

        var ex = Assert.Throws<BusinessRuleViolationException>(
            () => svc.ValidateCanWithdraw(req));
        Assert.Equal("BR-009", ex.RuleId);
    }

    [Theory]
    [InlineData(PaStatus.Submitted)]
    [InlineData(PaStatus.PendingInfo)]
    [InlineData(PaStatus.UnderReview)]
    [InlineData(PaStatus.Appealed)]
    public void BR009_WithdrawFromAllowedStatus_ShouldPass(PaStatus status)
    {
        var (svc, _) = Build("br009_ok");
        var req = new PaRequest { Status = status };

        svc.ValidateCanWithdraw(req); // must not throw
    }

    // ── BR-013: Re-submission requires new content ────────────────────────────

    [Fact]
    public async Task BR013_NoNewContentAfterPendingInfo_ShouldThrow()
    {
        var (svc, db) = Build("br013_noContent");
        var pendingAt = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

        // History: entered PendingInfo on pendingAt
        await db.PaStatusHistories.AddAsync(new PaStatusHistory
        {
            ID = 1, PaRequestID = 42,
            FromStatus = PaStatus.Submitted, ToStatus = PaStatus.PendingInfo,
            ChangedByUserID = "reviewer", ChangedAt = pendingAt,
        });
        await db.SaveChangesAsync();

        // No comments or documents added after pendingAt
        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ValidateResubmissionAsync(42));
        Assert.Equal("BR-013", ex.RuleId);
    }

    [Fact]
    public async Task BR013_NewCommentAfterPendingInfo_ShouldPass()
    {
        var (svc, db) = Build("br013_newComment");
        var pendingAt = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

        await db.PaStatusHistories.AddAsync(new PaStatusHistory
        {
            ID = 1, PaRequestID = 42,
            FromStatus = PaStatus.Submitted, ToStatus = PaStatus.PendingInfo,
            ChangedByUserID = "reviewer", ChangedAt = pendingAt,
        });
        await db.PaComments.AddAsync(new PaComment
        {
            PaRequestID = 42, CommentText = "New clinical evidence attached.",
            IsInternalOnly = false, AuthoredByUserID = "specialist",
            AuthoredAt = pendingAt.AddHours(2), // after pendingAt
        });
        await db.SaveChangesAsync();

        await svc.ValidateResubmissionAsync(42); // must not throw
    }

    // ── BR-014: Hard delete rules ─────────────────────────────────────────────

    [Fact]
    public void BR014_NonDraftRequest_ShouldThrow()
    {
        var (svc, _) = Build("br014_nondraft");
        var req = new PaRequest { Status = PaStatus.Submitted, SubmittedByUserID = "uid1" };

        var ex = Assert.Throws<BusinessRuleViolationException>(
            () => svc.ValidateCanHardDelete(req, "uid1", isAdmin: false));
        Assert.Equal("BR-014", ex.RuleId);
    }

    [Fact]
    public void BR014_DraftByDifferentUser_NonAdmin_ShouldThrow()
    {
        var (svc, _) = Build("br014_wronguser");
        var req = new PaRequest { Status = PaStatus.Draft, SubmittedByUserID = "uid1" };

        var ex = Assert.Throws<BusinessRuleViolationException>(
            () => svc.ValidateCanHardDelete(req, callerUserId: "uid2", isAdmin: false));
        Assert.Equal("BR-014", ex.RuleId);
    }

    [Fact]
    public void BR014_DraftByCreator_ShouldPass()
    {
        var (svc, _) = Build("br014_creator");
        var req = new PaRequest { Status = PaStatus.Draft, SubmittedByUserID = "uid1" };

        svc.ValidateCanHardDelete(req, callerUserId: "uid1", isAdmin: false); // must not throw
    }

    [Fact]
    public void BR014_DraftByAdmin_ShouldPass()
    {
        var (svc, _) = Build("br014_admin");
        var req = new PaRequest { Status = PaStatus.Draft, SubmittedByUserID = "uid1" };

        svc.ValidateCanHardDelete(req, callerUserId: "uid999", isAdmin: true); // must not throw
    }
}
