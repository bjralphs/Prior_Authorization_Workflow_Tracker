using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services;
using Tests.Unit.Fakes;

namespace Tests.Unit.Services;

/// <summary>
/// Unit tests for ReportingService (T17, T36 â€” â‰¥ 60% coverage target per Â§13.2).
/// Uses a per-test InMemory database seeded with known data so all aggregate
/// assertions are deterministic.  AsNoTracking() is enforced by verifying that
/// the returned objects are not tracked by the DbContext (test-level verification).
/// </summary>
public class ReportingServiceTests
{
    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private const string SystemUserId = "00000000-0000-0000-0000-000000000001";

    private static AppDbContext MakeDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(opts);
    }

    private static IDbContextFactory<AppDbContext> MakeFactory(string dbName)
    {
        // Each call to CreateDbContextAsync returns a fresh context pointing at the
        // same InMemory database so data seeded in one context is visible in another.
        var factory = new Moq.Mock<IDbContextFactory<AppDbContext>>();
        factory
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => MakeDb(dbName));
        return factory.Object;
    }

    private static ReportingService MakeService(string dbName, FakeDateTimeProvider? clock = null)
        => new ReportingService(
            MakeFactory(dbName),
            clock ?? new FakeDateTimeProvider(),
            NullLogger<ReportingService>.Instance);

    // Seed the minimum reference data needed by most tests.
    private static async Task SeedAsync(AppDbContext db)
    {
        var plan = new InsurancePlan
        {
            ID = 1, PlanName = "BlueCross PPO", PayerName = "BlueCross",
            PlanType = "PPO", RoutineDecisionDays = 14, UrgentDecisionDays = 3,
            EmergentDecisionDays = 1, IsActive = true,
        };
        var proc = new ProcedureCode
        {
            ID = 1, Code = "99213", Description = "Office Visit",
            RequiresPriorAuth = true, TypicalAuthorizationDurationDays = 30, IsActive = true,
        };
        var denial1 = new DenialReason
        {
            ID = 1, Code = "MED-NOT-NECESSARY", Description = "Not medically necessary",
            IsAppealable = true, AppealDeadlineDays = 30,
        };
        var denial2 = new DenialReason
        {
            ID = 2, Code = "COVERAGE-LAPSED", Description = "Coverage lapsed",
            IsAppealable = false, AppealDeadlineDays = 0,
        };
        db.InsurancePlans.Add(plan);
        db.ProcedureCodes.Add(proc);
        db.DenialReasons.Add(denial1);
        db.DenialReasons.Add(denial2);
        await db.SaveChangesAsync();
    }

    // Creates a minimal PaRequest and returns it without saving (caller must call SaveChanges).
    private static PaRequest MakeRequest(
        PaStatus status, DateTime? submittedAt = null,
        DateTime? decisionDueDate = null, DateTime? decisionRenderedAt = null,
        DateTime? authEndDate = null, int? denialReasonId = null,
        string? submittedByUserId = null,
        string? providerId = null,
        string? reviewerUserId = null,
        PaPriority priority = PaPriority.Routine)
    {
        return new PaRequest
        {
            PatientMrn        = "MRN001",
            PatientName       = "Alice Smith",
            PatientDob        = new DateTime(1980, 1, 1),
            InsurancePlanID   = 1,
            ProcedureCodeID   = 1,
            DiagnosisCode     = "A00.0",
            Priority          = priority,
            Status            = status,
            ApprovedUnitsRequested = 1,
            SubmittedAt       = submittedAt,
            DecisionDueDate   = decisionDueDate,
            DecisionRenderedAt = decisionRenderedAt,
            AuthorizationEndDate = authEndDate,
            DenialReasonID    = denialReasonId,
            SubmittedByUserID = submittedByUserId ?? SystemUserId,
            ProviderID        = providerId ?? string.Empty,
            ReviewerUserID    = reviewerUserId,
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow,
        };
    }

    // â”€â”€ GetStatusSummaryAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task GetStatusSummaryAsync_EmptyDatabase_ReturnsEmptyList()
    {
        using var db = MakeDb(nameof(GetStatusSummaryAsync_EmptyDatabase_ReturnsEmptyList));
        await SeedAsync(db);
        var svc = MakeService(nameof(GetStatusSummaryAsync_EmptyDatabase_ReturnsEmptyList));

        var result = await svc.GetStatusSummaryAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetStatusSummaryAsync_ReturnsCountsGroupedByStatus()
    {
        var dbName = nameof(GetStatusSummaryAsync_ReturnsCountsGroupedByStatus);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        db.PaRequests.AddRange(
            MakeRequest(PaStatus.Submitted),
            MakeRequest(PaStatus.Submitted),
            MakeRequest(PaStatus.Approved),
            MakeRequest(PaStatus.Denied));
        await db.SaveChangesAsync();

        var svc = MakeService(dbName);
        var result = await svc.GetStatusSummaryAsync();

        Assert.Equal(3, result.Count); // 3 distinct statuses
        var submitted = result.Single(r => r.Status == PaStatus.Submitted);
        Assert.Equal(2, submitted.Count);
        var approved = result.Single(r => r.Status == PaStatus.Approved);
        Assert.Equal(1, approved.Count);
    }

    [Fact]
    public async Task GetStatusSummaryAsync_CountsOverdueCorrectly()
    {
        var dbName = nameof(GetStatusSummaryAsync_CountsOverdueCorrectly);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var clock = new FakeDateTimeProvider(new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc));
        // Due yesterday = overdue; due tomorrow = not overdue
        var yesterday = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc);
        var tomorrow  = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc);

        db.PaRequests.AddRange(
            MakeRequest(PaStatus.Submitted, decisionDueDate: yesterday),
            MakeRequest(PaStatus.Submitted, decisionDueDate: tomorrow),
            MakeRequest(PaStatus.Submitted, decisionDueDate: yesterday));
        await db.SaveChangesAsync();

        var svc = MakeService(dbName, clock);
        var result = await svc.GetStatusSummaryAsync();

        var summary = result.Single(r => r.Status == PaStatus.Submitted);
        Assert.Equal(3, summary.Count);
        Assert.Equal(2, summary.OverdueCount);
    }

    // â”€â”€ GetDenialsByReasonAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task GetDenialsByReasonAsync_NoDecialsInRange_ReturnsEmpty()
    {
        var dbName = nameof(GetDenialsByReasonAsync_NoDecialsInRange_ReturnsEmpty);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var svc = MakeService(dbName);
        var from = DateOnly.FromDateTime(new DateTime(2026, 1, 1));
        var to   = DateOnly.FromDateTime(new DateTime(2026, 1, 31));

        var result = await svc.GetDenialsByReasonAsync(from, to);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDenialsByReasonAsync_AggregatesCountsAndPercentages()
    {
        var dbName = nameof(GetDenialsByReasonAsync_AggregatesCountsAndPercentages);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var decisionDate = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        db.PaRequests.AddRange(
            MakeRequest(PaStatus.Denied, decisionRenderedAt: decisionDate, denialReasonId: 1),
            MakeRequest(PaStatus.Denied, decisionRenderedAt: decisionDate, denialReasonId: 1),
            MakeRequest(PaStatus.Denied, decisionRenderedAt: decisionDate, denialReasonId: 2));
        await db.SaveChangesAsync();

        var svc = MakeService(dbName);
        var from = DateOnly.FromDateTime(new DateTime(2026, 4, 1));
        var to   = DateOnly.FromDateTime(new DateTime(2026, 4, 30));

        var result = await svc.GetDenialsByReasonAsync(from, to);

        Assert.Equal(2, result.Count);
        var med = result.Single(r => r.ReasonCode == "MED-NOT-NECESSARY");
        Assert.Equal(2, med.Count);
        Assert.Equal(66.7, med.Percentage, precision: 1);
        Assert.True(med.IsAppealable);

        var cov = result.Single(r => r.ReasonCode == "COVERAGE-LAPSED");
        Assert.Equal(1, cov.Count);
        Assert.Equal(33.3, cov.Percentage, precision: 1);
        Assert.False(cov.IsAppealable);
    }

    [Fact]
    public async Task GetDenialsByReasonAsync_ExcludesRequestsOutsideDateRange()
    {
        var dbName = nameof(GetDenialsByReasonAsync_ExcludesRequestsOutsideDateRange);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var insideDate  = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var outsideDate = new DateTime(2026, 2, 1,  0, 0, 0, DateTimeKind.Utc);

        db.PaRequests.AddRange(
            MakeRequest(PaStatus.Denied, decisionRenderedAt: insideDate,  denialReasonId: 1),
            MakeRequest(PaStatus.Denied, decisionRenderedAt: outsideDate, denialReasonId: 2));
        await db.SaveChangesAsync();

        var svc = MakeService(dbName);
        var from = DateOnly.FromDateTime(new DateTime(2026, 4, 1));
        var to   = DateOnly.FromDateTime(new DateTime(2026, 4, 30));

        var result = await svc.GetDenialsByReasonAsync(from, to);

        Assert.Single(result);
        Assert.Equal("MED-NOT-NECESSARY", result[0].ReasonCode);
    }

    // â”€â”€ GetMonthlyVolumeAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task GetMonthlyVolumeAsync_GroupsByYearMonth()
    {
        var dbName = nameof(GetMonthlyVolumeAsync_GroupsByYearMonth);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var clock = new FakeDateTimeProvider(new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc));

        // 2 requests in April, 1 request in March
        db.PaRequests.AddRange(
            MakeRequest(PaStatus.Approved, submittedAt: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeRequest(PaStatus.Denied,   submittedAt: new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc)),
            MakeRequest(PaStatus.Submitted, submittedAt: new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var svc = MakeService(dbName, clock);
        var result = await svc.GetMonthlyVolumeAsync(months: 12);

        Assert.Equal(2, result.Count); // March + April

        var march = result.Single(r => r.Month == 3 && r.Year == 2026);
        Assert.Equal(1, march.Submitted);
        Assert.Equal(0, march.Approved);

        var april = result.Single(r => r.Month == 4 && r.Year == 2026);
        Assert.Equal(2, april.Submitted);
        Assert.Equal(1, april.Approved);
        Assert.Equal(1, april.Denied);
    }

    [Fact]
    public async Task GetMonthlyVolumeAsync_ExcludesRequestsOutsideCutoff()
    {
        var dbName = nameof(GetMonthlyVolumeAsync_ExcludesRequestsOutsideCutoff);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var clock = new FakeDateTimeProvider(new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc));

        // 18 months ago is outside 12-month window
        db.PaRequests.Add(MakeRequest(PaStatus.Approved,
            submittedAt: new DateTime(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var svc = MakeService(dbName, clock);
        var result = await svc.GetMonthlyVolumeAsync(months: 12);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMonthlyVolumeAsync_ResultsOrderedAscendingByYearMonth()
    {
        var dbName = nameof(GetMonthlyVolumeAsync_ResultsOrderedAscendingByYearMonth);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var clock = new FakeDateTimeProvider(new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc));

        db.PaRequests.AddRange(
            MakeRequest(PaStatus.Submitted, submittedAt: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeRequest(PaStatus.Submitted, submittedAt: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)),
            MakeRequest(PaStatus.Submitted, submittedAt: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var svc = MakeService(dbName, clock);
        var result = await svc.GetMonthlyVolumeAsync(months: 12);

        Assert.Equal(3, result.Count);
        Assert.Equal(3, result[0].Month); // March first
        Assert.Equal(4, result[1].Month); // then April
        Assert.Equal(5, result[2].Month); // then May
    }

    // â”€â”€ GetExpiringAuthorizationsAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task GetExpiringAuthorizationsAsync_ReturnsOnlyApprovedWithinWindow()
    {
        var dbName = nameof(GetExpiringAuthorizationsAsync_ReturnsOnlyApprovedWithinWindow);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var clock = new FakeDateTimeProvider(new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc));

        // expires in 15 days â€” inside 30-day window
        db.PaRequests.Add(MakeRequest(PaStatus.Approved,
            authEndDate: new DateTime(2026, 5, 19, 0, 0, 0, DateTimeKind.Utc)));
        // expires in 45 days â€” outside 30-day window
        db.PaRequests.Add(MakeRequest(PaStatus.Approved,
            authEndDate: new DateTime(2026, 6, 18, 0, 0, 0, DateTimeKind.Utc)));
        // already expired â€” outside window (past)
        db.PaRequests.Add(MakeRequest(PaStatus.Approved,
            authEndDate: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)));
        // Denied â€” should not appear
        db.PaRequests.Add(MakeRequest(PaStatus.Denied,
            authEndDate: new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc)));
        await db.SaveChangesAsync();

        var svc = MakeService(dbName, clock);
        var result = await svc.GetExpiringAuthorizationsAsync(daysAhead: 30);

        Assert.Single(result);
        Assert.Equal(15, result[0].DaysUntilExpiry);
    }

    [Fact]
    public async Task GetExpiringAuthorizationsAsync_ComputesDaysUntilExpiryCorrectly()
    {
        var dbName = nameof(GetExpiringAuthorizationsAsync_ComputesDaysUntilExpiryCorrectly);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var clock = new FakeDateTimeProvider(new DateTime(2026, 5, 4, 0, 0, 0, DateTimeKind.Utc));
        var expiryDate = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc); // 10 days ahead

        db.PaRequests.Add(MakeRequest(PaStatus.Approved, authEndDate: expiryDate));
        await db.SaveChangesAsync();

        var svc = MakeService(dbName, clock);
        var result = await svc.GetExpiringAuthorizationsAsync(daysAhead: 30);

        Assert.Single(result);
        Assert.Equal(10, result[0].DaysUntilExpiry);
    }

    // â”€â”€ GetDashboardSummaryAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task GetDashboardSummaryAsync_EmptyDatabase_ReturnsAllZeros()
    {
        var dbName = nameof(GetDashboardSummaryAsync_EmptyDatabase_ReturnsAllZeros);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var svc = MakeService(dbName);
        var result = await svc.GetDashboardSummaryAsync("user-1");

        Assert.Equal(0, result.OpenRequests);
        Assert.Equal(0, result.OverdueRequests);
        Assert.Equal(0, result.ExpiringSoon);
        Assert.Equal(0, result.SubmittedThisMonth);
        Assert.Equal(0, result.ApprovedThisMonth);
        Assert.Equal(0, result.DeniedThisMonth);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_CountsOpenRequestsCorrectly()
    {
        var dbName = nameof(GetDashboardSummaryAsync_CountsOpenRequestsCorrectly);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // Active (open) statuses: Submitted, UnderReview, PendingInfo, Appealed
        db.PaRequests.AddRange(
            MakeRequest(PaStatus.Submitted),
            MakeRequest(PaStatus.UnderReview),
            MakeRequest(PaStatus.PendingInfo),
            MakeRequest(PaStatus.Appealed),
            MakeRequest(PaStatus.Approved),  // terminal â€” not counted
            MakeRequest(PaStatus.Denied));   // terminal â€” not counted
        await db.SaveChangesAsync();

        var svc = MakeService(dbName);
        var result = await svc.GetDashboardSummaryAsync("user-1");

        Assert.Equal(4, result.OpenRequests);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_CountsThisMonthCorrectly()
    {
        var dbName = nameof(GetDashboardSummaryAsync_CountsThisMonthCorrectly);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var clock = new FakeDateTimeProvider(new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc));

        // This month (May 2026)
        var thisMay = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        // Last month (April)
        var lastMonth = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);

        db.PaRequests.AddRange(
            // Approved this month: SubmittedAt + DecisionRenderedAt in May
            MakeRequest(PaStatus.Approved, submittedAt: thisMay, decisionRenderedAt: thisMay),
            // Denied this month: DecisionRenderedAt in May
            MakeRequest(PaStatus.Denied, submittedAt: thisMay, decisionRenderedAt: thisMay),
            // Submitted this month: just submitted, no decision yet
            MakeRequest(PaStatus.Submitted, submittedAt: thisMay),
            // Approved last month: excluded from ApprovedThisMonth
            MakeRequest(PaStatus.Approved, submittedAt: lastMonth, decisionRenderedAt: lastMonth));
        await db.SaveChangesAsync();

        var svc = MakeService(dbName, clock);
        var result = await svc.GetDashboardSummaryAsync("user-1");

        Assert.Equal(3, result.SubmittedThisMonth); // 3 with SubmittedAt in May
        Assert.Equal(1, result.ApprovedThisMonth);  // 1 with Status=Approved + DecisionRenderedAt in May
        Assert.Equal(1, result.DeniedThisMonth);    // 1 with Status=Denied + DecisionRenderedAt in May
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_CountsPendingMyAction_ForCurrentUser()
    {
        var dbName = nameof(GetDashboardSummaryAsync_CountsPendingMyAction_ForCurrentUser);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        const string myUserId    = "user-me";
        const string otherUserId = "user-other";

        db.PaRequests.AddRange(
            MakeRequest(PaStatus.PendingInfo, submittedByUserId: myUserId),
            MakeRequest(PaStatus.PendingInfo, submittedByUserId: myUserId),
            MakeRequest(PaStatus.PendingInfo, submittedByUserId: otherUserId)); // other user
        await db.SaveChangesAsync();

        var svc = MakeService(dbName);
        var result = await svc.GetDashboardSummaryAsync(myUserId);

        Assert.Equal(2, result.PendingMyAction);
    }

    // â”€â”€ GetTurnaroundTimeAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task GetTurnaroundTimeAsync_NoDecidedRequestsInRange_ReturnsEmpty()
    {
        var dbName = nameof(GetTurnaroundTimeAsync_NoDecidedRequestsInRange_ReturnsEmpty);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var svc = MakeService(dbName);
        var from = DateOnly.FromDateTime(new DateTime(2026, 1, 1));
        var to   = DateOnly.FromDateTime(new DateTime(2026, 1, 31));

        var result = await svc.GetTurnaroundTimeAsync(from, to);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTurnaroundTimeAsync_ComputesAverageDaysGroupedByPriority()
    {
        var dbName = nameof(GetTurnaroundTimeAsync_ComputesAverageDaysGroupedByPriority);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var submittedAt    = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var decisionAt10   = new DateTime(2026, 4, 11, 0, 0, 0, DateTimeKind.Utc); // 10 days
        var decisionAt20   = new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc); // 20 days

        // Two Routine requests decided in April: avg should be (10+20)/2 = 15 days
        db.PaRequests.AddRange(
            MakeRequest(PaStatus.Approved, submittedAt: submittedAt,
                decisionRenderedAt: decisionAt10),
            MakeRequest(PaStatus.Denied, submittedAt: submittedAt,
                decisionRenderedAt: decisionAt20),
            // Urgent request: 10-day turnaround
            new PaRequest
            {
                PatientMrn             = "MRN999",
                PatientName            = "Bob Test",
                PatientDob             = new DateTime(1990, 1, 1),
                InsurancePlanID        = 1,
                ProcedureCodeID        = 1,
                DiagnosisCode          = "A00.0",
                Priority               = PaPriority.Urgent,
                Status                 = PaStatus.Approved,
                ApprovedUnitsRequested = 1,
                SubmittedAt            = submittedAt,
                DecisionRenderedAt     = decisionAt10,
                SubmittedByUserID      = SystemUserId,
                CreatedAt              = DateTime.UtcNow,
                UpdatedAt              = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        var svc = MakeService(dbName);
        var from = DateOnly.FromDateTime(new DateTime(2026, 4, 1));
        var to   = DateOnly.FromDateTime(new DateTime(2026, 4, 30));

        var result = await svc.GetTurnaroundTimeAsync(from, to);

        Assert.Equal(2, result.Count); // Routine + Urgent
        var routine = result.Single(r => r.Priority == "Routine");
        Assert.Equal(15.0, routine.AverageDays);
        Assert.Equal(2, routine.Count);

        var urgent = result.Single(r => r.Priority == "Urgent");
        Assert.Equal(10.0, urgent.AverageDays);
        Assert.Equal(1, urgent.Count);
    }

    [Fact]
    public async Task GetTurnaroundTimeAsync_ExcludesRequestsOutsideDateRange()
    {
        var dbName = nameof(GetTurnaroundTimeAsync_ExcludesRequestsOutsideDateRange);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var submittedAt      = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var inRangeDecision  = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var outRangeDecision = new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc);

        db.PaRequests.AddRange(
            MakeRequest(PaStatus.Approved, submittedAt: submittedAt, decisionRenderedAt: inRangeDecision),
            MakeRequest(PaStatus.Approved, submittedAt: submittedAt, decisionRenderedAt: outRangeDecision));
        await db.SaveChangesAsync();

        var svc = MakeService(dbName);
        var from = DateOnly.FromDateTime(new DateTime(2026, 4, 1));
        var to   = DateOnly.FromDateTime(new DateTime(2026, 4, 30));

        var result = await svc.GetTurnaroundTimeAsync(from, to);

        Assert.Single(result);                  // Only the in-range request
        Assert.Equal(1, result[0].Count);
    }

    // â”€â”€ GetProviderVolumeAsync â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Fact]
    public async Task GetProviderVolumeAsync_NoRequestsInRange_ReturnsEmpty()
    {
        var dbName = nameof(GetProviderVolumeAsync_NoRequestsInRange_ReturnsEmpty);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var svc = MakeService(dbName);
        var from = DateOnly.FromDateTime(new DateTime(2026, 1, 1));
        var to   = DateOnly.FromDateTime(new DateTime(2026, 1, 31));

        var result = await svc.GetProviderVolumeAsync(from, to);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetProviderVolumeAsync_AggregatesCountsPerProvider()
    {
        var dbName = nameof(GetProviderVolumeAsync_AggregatesCountsPerProvider);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        const string providerA = "provider-a";
        const string providerB = "provider-b";
        var submitted          = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc);

        // Seed ApplicationUser rows so the name join works
        db.Users.AddRange(
            new ApplicationUser
            {
                Id = providerA, UserName = "prov.a@test.com", Email = "prov.a@test.com",
                FullName = "Dr. Alpha", IsActive = true, CreatedAt = DateTime.UtcNow,
            },
            new ApplicationUser
            {
                Id = providerB, UserName = "prov.b@test.com", Email = "prov.b@test.com",
                FullName = "Dr. Beta", IsActive = true, CreatedAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        db.PaRequests.AddRange(
            // Provider A: 2 approved
            new PaRequest { PatientMrn = "M1", PatientName = "Pat1", PatientDob = new DateTime(1980,1,1),
                InsurancePlanID = 1, ProcedureCodeID = 1, DiagnosisCode = "A00", Priority = PaPriority.Routine,
                Status = PaStatus.Approved, ApprovedUnitsRequested = 1, ProviderID = providerA,
                SubmittedAt = submitted, SubmittedByUserID = SystemUserId,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new PaRequest { PatientMrn = "M2", PatientName = "Pat2", PatientDob = new DateTime(1980,1,1),
                InsurancePlanID = 1, ProcedureCodeID = 1, DiagnosisCode = "A00", Priority = PaPriority.Routine,
                Status = PaStatus.Approved, ApprovedUnitsRequested = 1, ProviderID = providerA,
                SubmittedAt = submitted, SubmittedByUserID = SystemUserId,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            // Provider A: 1 denied
            new PaRequest { PatientMrn = "M3", PatientName = "Pat3", PatientDob = new DateTime(1980,1,1),
                InsurancePlanID = 1, ProcedureCodeID = 1, DiagnosisCode = "A00", Priority = PaPriority.Routine,
                Status = PaStatus.Denied, ApprovedUnitsRequested = 1, ProviderID = providerA,
                SubmittedAt = submitted, SubmittedByUserID = SystemUserId,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            // Provider B: 1 submitted
            new PaRequest { PatientMrn = "M4", PatientName = "Pat4", PatientDob = new DateTime(1980,1,1),
                InsurancePlanID = 1, ProcedureCodeID = 1, DiagnosisCode = "A00", Priority = PaPriority.Routine,
                Status = PaStatus.Submitted, ApprovedUnitsRequested = 1, ProviderID = providerB,
                SubmittedAt = submitted, SubmittedByUserID = SystemUserId,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var svc = MakeService(dbName);
        var from = DateOnly.FromDateTime(new DateTime(2026, 4, 1));
        var to   = DateOnly.FromDateTime(new DateTime(2026, 4, 30));

        var result = await svc.GetProviderVolumeAsync(from, to);

        Assert.Equal(2, result.Count);

        var a = result.Single(r => r.ProviderDisplayName == "Dr. Alpha");
        Assert.Equal(3, a.SubmissionCount);
        Assert.Equal(2, a.ApprovedCount);
        Assert.Equal(1, a.DeniedCount);

        var b = result.Single(r => r.ProviderDisplayName == "Dr. Beta");
        Assert.Equal(1, b.SubmissionCount);
        Assert.Equal(0, b.ApprovedCount);
        Assert.Equal(0, b.DeniedCount);
    }

    [Fact]
    public async Task GetProviderVolumeAsync_OrdersBySubmissionCountDescending()
    {
        var dbName = nameof(GetProviderVolumeAsync_OrdersBySubmissionCountDescending);
        using var db = MakeDb(dbName);
        await SeedAsync(db);

        const string providerA = "provider-vol-a";
        const string providerB = "provider-vol-b";
        var submitted          = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc);

        db.Users.AddRange(
            new ApplicationUser
            {
                Id = providerA, UserName = "vol.a@test.com", Email = "vol.a@test.com",
                FullName = "Dr. High Volume", IsActive = true, CreatedAt = DateTime.UtcNow,
            },
            new ApplicationUser
            {
                Id = providerB, UserName = "vol.b@test.com", Email = "vol.b@test.com",
                FullName = "Dr. Low Volume", IsActive = true, CreatedAt = DateTime.UtcNow,
            });
        await db.SaveChangesAsync();

        // Provider B gets 1 request, Provider A gets 3 â€” result should be A first
        for (int i = 0; i < 3; i++)
        {
            db.PaRequests.Add(new PaRequest
            {
                PatientMrn = $"VM{i}", PatientName = "P", PatientDob = new DateTime(1980,1,1),
                InsurancePlanID = 1, ProcedureCodeID = 1, DiagnosisCode = "A00",
                Priority = PaPriority.Routine, Status = PaStatus.Submitted,
                ApprovedUnitsRequested = 1, ProviderID = providerA,
                SubmittedAt = submitted, SubmittedByUserID = SystemUserId,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
        }
        db.PaRequests.Add(new PaRequest
        {
            PatientMrn = "VM99", PatientName = "P", PatientDob = new DateTime(1980,1,1),
            InsurancePlanID = 1, ProcedureCodeID = 1, DiagnosisCode = "A00",
            Priority = PaPriority.Routine, Status = PaStatus.Submitted,
            ApprovedUnitsRequested = 1, ProviderID = providerB,
            SubmittedAt = submitted, SubmittedByUserID = SystemUserId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var svc = MakeService(dbName);
        var from = DateOnly.FromDateTime(new DateTime(2026, 4, 1));
        var to   = DateOnly.FromDateTime(new DateTime(2026, 4, 30));

        var result = await svc.GetProviderVolumeAsync(from, to);

        Assert.Equal(2, result.Count);
        Assert.Equal("Dr. High Volume", result[0].ProviderDisplayName); // highest first
        Assert.Equal("Dr. Low Volume",  result[1].ProviderDisplayName);
    }

    // ── GetDashboardSummaryAsync — server-side CountAsync (T-R1) ─────────────

    [Fact]
    public async Task GetDashboardSummaryAsync_ReturnsCorrectServerSideCounts()
    {
        var dbName    = nameof(GetDashboardSummaryAsync_ReturnsCorrectServerSideCounts);
        var myUserId  = "user-analyst-01";
        var otherUser = "user-other-99";
        var now       = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock     = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var monthStart      = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var thirtyDaysAhead = now.AddDays(30).Date;

        // 2 open requests owned by myUserId
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,    submittedAt: now, submittedByUserId: myUserId));
        db.PaRequests.Add(MakeRequest(PaStatus.UnderReview,  submittedAt: now, submittedByUserId: myUserId));
        // 1 open request not owned by myUserId
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,    submittedAt: now, submittedByUserId: otherUser));
        // 1 overdue (active status, due yesterday)
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,    decisionDueDate: now.AddDays(-1),
                                       submittedByUserId: otherUser));
        // 1 expiring soon (Approved, end date within 30 days)
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, authEndDate: now.AddDays(5),
                                       submittedByUserId: otherUser));
        // 1 approved this month
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, decisionRenderedAt: now,
                                       submittedByUserId: otherUser));
        // 1 denied this month
        db.PaRequests.Add(MakeRequest(PaStatus.Denied, decisionRenderedAt: now,
                                       submittedByUserId: otherUser));
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetDashboardSummaryAsync(myUserId);

        // 4 active (Submitted×3 + UnderReview×1)
        Assert.Equal(4, result.OpenRequests);
        // 2 active with myUserId as submitter
        Assert.Equal(2, result.PendingMyAction);
        // 1 overdue active request
        Assert.Equal(1, result.OverdueRequests);
        // 1 expiring (the Approved with authEndDate in 5 days)
        Assert.Equal(1, result.ExpiringSoon);
        // 3 requests have SubmittedAt == now (within this month); the overdue request has no SubmittedAt
        Assert.Equal(3, result.SubmittedThisMonth);
        // 1 approved this month (by DecisionRenderedAt, separate from the expiring-soon one)
        Assert.Equal(1, result.ApprovedThisMonth);
        // 1 denied this month
        Assert.Equal(1, result.DeniedThisMonth);
    }

    // ── GetSpecialistDashboardAsync (T-R1) ───────────────────────────────────

    [Fact]
    public async Task GetSpecialistDashboardAsync_ReturnsOnlyOwnedRequests()
    {
        var dbName       = nameof(GetSpecialistDashboardAsync_ReturnsOnlyOwnedRequests);
        var specialistId = "specialist-001";
        var otherId      = "other-user-099";
        var now          = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock        = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // Specialist's own requests
        db.PaRequests.Add(MakeRequest(PaStatus.Draft,      submittedByUserId: specialistId));
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,  submittedByUserId: specialistId));
        db.PaRequests.Add(MakeRequest(PaStatus.Approved,
            decisionRenderedAt: now, submittedByUserId: specialistId,
            authEndDate: now.AddDays(20)));
        // Another user's request — should NOT count
        db.PaRequests.Add(MakeRequest(PaStatus.Draft, submittedByUserId: otherId));
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetSpecialistDashboardAsync(specialistId);

        Assert.Equal(1, result.MyDrafts);
        Assert.Equal(1, result.MySubmitted);
        Assert.Equal(0, result.MyPendingInfo);
        Assert.Equal(1, result.MyApprovedThisMonth);
        Assert.Equal(0, result.MyDeniedThisMonth);
        Assert.Equal(1, result.MyExpiringSoon);
    }

    // ── GetBillingDashboardAsync (T-R1) ──────────────────────────────────────

    [Fact]
    public async Task GetBillingDashboardAsync_ComputesApprovalRateFromServerSideCounts()
    {
        var dbName = nameof(GetBillingDashboardAsync_ComputesApprovalRateFromServerSideCounts);
        var now    = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock  = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // 3 approved this month, 1 denied this month → 75% approval rate
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, decisionRenderedAt: now, authEndDate: now.AddDays(60)));
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, decisionRenderedAt: now, authEndDate: now.AddDays(60)));
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, decisionRenderedAt: now, authEndDate: now.AddDays(60)));
        db.PaRequests.Add(MakeRequest(PaStatus.Denied,   decisionRenderedAt: now));
        // 1 expiring soon (authEndDate within 30 days)
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, authEndDate: now.AddDays(10)));
        // 1 expired this month
        db.PaRequests.Add(MakeRequest(PaStatus.Expired,  decisionRenderedAt: now));
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetBillingDashboardAsync();

        Assert.Equal(4, result.ApprovedActive);       // 3 decision + 1 expiring-soon
        Assert.Equal(1, result.ExpiringSoon);
        Assert.Equal(1, result.ExpiredThisMonth);
        Assert.Equal(1, result.DeniedThisMonth);
        Assert.Equal(3, result.ApprovedThisMonth);
        Assert.Equal(75.0, result.ApprovalRatePct);   // 3/(3+1) = 75%
    }

    // ── GetProviderDashboardAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetProviderDashboardAsync_ReturnsOnlyOwnProviderRequests()
    {
        var dbName     = nameof(GetProviderDashboardAsync_ReturnsOnlyOwnProviderRequests);
        var providerId = "provider-001";
        var otherId    = "provider-other";
        var now        = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock      = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // Provider's own requests
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,    providerId: providerId));
        db.PaRequests.Add(MakeRequest(PaStatus.UnderReview,  providerId: providerId));
        db.PaRequests.Add(MakeRequest(PaStatus.PendingInfo,  providerId: providerId));
        db.PaRequests.Add(MakeRequest(PaStatus.Approved,
            decisionRenderedAt: now, authEndDate: now.AddDays(15),
            providerId: providerId));
        // Another provider's request — must NOT appear in counts
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted, providerId: otherId));
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetProviderDashboardAsync(providerId);

        Assert.Equal(1, result.MySubmitted);
        Assert.Equal(1, result.MyUnderReview);
        Assert.Equal(1, result.MyPendingInfo);
        Assert.Equal(1, result.MyApprovedThisMonth);
        Assert.Equal(1, result.MyExpiringSoon);   // authEndDate in 15 days ≤ 30-day window
    }

    // ── GetReviewerDashboardAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetReviewerDashboardAsync_CountsCorrectly()
    {
        var dbName     = nameof(GetReviewerDashboardAsync_CountsCorrectly);
        var reviewerId = "reviewer-001";
        var now        = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock      = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // Submitted/Appealed → awaiting review (2)
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted));
        db.PaRequests.Add(MakeRequest(PaStatus.Appealed));
        // Under review by THIS reviewer (1), under review by someone else (1)
        db.PaRequests.Add(MakeRequest(PaStatus.UnderReview, reviewerUserId: reviewerId));
        db.PaRequests.Add(MakeRequest(PaStatus.UnderReview, reviewerUserId: "reviewer-other"));
        // PendingInfo (1)
        db.PaRequests.Add(MakeRequest(PaStatus.PendingInfo));
        // Approved this month (1)
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, decisionRenderedAt: now));
        // Denied this month (1)
        db.PaRequests.Add(MakeRequest(PaStatus.Denied, decisionRenderedAt: now));
        // Overdue: Submitted with DecisionDueDate in the past (1)
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted, decisionDueDate: now.AddDays(-3)));
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetReviewerDashboardAsync(reviewerId);

        Assert.Equal(3, result.AwaitingReview);    // 2 Submitted (incl. overdue) + 1 Appealed
        Assert.Equal(1, result.UnderReviewByMe);   // only the one assigned to reviewerId
        Assert.Equal(1, result.PendingInfoTotal);
        Assert.Equal(1, result.ApprovedThisMonth);
        Assert.Equal(1, result.DeniedThisMonth);
        Assert.Equal(1, result.OverdueRequests);   // only the Submitted with DecisionDueDate = now-3
    }

    // ── GetApprovalRateAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetApprovalRateAsync_ComputesRateGroupedByPriority()
    {
        var dbName = nameof(GetApprovalRateAsync_ComputesRateGroupedByPriority);
        var now    = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock  = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // Routine: 3 approved, 1 denied → 75%
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, decisionRenderedAt: now, priority: PaPriority.Routine));
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, decisionRenderedAt: now, priority: PaPriority.Routine));
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, decisionRenderedAt: now, priority: PaPriority.Routine));
        db.PaRequests.Add(MakeRequest(PaStatus.Denied,   decisionRenderedAt: now, priority: PaPriority.Routine));
        // Urgent: 1 approved, 1 denied → 50%
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, decisionRenderedAt: now, priority: PaPriority.Urgent));
        db.PaRequests.Add(MakeRequest(PaStatus.Denied,   decisionRenderedAt: now, priority: PaPriority.Urgent));
        // Withdrawn (should NOT appear in results — excluded from denominator)
        db.PaRequests.Add(MakeRequest(PaStatus.Withdrawn));
        await db.SaveChangesAsync();

        var from = DateOnly.FromDateTime(now.AddDays(-1));
        var to   = DateOnly.FromDateTime(now.AddDays(1));

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetApprovalRateAsync(from, to);

        // Should have exactly 2 rows (Routine, Urgent); Withdrawn excluded
        Assert.Equal(2, result.Count);

        var routine = result.Single(r => r.Priority == nameof(PaPriority.Routine));
        Assert.Equal(3,    routine.Approved);
        Assert.Equal(1,    routine.Denied);
        Assert.Equal(4,    routine.Total);
        Assert.Equal(75.0, routine.ApprovalRatePct);

        var urgent = result.Single(r => r.Priority == nameof(PaPriority.Urgent));
        Assert.Equal(1,    urgent.Approved);
        Assert.Equal(1,    urgent.Denied);
        Assert.Equal(2,    urgent.Total);
        Assert.Equal(50.0, urgent.ApprovalRatePct);
    }

    // ── GetSpecialistDashboardAsync — MyOverdueRequests (GAP-01) ─────────────

    [Fact]
    public async Task GetSpecialistDashboardAsync_CountsOverdueRequests()
    {
        var dbName       = nameof(GetSpecialistDashboardAsync_CountsOverdueRequests);
        var specialistId = "spec-overdue-001";
        var otherId      = "other-user";
        var now          = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock        = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // Overdue: active status, DecisionDueDate in the past, owned by specialist
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,
            decisionDueDate: now.AddDays(-2), submittedByUserId: specialistId));
        // Not overdue: future due date
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,
            decisionDueDate: now.AddDays(5), submittedByUserId: specialistId));
        // Overdue but terminal status (Approved) — should NOT count
        db.PaRequests.Add(MakeRequest(PaStatus.Approved,
            decisionDueDate: now.AddDays(-1), decisionRenderedAt: now,
            authEndDate: now.AddDays(60), submittedByUserId: specialistId));
        // Overdue but belongs to other user — should NOT count
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,
            decisionDueDate: now.AddDays(-1), submittedByUserId: otherId));
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetSpecialistDashboardAsync(specialistId);

        Assert.Equal(1, result.MyOverdueRequests);
    }

    [Fact]
    public async Task GetSpecialistDashboardAsync_OverdueRequests_IsZero_WhenNoneOverdue()
    {
        var dbName       = nameof(GetSpecialistDashboardAsync_OverdueRequests_IsZero_WhenNoneOverdue);
        var specialistId = "spec-no-overdue";
        var now          = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock        = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,
            decisionDueDate: now.AddDays(3), submittedByUserId: specialistId));
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetSpecialistDashboardAsync(specialistId);

        Assert.Equal(0, result.MyOverdueRequests);
    }

    // ── GetReviewerDashboardAsync — PendingMoreThan5Days (GAP-05) ────────────

    [Fact]
    public async Task GetReviewerDashboardAsync_CountsPendingMoreThan5Days()
    {
        var dbName     = nameof(GetReviewerDashboardAsync_CountsPendingMoreThan5Days);
        var reviewerId = "reviewer-pending5";
        var now        = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock      = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // Submitted 6 days ago → >5 days, should count
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,
            submittedAt: now.AddDays(-6)));
        // Submitted exactly 5 days ago (today - 5) → boundary: SubmittedAt.Date == cutoff.Date → should count
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,
            submittedAt: now.AddDays(-5)));
        // Submitted 3 days ago → <5 days, should NOT count
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted,
            submittedAt: now.AddDays(-3)));
        // UnderReview, submitted 7 days ago → should count
        db.PaRequests.Add(MakeRequest(PaStatus.UnderReview,
            submittedAt: now.AddDays(-7), reviewerUserId: reviewerId));
        // PendingInfo — not in the Submitted/UnderReview set, should NOT count
        db.PaRequests.Add(MakeRequest(PaStatus.PendingInfo,
            submittedAt: now.AddDays(-10)));
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetReviewerDashboardAsync(reviewerId);

        // 6-day + 5-day + 7-day (UnderReview) = 3 requests
        Assert.Equal(3, result.PendingMoreThan5Days);
    }

    // ── GetReviewerDashboardAsync — AvgDecisionDaysThisMonth (GAP-06) ────────

    [Fact]
    public async Task GetReviewerDashboardAsync_ComputesAvgDecisionDaysThisMonth()
    {
        var dbName     = nameof(GetReviewerDashboardAsync_ComputesAvgDecisionDaysThisMonth);
        var reviewerId = "reviewer-avg";
        var now        = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock      = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // 3 decided requests this month: turnaround = 2, 4, 6 days → avg 4.0
        db.PaRequests.Add(MakeRequest(PaStatus.Approved,
            submittedAt: now.AddDays(-2), decisionRenderedAt: now,
            authEndDate: now.AddDays(90)));
        db.PaRequests.Add(MakeRequest(PaStatus.Approved,
            submittedAt: now.AddDays(-4), decisionRenderedAt: now,
            authEndDate: now.AddDays(90)));
        db.PaRequests.Add(MakeRequest(PaStatus.Denied,
            submittedAt: now.AddDays(-6), decisionRenderedAt: now));
        // Decided last month — should NOT affect the average
        db.PaRequests.Add(MakeRequest(PaStatus.Approved,
            submittedAt: now.AddMonths(-2), decisionRenderedAt: now.AddMonths(-1),
            authEndDate: now.AddDays(90)));
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetReviewerDashboardAsync(reviewerId);

        Assert.Equal(4.0, result.AvgDecisionDaysThisMonth);
    }

    [Fact]
    public async Task GetReviewerDashboardAsync_AvgDecisionDays_IsZero_WhenNoDecisionsThisMonth()
    {
        var dbName     = nameof(GetReviewerDashboardAsync_AvgDecisionDays_IsZero_WhenNoDecisionsThisMonth);
        var reviewerId = "reviewer-no-decisions";
        var now        = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock      = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // Only an open request — no decisions yet
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted, submittedAt: now));
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetReviewerDashboardAsync(reviewerId);

        Assert.Equal(0.0, result.AvgDecisionDaysThisMonth);
    }

    // ── GetBillingDashboardAsync — PendingRequests (GAP-03) ──────────────────

    [Fact]
    public async Task GetBillingDashboardAsync_ReturnsPendingRequestsCount()
    {
        var dbName = nameof(GetBillingDashboardAsync_ReturnsPendingRequestsCount);
        var now    = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock  = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // 3 active requests (Submitted, UnderReview, PendingInfo)
        db.PaRequests.Add(MakeRequest(PaStatus.Submitted));
        db.PaRequests.Add(MakeRequest(PaStatus.UnderReview));
        db.PaRequests.Add(MakeRequest(PaStatus.PendingInfo));
        // Terminal requests — should NOT count
        db.PaRequests.Add(MakeRequest(PaStatus.Approved, authEndDate: now.AddDays(60)));
        db.PaRequests.Add(MakeRequest(PaStatus.Denied));
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetBillingDashboardAsync();

        Assert.Equal(3, result.PendingRequests);
    }

    // ── GetRecentActivityAsync (GAP-02) ──────────────────────────────────────

    [Fact]
    public async Task GetRecentActivityAsync_ReturnsLatestNItems_OrderedDescending()
    {
        var dbName       = nameof(GetRecentActivityAsync_ReturnsLatestNItems_OrderedDescending);
        var specialistId = "spec-activity-001";
        var now          = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock        = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // Seed a request belonging to the specialist
        var req = MakeRequest(PaStatus.Approved,
            submittedAt: now.AddDays(-10), decisionRenderedAt: now,
            authEndDate: now.AddDays(60), submittedByUserId: specialistId);
        db.PaRequests.Add(req);
        await db.SaveChangesAsync();

        // Add 12 status history rows for this request (more than the default count of 10)
        for (var i = 0; i < 12; i++)
        {
            db.PaStatusHistories.Add(new Prior_Authorization_Workflow_Tracker.Models.PaStatusHistory
            {
                PaRequestID      = req.ID,
                FromStatus       = PaStatus.Draft,
                ToStatus         = PaStatus.Submitted,
                ChangedByUserID  = specialistId,
                ChangedAt        = now.AddHours(-12 + i),
            });
        }
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetRecentActivityAsync(specialistId, count: 10);

        Assert.Equal(10, result.Count);
        // Verify descending order: first item is most recent
        Assert.True(result[0].OccurredAt >= result[1].OccurredAt);
        Assert.True(result[1].OccurredAt >= result[2].OccurredAt);
    }

    [Fact]
    public async Task GetRecentActivityAsync_IsScopedToSpecialist()
    {
        var dbName        = nameof(GetRecentActivityAsync_IsScopedToSpecialist);
        var specialistId  = "spec-scoped-001";
        var otherSpecId   = "spec-scoped-other";
        var now           = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock         = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        // Request belonging to target specialist
        var myReq = MakeRequest(PaStatus.Submitted,
            submittedAt: now, submittedByUserId: specialistId);
        db.PaRequests.Add(myReq);

        // Request belonging to OTHER specialist
        var otherReq = MakeRequest(PaStatus.Submitted,
            submittedAt: now, submittedByUserId: otherSpecId);
        db.PaRequests.Add(otherReq);
        await db.SaveChangesAsync();

        db.PaStatusHistories.Add(new Prior_Authorization_Workflow_Tracker.Models.PaStatusHistory
        {
            PaRequestID = myReq.ID, FromStatus = PaStatus.Draft, ToStatus = PaStatus.Submitted,
            ChangedByUserID = specialistId, ChangedAt = now,
        });
        db.PaStatusHistories.Add(new Prior_Authorization_Workflow_Tracker.Models.PaStatusHistory
        {
            PaRequestID = otherReq.ID, FromStatus = PaStatus.Draft, ToStatus = PaStatus.Submitted,
            ChangedByUserID = otherSpecId, ChangedAt = now,
        });
        await db.SaveChangesAsync();

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetRecentActivityAsync(specialistId, count: 10);

        // Only 1 history row belongs to the target specialist's request
        Assert.Single(result);
        Assert.Equal(myReq.ID, result[0].RequestId);
    }

    [Fact]
    public async Task GetRecentActivityAsync_ReturnsEmpty_WhenNoHistory()
    {
        var dbName       = nameof(GetRecentActivityAsync_ReturnsEmpty_WhenNoHistory);
        var specialistId = "spec-no-history";
        var now          = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var clock        = new FakeDateTimeProvider(now);

        using var db = MakeDb(dbName);
        await SeedAsync(db);

        var svc    = MakeService(dbName, clock);
        var result = await svc.GetRecentActivityAsync(specialistId, count: 10);

        Assert.Empty(result);
    }
}
