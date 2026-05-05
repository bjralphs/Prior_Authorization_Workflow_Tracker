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
/// Unit tests for WorkflowService (T12, T32 — ≥ 90% coverage target).
/// Uses in-memory AppDbContext; mocks IAuditService and INotificationService.
/// </summary>
public class WorkflowServiceTests
{
    // ── Builder ───────────────────────────────────────────────────────────────

    private record Sut(
        WorkflowService Service,
        AppDbContext Db,
        Mock<IAuditService> AuditMock,
        Mock<INotificationService> NotifMock,
        FakeDateTimeProvider Clock,
        FakeCurrentUserService User);

    private static AppDbContext MakeDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(opts);
    }

    private static Sut Build(
        string dbName,
        FakeCurrentUserService? user = null,
        FakeDateTimeProvider? clock = null)
    {
        user  ??= FakeCurrentUserService.AsSpecialist();
        clock ??= new FakeDateTimeProvider();

        var db = MakeDb(dbName);

        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => MakeDb(dbName));

        var ruleFactory = new Mock<IDbContextFactory<AppDbContext>>();
        ruleFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(() => MakeDb(dbName));

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        audit.Setup(a => a.LogAsSystemAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var notif = new Mock<INotificationService>();
        notif.Setup(n => n.CreateAsync(
            It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(),
            It.IsAny<NotificationType>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var rules = new BusinessRuleService(ruleFactory.Object, clock);

        var svc = new WorkflowService(
            factory.Object, user, clock, audit.Object,
            rules, notif.Object, NullLogger<WorkflowService>.Instance);

        return new Sut(svc, db, audit, notif, clock, user);
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static InsurancePlan DefaultPlan() => new()
    {
        ID = 1, PlanName = "Test Plan", PayerName = "Test Payer",
        PlanType = "PPO", RoutineDecisionDays = 14,
        UrgentDecisionDays = 3, EmergentDecisionDays = 1, IsActive = true,
    };

    private static ProcedureCode DefaultProc() => new()
    {
        ID = 1, Code = "27447", Description = "Total knee replacement",
        RequiresPriorAuth = true, TypicalAuthorizationDurationDays = 90, IsActive = true,
    };

    private static async Task<PaRequest> SeedRequestAsync(
        AppDbContext db, PaStatus status = PaStatus.Draft,
        string specialistId = "specialist-id", string providerId = "provider-id")
    {
        var req = new PaRequest
        {
            RequestNumber          = "PA-2026-00001",
            PatientMrn             = "MRN001",
            PatientName            = "Test Patient",
            PatientDob             = new DateTime(1970, 1, 1),
            InsurancePlanID        = 1,
            ProcedureCodeID        = 1,
            ProviderID             = providerId,
            SubmittedByUserID      = specialistId,
            DiagnosisCode          = "M17.11",
            ClinicalJustification  = new string('x', 55),
            ApprovedUnitsRequested = 5,
            Status                 = status,
            Priority               = PaPriority.Routine,
            CreatedAt              = DateTime.UtcNow,
            UpdatedAt              = DateTime.UtcNow,
        };
        if (status == PaStatus.UnderReview || status == PaStatus.Approved ||
            status == PaStatus.Denied  || status == PaStatus.Appealed)
        {
            req.SubmittedAt    = DateTime.UtcNow.AddDays(-3);
            req.DecisionDueDate = DateTime.UtcNow.AddDays(11);
        }
        if (status == PaStatus.Denied)
        {
            req.DenialReasonID     = 1;
            req.DecisionRenderedAt = DateTime.UtcNow.AddDays(-1);
        }
        db.PaRequests.Add(req);
        await db.SaveChangesAsync();
        return req;
    }

    // ── IsTransitionValid ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(PaStatus.Draft,       PaStatus.Submitted,   true)]
    [InlineData(PaStatus.Submitted,   PaStatus.UnderReview, true)]
    [InlineData(PaStatus.UnderReview, PaStatus.Approved,    true)]
    [InlineData(PaStatus.UnderReview, PaStatus.Denied,      true)]
    [InlineData(PaStatus.Denied,      PaStatus.Appealed,    true)]
    [InlineData(PaStatus.Approved,    PaStatus.Expired,     true)]
    [InlineData(PaStatus.Appealed,    PaStatus.UnderReview, true)]  // §8.7: appeal reopened
    [InlineData(PaStatus.Appealed,    PaStatus.Approved,    true)]  // §8.7: appeal overturned
    [InlineData(PaStatus.Appealed,    PaStatus.Denied,      true)]  // §8.7: appeal upheld
    [InlineData(PaStatus.Draft,       PaStatus.Approved,    false)] // invalid
    [InlineData(PaStatus.Approved,    PaStatus.Submitted,   false)] // invalid
    [InlineData(PaStatus.Expired,     PaStatus.Draft,       false)] // invalid
    public void IsTransitionValid_KnownPairs(PaStatus from, PaStatus to, bool expected)
    {
        var sut = Build("transition_table");
        Assert.Equal(expected, sut.Service.IsTransitionValid(from, to));
    }

    // ── SubmitAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitAsync_ValidDraft_TransitionsToSubmitted()
    {
        var sut = Build("submit_valid");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db);

        var result = await sut.Service.SubmitAsync(req.ID);

        Assert.Equal(PaStatus.Submitted, result.Status);
        Assert.NotNull(result.SubmittedAt);
        Assert.NotNull(result.DecisionDueDate);
    }

    [Fact]
    public async Task SubmitAsync_CalculatesDecisionDueDateFromPlanSla()
    {
        var clock = new FakeDateTimeProvider(); // 2026-05-04 12:00 UTC
        var sut = Build("submit_sla", clock: clock);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan()); // Routine = 14 days
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db);
        req.Priority = PaPriority.Routine;

        var result = await sut.Service.SubmitAsync(req.ID);

        var expectedDue = clock.UtcNow.AddDays(14);
        Assert.Equal(expectedDue.Date, result.DecisionDueDate!.Value.Date);
    }

    [Fact]
    public async Task SubmitAsync_WrongRole_ThrowsAuthorizationException()
    {
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("submit_wrongrole", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => sut.Service.SubmitAsync(req.ID));
    }

    [Fact]
    public async Task SubmitAsync_WrongStatus_ThrowsWorkflowTransitionException()
    {
        var sut = Build("submit_wrongstatus");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        await Assert.ThrowsAsync<WorkflowTransitionException>(
            () => sut.Service.SubmitAsync(req.ID));
    }

    [Fact]
    public async Task SubmitAsync_WritesAuditEntry()
    {
        var sut = Build("submit_audit");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db);

        await sut.Service.SubmitAsync(req.ID);

        sut.AuditMock.Verify(a => a.LogAsync(
            "PaRequest", req.ID.ToString(), "StatusChanged",
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── BeginReviewAsync — Appealed path (§8.7) ─────────────────────────────────

    [Fact]
    public async Task BeginReviewAsync_FromAppealed_TransitionsToUnderReview()
    {
        // Appealed → UnderReview: reviewer picks up an appealed request for secondary review (§8.7)
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("begin_review_from_appealed", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Appealed);

        var result = await sut.Service.BeginReviewAsync(req.ID);

        Assert.Equal(PaStatus.UnderReview, result.Status);
        Assert.Equal(reviewer.UserId, result.ReviewerUserID);
    }

    // ── ApproveAsync — Appealed path (§8.7) ──────────────────────────────────

    [Fact]
    public async Task ApproveAsync_FromAppealed_OverturnsDenial()
    {
        // Appealed → Approved: reviewer overturns the original denial (§8.7)
        var reviewer = FakeCurrentUserService.AsReviewer();
        var clock    = new FakeDateTimeProvider();
        var sut = Build("approve_from_appealed", user: reviewer, clock: clock);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Appealed);

        var decision = new ApprovalDecisionModel
        {
            ApprovedUnitsGranted   = 5,
            AuthorizationStartDate = clock.UtcNow.Date,
            AuthorizationEndDate   = clock.UtcNow.AddDays(90),
        };

        var result = await sut.Service.ApproveAsync(req.ID, decision);

        Assert.Equal(PaStatus.Approved, result.Status);
        Assert.Matches(@"^AUTH-\d{8}-[0-9A-F]{6}$", result.AuthorizationNumber);
        Assert.Equal(5, result.ApprovedUnitsGranted);
    }

    // ── DenyAsync — Appealed path (§8.7) ────────────────────────────────────

    [Fact]
    public async Task DenyAsync_FromAppealed_UpholdsDenial()
    {
        // Appealed → Denied: reviewer upholds the original denial (appeal dismissed, §8.7)
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("deny_from_appealed", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        await sut.Db.DenialReasons.AddAsync(new DenialReason
        {
            ID = 1, Code = "MED-NOT-NECESSARY", Description = "Not medically necessary",
            IsAppealable = true, AppealDeadlineDays = 30,
        });
        await sut.Db.SaveChangesAsync();
        var req = await SeedRequestAsync(sut.Db, PaStatus.Appealed);

        var result = await sut.Service.DenyAsync(req.ID, denialReasonId: 1, notes: "No new evidence presented.");

        Assert.Equal(PaStatus.Denied, result.Status);
        Assert.Equal(1, result.DenialReasonID);
        Assert.NotNull(result.DecisionRenderedAt);
    }

    // ── ApproveAsync ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_ValidDecision_GeneratesAuthorizationNumber()
    {
        var reviewer = FakeCurrentUserService.AsReviewer();
        var clock = new FakeDateTimeProvider();
        var sut = Build("approve_valid", user: reviewer, clock: clock);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.UnderReview);

        var decision = new ApprovalDecisionModel
        {
            ApprovedUnitsGranted   = 5,
            AuthorizationStartDate = clock.UtcNow.Date,
            AuthorizationEndDate   = clock.UtcNow.AddDays(90),
        };

        var result = await sut.Service.ApproveAsync(req.ID, decision);

        Assert.Equal(PaStatus.Approved, result.Status);
        Assert.NotNull(result.AuthorizationNumber);
        // BUG-01: format is AUTH-YYYYMMDD-XXXXXX (6 uppercase hex chars from CSPRNG)
        Assert.Matches(@"^AUTH-\d{8}-[0-9A-F]{6}$", result.AuthorizationNumber);
        Assert.Equal(5, result.ApprovedUnitsGranted);
    }

    [Fact]
    public async Task ApproveAsync_ZeroUnitsGranted_ThrowsBusinessRuleViolation()
    {
        var reviewer = FakeCurrentUserService.AsReviewer();
        var clock = new FakeDateTimeProvider();
        var sut = Build("approve_zerounits", user: reviewer, clock: clock);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.UnderReview);

        var decision = new ApprovalDecisionModel
        {
            ApprovedUnitsGranted   = 0,  // BR-012 violation
            AuthorizationStartDate = clock.UtcNow.Date,
            AuthorizationEndDate   = clock.UtcNow.AddDays(90),
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => sut.Service.ApproveAsync(req.ID, decision));
        Assert.Equal("BR-012", ex.RuleId);
    }

    [Fact]
    public async Task ApproveAsync_SendsNotificationToSpecialist()
    {
        var reviewer = FakeCurrentUserService.AsReviewer();
        var clock = new FakeDateTimeProvider();
        var sut = Build("approve_notif", user: reviewer, clock: clock);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.UnderReview, specialistId: "specialist-id");

        await sut.Service.ApproveAsync(req.ID, new ApprovalDecisionModel
        {
            ApprovedUnitsGranted   = 1,
            AuthorizationStartDate = clock.UtcNow.Date,
            AuthorizationEndDate   = clock.UtcNow.AddDays(30),
        });

        sut.NotifMock.Verify(n => n.CreateAsync(
            "specialist-id",
            req.ID,
            It.IsAny<string>(),
            NotificationType.DecisionRendered,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── DenyAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DenyAsync_ValidDenial_TransitionsToDenied()
    {
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("deny_valid", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        await sut.Db.DenialReasons.AddAsync(new DenialReason
        {
            ID = 1, Code = "MED-NOT-NECESSARY", Description = "Not medically necessary",
            IsAppealable = true, AppealDeadlineDays = 30,
        });
        await sut.Db.SaveChangesAsync();
        var req = await SeedRequestAsync(sut.Db, PaStatus.UnderReview);

        var result = await sut.Service.DenyAsync(req.ID, denialReasonId: 1, notes: "Insufficient evidence.");

        Assert.Equal(PaStatus.Denied, result.Status);
        Assert.Equal(1, result.DenialReasonID);
        Assert.NotNull(result.DecisionRenderedAt);
    }

    [Fact]
    public async Task DenyAsync_AppealableReason_SendsAppealWindowNotification()
    {
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("deny_appeal_notif", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        await sut.Db.DenialReasons.AddAsync(new DenialReason
        {
            ID = 1, Code = "MED-NOT-NECESSARY", Description = "Not medically necessary",
            IsAppealable = true, AppealDeadlineDays = 30,
        });
        await sut.Db.SaveChangesAsync();
        var req = await SeedRequestAsync(sut.Db, PaStatus.UnderReview, specialistId: "specialist-id");

        await sut.Service.DenyAsync(req.ID, 1, null);

        // Expect DecisionRendered + AppealWindow notifications
        sut.NotifMock.Verify(n => n.CreateAsync(
            "specialist-id", req.ID, It.IsAny<string>(),
            NotificationType.AppealWindow, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// PRD §13.3 key scenario: WorkflowService_Deny_NonAppealableReason_ShouldNotAllowAppeal.
    /// When a denial reason has IsAppealable = false, no AppealWindow notification
    /// must be queued — the Specialist must not receive a misleading appeal prompt.
    /// </summary>
    [Fact]
    public async Task DenyAsync_NonAppealableReason_ShouldNotSendAppealWindowNotification()
    {
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("deny_non_appealable", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        await sut.Db.DenialReasons.AddAsync(new DenialReason
        {
            ID = 1, Code = "COVERAGE-LAPSED", Description = "Coverage lapsed",
            IsAppealable = false, AppealDeadlineDays = 0,
        });
        await sut.Db.SaveChangesAsync();
        var req = await SeedRequestAsync(sut.Db, PaStatus.UnderReview, specialistId: "specialist-id");

        await sut.Service.DenyAsync(req.ID, denialReasonId: 1, notes: null);

        // DecisionRendered notification must still be sent
        sut.NotifMock.Verify(n => n.CreateAsync(
            "specialist-id", req.ID, It.IsAny<string>(),
            NotificationType.DecisionRendered, It.IsAny<CancellationToken>()),
            Times.Once);

        // AppealWindow notification must NOT be sent (§13.3 — non-appealable reason)
        sut.NotifMock.Verify(n => n.CreateAsync(
            It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(),
            NotificationType.AppealWindow, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── AppealAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AppealAsync_WithinDeadline_TransitionsToAppealed()
    {
        var decisionDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeDateTimeProvider { UtcNow = decisionDate.AddDays(15) }; // within 30-day window
        var sut = Build("appeal_valid", clock: clock);

        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        await sut.Db.DenialReasons.AddAsync(new DenialReason
        {
            ID = 1, Code = "MED-NOT-NECESSARY", Description = "Not medically necessary",
            IsAppealable = true, AppealDeadlineDays = 30,
        });
        await sut.Db.SaveChangesAsync();

        var req = await SeedRequestAsync(sut.Db, PaStatus.Denied);
        req.DecisionRenderedAt = decisionDate;
        req.DenialReasonID = 1;
        await sut.Db.SaveChangesAsync();

        var result = await sut.Service.AppealAsync(req.ID);

        Assert.Equal(PaStatus.Appealed, result.Status);
    }

    [Fact]
    public async Task AppealAsync_PastDeadline_ThrowsBusinessRuleViolation()
    {
        var decisionDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new FakeDateTimeProvider { UtcNow = decisionDate.AddDays(45) }; // past 30-day deadline
        var sut = Build("appeal_past", clock: clock);

        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        await sut.Db.DenialReasons.AddAsync(new DenialReason
        {
            ID = 1, Code = "MED-NOT-NECESSARY", Description = "Not medically necessary",
            IsAppealable = true, AppealDeadlineDays = 30,
        });
        await sut.Db.SaveChangesAsync();

        var req = await SeedRequestAsync(sut.Db, PaStatus.Denied);
        req.DecisionRenderedAt = decisionDate;
        req.DenialReasonID = 1;
        await sut.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => sut.Service.AppealAsync(req.ID));
        Assert.Equal("BR-008", ex.RuleId);
    }

    // ── WithdrawAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task WithdrawAsync_FromSubmitted_Succeeds()
    {
        var sut = Build("withdraw_submitted");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        var result = await sut.Service.WithdrawAsync(req.ID, "Patient requested cancellation.");

        Assert.Equal(PaStatus.Withdrawn, result.Status);
    }

    [Fact]
    public async Task WithdrawAsync_FromDraftStatus_ThrowsBusinessRuleViolation()
    {
        // BR-009: Draft cannot be withdrawn; it should be deleted (BUG-013 mitigation)
        var sut = Build("withdraw_draft");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Draft);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => sut.Service.WithdrawAsync(req.ID, "reason"));
        Assert.Equal("BR-009", ex.RuleId);
    }

    [Fact]
    public async Task WithdrawAsync_FromDeniedStatus_ThrowsBusinessRuleViolation()
    {
        var sut = Build("withdraw_denied");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        await sut.Db.DenialReasons.AddAsync(new DenialReason
        {
            ID = 1, Code = "TEST", Description = "Test",
            IsAppealable = false, AppealDeadlineDays = 0,
        });
        await sut.Db.SaveChangesAsync();
        var req = await SeedRequestAsync(sut.Db, PaStatus.Denied);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => sut.Service.WithdrawAsync(req.ID, "reason"));
        Assert.Equal("BR-009", ex.RuleId);
    }

    // ── ExpireAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExpireAsync_ApprovedRequest_TransitionsToExpired()
    {
        var sut = Build("expire_valid");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Approved, specialistId: "specialist-id");
        req.AuthorizationNumber = "AUTH-20260101-12345";
        req.ProviderID = "provider-id";
        await sut.Db.SaveChangesAsync();

        var result = await sut.Service.ExpireAsync(req.ID);

        Assert.Equal(PaStatus.Expired, result.Status);
    }

    [Fact]
    public async Task ExpireAsync_NonApprovedRequest_ThrowsWorkflowTransitionException()
    {
        var sut = Build("expire_notapproved");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        await Assert.ThrowsAsync<WorkflowTransitionException>(
            () => sut.Service.ExpireAsync(req.ID));
    }

    [Fact]
    public async Task ExpireAsync_SendsNotificationToBothSpecialistAndProvider()
    {
        var sut = Build("expire_notif");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Approved,
            specialistId: "specialist-id", providerId: "provider-id");
        req.AuthorizationNumber = "AUTH-20260101-12345";
        await sut.Db.SaveChangesAsync();

        await sut.Service.ExpireAsync(req.ID);

        sut.NotifMock.Verify(n => n.CreateAsync(
            "specialist-id", req.ID, It.IsAny<string>(),
            NotificationType.ExpirationAlert, It.IsAny<CancellationToken>()),
            Times.Once);
        sut.NotifMock.Verify(n => n.CreateAsync(
            "provider-id", req.ID, It.IsAny<string>(),
            NotificationType.ExpirationAlert, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// TEST-003 (§11): ExpireAsync must write the audit entry via
    /// IAuditService.LogAsSystemAsync (not LogAsync) so the audit record is attributed
    /// to the SYSTEM user and never produces an empty UserID in background-job context.
    /// </summary>
    [Fact]
    public async Task ExpireAsync_AuditEntry_UsesLogAsSystemAsync()
    {
        var sut = Build("expire_audit_system");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Approved,
            specialistId: "specialist-id");
        req.AuthorizationNumber = "AUTH-20260101-12345";
        await sut.Db.SaveChangesAsync();

        await sut.Service.ExpireAsync(req.ID);

        // Must use LogAsSystemAsync — not LogAsync — so there is no dependency on
        // ICurrentUserService (which is anonymous in background-job scope).
        sut.AuditMock.Verify(a => a.LogAsSystemAsync(
            "PaRequest", req.ID.ToString(), "Expired",
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // LogAsync must NOT be called for expiration (all other transitions use LogAsync).
        sut.AuditMock.Verify(a => a.LogAsync(
            "PaRequest", It.IsAny<string>(), "Expired",
            It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Inactive user ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitAsync_InactiveUser_ThrowsAuthorizationException()
    {
        var inactiveUser = FakeCurrentUserService.AsSpecialist();
        inactiveUser.IsActive = false;
        var sut = Build("submit_inactive", user: inactiveUser);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => sut.Service.SubmitAsync(req.ID));
    }

    // ── RequestAdditionalInfoAsync ─────────────────────────────────────────────

    [Fact]
    public async Task RequestAdditionalInfoAsync_FromSubmitted_TransitionsToPendingInfo()
    {
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("req_info_submitted", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        var result = await sut.Service.RequestAdditionalInfoAsync(req.ID, "Need lab results.");

        Assert.Equal(PaStatus.PendingInfo, result.Status);
    }

    [Fact]
    public async Task RequestAdditionalInfoAsync_WrongRole_ThrowsAuthorizationException()
    {
        // Only Reviewer/Admin may request additional info — Specialist is unauthorized
        var specialist = FakeCurrentUserService.AsSpecialist();
        var sut = Build("req_info_wrong_role", user: specialist);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => sut.Service.RequestAdditionalInfoAsync(req.ID, "Need info."));
    }

    [Fact]
    public async Task RequestAdditionalInfoAsync_WrongStatus_ThrowsWorkflowTransitionException()
    {
        // Can only request info from Submitted or UnderReview — not from Draft
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("req_info_wrong_status", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Draft);

        await Assert.ThrowsAsync<WorkflowTransitionException>(
            () => sut.Service.RequestAdditionalInfoAsync(req.ID, "Need info."));
    }

    // ── BeginReviewAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task BeginReviewAsync_FromSubmitted_TransitionsToUnderReview()
    {
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("begin_review_valid", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        var result = await sut.Service.BeginReviewAsync(req.ID);

        Assert.Equal(PaStatus.UnderReview, result.Status);
        Assert.Equal(reviewer.UserId, result.ReviewerUserID);
    }

    [Fact]
    public async Task BeginReviewAsync_WrongRole_ThrowsAuthorizationException()
    {
        // Only Reviewer/Admin may begin review
        var specialist = FakeCurrentUserService.AsSpecialist();
        var sut = Build("begin_review_wrong_role", user: specialist);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => sut.Service.BeginReviewAsync(req.ID));
    }

    [Fact]
    public async Task BeginReviewAsync_WrongStatus_ThrowsWorkflowTransitionException()
    {
        // Can only begin review from Submitted — not from Draft
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("begin_review_wrong_status", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Draft);

        await Assert.ThrowsAsync<WorkflowTransitionException>(
            () => sut.Service.BeginReviewAsync(req.ID));
    }

    [Fact]
    public async Task BeginReviewAsync_WritesAuditEntry()
    {
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("begin_review_audit", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        await sut.Service.BeginReviewAsync(req.ID);

        sut.AuditMock.Verify(a => a.LogAsync(
            "PaRequest", req.ID.ToString(), "StatusChanged",
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── ResubmitAsync (PendingInfo → Submitted) ───────────────────────────────

    [Fact]
    public async Task ResubmitAsync_FromPendingInfo_TransitionsToSubmitted()
    {
        // Specialist can resubmit a PendingInfo request per Transitions table
        var sut = Build("resubmit_valid");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.PendingInfo);

        // BR-013: ValidateResubmissionAsync needs a PaStatusHistory row marking
        // when the request entered PendingInfo (it compares comment.AuthoredAt > pendingAt).
        var pendingAt = sut.Clock.UtcNow.AddMinutes(-10);
        sut.Db.PaStatusHistories.Add(new PaStatusHistory
        {
            PaRequestID      = req.ID,
            FromStatus       = PaStatus.Submitted,
            ToStatus         = PaStatus.PendingInfo,
            ChangedByUserID  = "reviewer-id",
            ChangedAt        = pendingAt,
        });

        // Add a comment AFTER the PendingInfo transition (satisfies BR-013)
        sut.Db.PaComments.Add(new PaComment
        {
            PaRequestID      = req.ID,
            CommentText      = "Attached additional lab results as requested.",
            IsInternalOnly   = false,
            AuthoredByUserID = sut.User.UserId,
            AuthoredAt       = sut.Clock.UtcNow, // after pendingAt
        });
        await sut.Db.SaveChangesAsync();

        var result = await sut.Service.ResubmitAsync(req.ID);

        Assert.Equal(PaStatus.Submitted, result.Status);
        Assert.NotNull(result.SubmittedAt);
        Assert.NotNull(result.DecisionDueDate);
    }

    [Fact]
    public async Task ResubmitAsync_WrongStatus_ThrowsWorkflowTransitionException()
    {
        // Can only resubmit from PendingInfo, not from Submitted
        var sut = Build("resubmit_wrong_status");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        await Assert.ThrowsAsync<WorkflowTransitionException>(
            () => sut.Service.ResubmitAsync(req.ID));
    }

    [Fact]
    public async Task ResubmitAsync_WrongRole_ThrowsAuthorizationException()
    {
        // Only Specialist or Admin may resubmit — Reviewer is unauthorized
        var reviewer = FakeCurrentUserService.AsReviewer();
        var sut = Build("resubmit_wrong_role", user: reviewer);
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.PendingInfo);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => sut.Service.ResubmitAsync(req.ID));
    }

    [Fact]
    public async Task ResubmitAsync_NoNewContent_ThrowsBusinessRuleViolation()
    {
        // BR-013: must add a comment or document since the last submission
        var sut = Build("resubmit_br013");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.PendingInfo);
        // No comments added → BR-013 validation should fail

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => sut.Service.ResubmitAsync(req.ID));
    }

    /// <summary>
    /// BR-013 (§9): A new document (rather than a comment) also satisfies the
    /// resubmission requirement. Covers the document-only path not tested in
    /// <see cref="ResubmitAsync_FromPendingInfo_TransitionsToSubmitted"/>.
    /// </summary>
    [Fact]
    public async Task ResubmitAsync_WithNewDocumentOnly_TransitionsToSubmitted()
    {
        var sut = Build("resubmit_br013_doc");
        await sut.Db.InsurancePlans.AddAsync(DefaultPlan());
        await sut.Db.ProcedureCodes.AddAsync(DefaultProc());
        var req = await SeedRequestAsync(sut.Db, PaStatus.PendingInfo);

        var pendingAt = sut.Clock.UtcNow.AddMinutes(-10);
        sut.Db.PaStatusHistories.Add(new PaStatusHistory
        {
            PaRequestID     = req.ID,
            FromStatus      = PaStatus.Submitted,
            ToStatus        = PaStatus.PendingInfo,
            ChangedByUserID = "reviewer-id",
            ChangedAt       = pendingAt,
        });

        // Upload a document AFTER PendingInfo — no comment added.
        sut.Db.PaDocuments.Add(new PaDocument
        {
            PaRequestID      = req.ID,
            FileName         = "lab_results.pdf",
            DocumentType     = "LabResult",
            ContentType      = "application/pdf",
            FileSizeBytes    = 51_200,
            UploadedByUserID = sut.User.UserId,
            UploadedAt       = sut.Clock.UtcNow, // after pendingAt
        });
        await sut.Db.SaveChangesAsync();

        var result = await sut.Service.ResubmitAsync(req.ID);

        Assert.Equal(PaStatus.Submitted, result.Status);
    }

    // ── T-C5: AdminOverrideAsync ──────────────────────────────────────────────

    [Fact]
    public async Task AdminOverrideAsync_Admin_ForcesStatusAndWritesHistory()
    {
        // Happy path: Admin overrides a Submitted request to Approved
        var admin = FakeCurrentUserService.AsAdmin();
        var sut   = Build("override_happy", user: admin);
        var req   = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        var result = await sut.Service.AdminOverrideAsync(
            req.ID, PaStatus.Approved, "Emergency correction by admin.");

        Assert.Equal(PaStatus.Approved, result.Status);

        // Verify PaStatusHistory row created with [Admin Override] prefix
        var history = await sut.Db.PaStatusHistories
            .Where(h => h.PaRequestID == req.ID)
            .OrderByDescending(h => h.ChangedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(history);
        Assert.Equal(PaStatus.Submitted, history.FromStatus);
        Assert.Equal(PaStatus.Approved,  history.ToStatus);
        Assert.StartsWith("[Admin Override]", history.ChangeReason);

        // Verify AdminOverride audit action logged
        sut.AuditMock.Verify(a => a.LogAsync(
            "PaRequest", req.ID.ToString(), "AdminOverride",
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdminOverrideAsync_NonAdmin_ThrowsAuthorizationException()
    {
        // Only Administrators may use override
        var specialist = FakeCurrentUserService.AsSpecialist();
        var sut        = Build("override_non_admin", user: specialist);
        var req        = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => sut.Service.AdminOverrideAsync(req.ID, PaStatus.Approved, "should not work"));
    }

    [Fact]
    public async Task AdminOverrideAsync_EmptyJustification_ThrowsBusinessRuleViolation()
    {
        // Justification is required to ensure accountability
        var admin = FakeCurrentUserService.AsAdmin();
        var sut   = Build("override_no_justification", user: admin);
        var req   = await SeedRequestAsync(sut.Db, PaStatus.Submitted);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => sut.Service.AdminOverrideAsync(req.ID, PaStatus.Approved, "   "));
    }

    [Fact]
    public async Task AdminOverrideAsync_RequestNotFound_ThrowsEntityNotFoundException()
    {
        var admin = FakeCurrentUserService.AsAdmin();
        var sut   = Build("override_not_found", user: admin);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => sut.Service.AdminOverrideAsync(99_999, PaStatus.Withdrawn, "test"));
    }

    // ── GenerateAuthorizationNumber (BUG-01, ROADMAP_v3 §7) ──────────────────

    /// <summary>
    /// ROADMAP_v3 §7 test: GenerateAuthorizationNumber_IsUnique_UnderLoad.
    /// Verifies that the CSPRNG-based auth number generator (BUG-01 fix) produces
    /// correctly-formatted, non-repeating values when called repeatedly with the
    /// same UTC date (simulating burst approvals on the same calendar day).
    ///
    /// Statistical note: 100 draws from 2^24 ≈ 16.7M distinct suffixes gives
    /// P(any collision) ≈ 0.03% (birthday paradox). A collision almost certainly
    /// indicates regression to a predictable PRNG, not a genuine random coincidence.
    /// </summary>
    [Fact]
    public void GenerateAuthorizationNumber_IsUnique_UnderLoad()
    {
        // Access the private static helper via reflection.
        // It is a pure, dependency-free function — reflection is safe and
        // avoids the overhead of wiring up a full WorkflowService for each call.
        var method = typeof(WorkflowService).GetMethod(
            "GenerateAuthorizationNumber",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method); // Guards against accidental rename silently breaking the test

        var fixedDate = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var results   = new HashSet<string>(capacity: 100);

        for (var i = 0; i < 100; i++)
        {
            var authNum = (string)method!.Invoke(null, new object[] { fixedDate })!;

            // Format: AUTH-YYYYMMDD-XXXXXX (6 uppercase hex chars — CSPRNG suffix)
            Assert.Matches(@"^AUTH-20260505-[0-9A-F]{6}$", authNum);
            results.Add(authNum);
        }

        Assert.Equal(100, results.Count);
    }
}

