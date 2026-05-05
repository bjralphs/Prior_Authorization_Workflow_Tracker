using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;
using Tests.Unit.Fakes;

namespace Tests.Unit.Services;

/// <summary>
/// Unit tests for ExpirationJob (§8.6, FR-006, §13.1).
///
/// Strategy: mock IServiceScopeFactory so both GetExpiredRequestIdsAsync (IDbContextFactory)
/// and ExpireSingleAsync (IWorkflowService) are intercepted without needing real DI.
/// The InMemory database (named per-test) provides real EF query behaviour for the
/// candidate-ID lookup; IWorkflowService is always mocked for isolation.
/// </summary>
public class ExpirationJobTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppDbContext MakeDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(opts);
    }

    /// <summary>
    /// Builds an ExpirationJob where:
    ///  - GetExpiredRequestIdsAsync uses a real InMemory DbContext (named dbName).
    ///  - ExpireSingleAsync delegates to the returned IWorkflowService mock.
    /// </summary>
    private static (ExpirationJob Job, Mock<IWorkflowService> WorkflowMock, AppDbContext Db)
        Build(string dbName, FakeDateTimeProvider? clock = null)
    {
        clock ??= new FakeDateTimeProvider();
        var db = MakeDb(dbName);

        var dbFactory = new Mock<IDbContextFactory<AppDbContext>>();
        dbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(() => MakeDb(dbName));

        var workflowMock = new Mock<IWorkflowService>();
        // Default: ExpireAsync succeeds and returns an Expired request
        workflowMock
            .Setup(w => w.ExpireAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                new PaRequest { ID = id, Status = PaStatus.Expired });

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IDbContextFactory<AppDbContext>)))
            .Returns(dbFactory.Object);
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IWorkflowService)))
            .Returns(workflowMock.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var job = new ExpirationJob(
            scopeFactory.Object, clock, NullLogger<ExpirationJob>.Instance);

        return (job, workflowMock, db);
    }

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

    private static PaRequest ApprovedRequest(string requestNumber, DateTime authEndDate) => new()
    {
        RequestNumber          = requestNumber,
        PatientMrn             = "MRN001",
        PatientDob             = new DateTime(1970, 1, 1),
        InsurancePlanID        = 1,
        ProcedureCodeID        = 1,
        DiagnosisCode          = "M17.11",
        ClinicalJustification  = new string('x', 55),
        ApprovedUnitsRequested = 1,
        Status                 = PaStatus.Approved,
        Priority               = PaPriority.Routine,
        CreatedAt              = DateTime.UtcNow,
        UpdatedAt              = DateTime.UtcNow,
        AuthorizationEndDate   = authEndDate,
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TriggerAsync_NoExpiredRequests_ReturnsZero()
    {
        var (job, workflowMock, _) = Build("expjob_none");

        var count = await job.TriggerAsync();

        Assert.Equal(0, count);
        workflowMock.Verify(
            w => w.ExpireAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TriggerAsync_OneExpiredRequest_CallsExpireAndReturnsOne()
    {
        const string dbName = "expjob_one_expired";
        var clock = new FakeDateTimeProvider(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var (job, workflowMock, db) = Build(dbName, clock);

        db.InsurancePlans.Add(DefaultPlan());
        db.ProcedureCodes.Add(DefaultProc());
        // AuthorizationEndDate is yesterday (2026-05-31 < 2026-06-01)
        db.PaRequests.Add(ApprovedRequest("PA-2026-00001", new DateTime(2026, 5, 31)));
        await db.SaveChangesAsync();

        var count = await job.TriggerAsync();

        Assert.Equal(1, count);
        workflowMock.Verify(
            w => w.ExpireAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TriggerAsync_ActiveApprovedRequest_IsNotExpired()
    {
        const string dbName = "expjob_active";
        var clock = new FakeDateTimeProvider(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var (job, workflowMock, db) = Build(dbName, clock);

        db.InsurancePlans.Add(DefaultPlan());
        db.ProcedureCodes.Add(DefaultProc());
        // AuthorizationEndDate is today (not yet expired — condition is strict <)
        db.PaRequests.Add(ApprovedRequest("PA-2026-00002", new DateTime(2026, 6, 1)));
        await db.SaveChangesAsync();

        var count = await job.TriggerAsync();

        Assert.Equal(0, count);
        workflowMock.Verify(
            w => w.ExpireAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TriggerAsync_WorkflowTransitionException_SkipsAndReturnsZeroExpired()
    {
        // Simulates a race condition where a request was already expired between
        // the candidate-ID query and the actual ExpireAsync call (BUG-008 guard).
        const string dbName = "expjob_skip_already_expired";
        var clock = new FakeDateTimeProvider(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var (job, workflowMock, db) = Build(dbName, clock);

        db.InsurancePlans.Add(DefaultPlan());
        db.ProcedureCodes.Add(DefaultProc());
        db.PaRequests.Add(ApprovedRequest("PA-2026-00003", new DateTime(2026, 5, 31)));
        await db.SaveChangesAsync();

        // Workflow rejects because request is already Expired
        workflowMock
            .Setup(w => w.ExpireAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WorkflowTransitionException(PaStatus.Expired, PaStatus.Expired));

        // Must not throw — gracefully skips
        var count = await job.TriggerAsync();

        Assert.Equal(0, count); // expired=0 (skipped=1, the transition exception counts as skip)
    }

    [Fact]
    public async Task TriggerAsync_PerRequestFailure_ContinuesOtherRequests()
    {
        const string dbName = "expjob_partial_fail";
        var clock = new FakeDateTimeProvider(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var (job, workflowMock, db) = Build(dbName, clock);

        db.InsurancePlans.Add(DefaultPlan());
        db.ProcedureCodes.Add(DefaultProc());
        var req1 = ApprovedRequest("PA-2026-00010", new DateTime(2026, 5, 31));
        var req2 = ApprovedRequest("PA-2026-00011", new DateTime(2026, 5, 31));
        req2.PatientMrn = "MRN002"; // distinct MRN to avoid duplicate key issues
        db.PaRequests.AddRange(req1, req2);
        await db.SaveChangesAsync();

        // req1 fails with an unexpected exception
        workflowMock
            .Setup(w => w.ExpireAsync(req1.ID, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated transient failure"));

        // req2 succeeds (default mock returns Expired PaRequest)
        workflowMock
            .Setup(w => w.ExpireAsync(req2.ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaRequest { ID = req2.ID, Status = PaStatus.Expired });

        var count = await job.TriggerAsync();

        // One expired, one failed — job continues and counts only successes
        Assert.Equal(1, count);
        workflowMock.Verify(
            w => w.ExpireAsync(req2.ID, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
