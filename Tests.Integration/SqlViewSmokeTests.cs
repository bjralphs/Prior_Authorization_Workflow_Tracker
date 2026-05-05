using Microsoft.EntityFrameworkCore;
using Prior_Authorization_Workflow_Tracker.Models;
using Xunit;

namespace Tests.Integration;

/// <summary>
/// Smoke tests that verify the four SQL reporting views created by the
/// AddReportingViews migration can be queried against real SQL Server
/// after migrations are applied.
///
/// Why integration tests?
///   EF Core InMemory cannot execute raw SQL views, so these tests require
///   a real SQL Server instance (Testcontainers).
///
/// Each test seeds an Approved request so that views returning aggregations
/// have at least one row of data to return.
/// </summary>
[Collection("SqlServer")]
public sealed class SqlViewSmokeTests(SqlServerFixture fixture)
{
    // ── Seed helper ───────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a minimal Approved PaRequest with an AuthorizationNumber and future
    /// AuthorizationEndDate so all four reporting views have data to aggregate.
    /// </summary>
    private async Task SeedApprovedRequestAsync()
    {
        var uid = $"vw-{Guid.NewGuid():N}";

        await using var ctx = fixture.CreateDbContext();
        await fixture.SeedUserAsync(ctx, uid, $"{uid}@test.com");

        var plan = new InsurancePlan
        {
            PlanName             = "View PPO",
            PayerName            = "ViewPayer",
            PlanType             = "PPO",
            RoutineDecisionDays  = 14,
            UrgentDecisionDays   = 3,
            EmergentDecisionDays = 1,
            IsActive             = true,
        };
        var proc = new ProcedureCode
        {
            Code                             = "99214",
            Description                      = "Office Visit Level 4",
            RequiresPriorAuth                = true,
            TypicalAuthorizationDurationDays = 90,
            IsActive                         = true,
        };
        ctx.InsurancePlans.Add(plan);
        ctx.ProcedureCodes.Add(proc);
        await ctx.SaveChangesAsync();

        var now = DateTime.UtcNow;
        ctx.PaRequests.Add(new PaRequest
        {
            RequestNumber          = $"PA-VW-{uid[..8]}",
            PatientMrn             = $"VW-{uid[..8]}",
            PatientName            = "View Smoke Patient",
            PatientDob             = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            InsurancePlanID        = plan.ID,
            ProcedureCodeID        = proc.ID,
            ProviderID             = uid,
            SubmittedByUserID      = uid,
            DiagnosisCode          = "Z00.00",
            ClinicalJustification  = "View smoke test justification — at least fifty characters long.",
            Status                 = PaStatus.Approved,
            Priority               = PaPriority.Routine,
            SubmittedAt            = now.AddDays(-7),
            DecisionDueDate        = now.AddDays(7),
            DecisionRenderedAt     = now.AddDays(-1),
            ApprovedUnitsRequested = 1,
            ApprovedUnitsGranted   = 1,
            // AuthorizationEndDate within 30 days ensures vw_ExpiringAuthorizations also returns a row.
            AuthorizationNumber    = $"AUTH-{now:yyyyMMdd}-{uid[..6].ToUpperInvariant()}",
            AuthorizationStartDate = now.Date,
            AuthorizationEndDate   = now.AddDays(20),
            CreatedAt              = now,
            UpdatedAt              = now,
        });
        await ctx.SaveChangesAsync();
    }

    // ── vw_PaRequestSummary ───────────────────────────────────────────────────

    /// <summary>
    /// vw_PaRequestSummary must return at least one row when approved requests exist.
    /// This view joins PaRequests, ProcedureCodes, InsurancePlans, and AspNetUsers.
    /// </summary>
    [Fact]
    public async Task vw_PaRequestSummary_ReturnsRows_WhenRequestsExist()
    {
        await SeedApprovedRequestAsync();

        await using var ctx = fixture.CreateDbContext();

        var count = await ctx.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM vw_PaRequestSummary")
            .SingleAsync();

        Assert.True(count > 0, "vw_PaRequestSummary returned 0 rows; expected at least 1.");
    }

    // ── vw_RequestVolumeByMonth ───────────────────────────────────────────────

    /// <summary>
    /// vw_RequestVolumeByMonth aggregates submission counts by year-month.
    /// Must return at least one row when any PaRequest with SubmittedAt is present.
    /// </summary>
    [Fact]
    public async Task vw_RequestVolumeByMonth_ReturnsRows_WhenRequestsExist()
    {
        await SeedApprovedRequestAsync();

        await using var ctx = fixture.CreateDbContext();

        var count = await ctx.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM vw_RequestVolumeByMonth")
            .SingleAsync();

        Assert.True(count > 0, "vw_RequestVolumeByMonth returned 0 rows; expected at least 1.");
    }

    // ── vw_ExpiringAuthorizations ─────────────────────────────────────────────

    /// <summary>
    /// vw_ExpiringAuthorizations returns Approved requests whose AuthorizationEndDate
    /// falls within 30 days from now. The seeded request uses AuthorizationEndDate = now+20d,
    /// so the view must return at least one row.
    /// </summary>
    [Fact]
    public async Task vw_ExpiringAuthorizations_ReturnsRows_WhenExpiringAuthsExist()
    {
        await SeedApprovedRequestAsync();

        await using var ctx = fixture.CreateDbContext();

        var count = await ctx.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM vw_ExpiringAuthorizations")
            .SingleAsync();

        Assert.True(count > 0,
            "vw_ExpiringAuthorizations returned 0 rows; expected at least 1 (seeded request expires in 20 days).");
    }

    // ── vw_DenialsByReason ────────────────────────────────────────────────────

    /// <summary>
    /// vw_DenialsByReason must be queryable without error.
    /// Returns 0 rows when no denied requests exist, which is valid.
    /// </summary>
    [Fact]
    public async Task vw_DenialsByReason_IsQueryable_WithoutError()
    {
        await using var ctx = fixture.CreateDbContext();

        // Verify the view exists and can be queried; 0 rows is acceptable.
        var ex = await Record.ExceptionAsync(async () =>
        {
            _ = await ctx.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM vw_DenialsByReason")
                .SingleAsync();
        });

        Assert.Null(ex);
    }
}
