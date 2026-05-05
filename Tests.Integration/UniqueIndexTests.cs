using Microsoft.EntityFrameworkCore;
using Prior_Authorization_Workflow_Tracker.Models;
using Xunit;

namespace Tests.Integration;

/// <summary>
/// Integration tests for the unique partial index on PaRequests.AuthorizationNumber
/// added by the AddAuthorizationNumberUniqueIndex migration.
///
/// Index definition:
///   CREATE UNIQUE INDEX UX_PaRequests_AuthorizationNumber
///   ON PaRequests (AuthorizationNumber)
///   WHERE AuthorizationNumber IS NOT NULL
///
/// Why integration tests?
///   EF Core InMemory ignores SQL unique indexes. Only a real SQL Server instance
///   enforces the constraint and raises an error on duplicate non-null values.
///
/// Two scenarios are covered:
///   1. Two approved requests with the same non-null AuthorizationNumber → DbUpdateException.
///   2. Two draft requests with NULL AuthorizationNumber → both inserts succeed
///      (the WHERE IS NOT NULL filter excludes NULLs from the unique index).
/// </summary>
[Collection("SqlServer")]
public sealed class UniqueIndexTests(SqlServerFixture fixture)
{
    // ── Seed helper ───────────────────────────────────────────────────────────

    private async Task<(int planId, int procId, string uid)> SeedReferenceDataAsync()
    {
        var uid = $"ix-{Guid.NewGuid():N}";

        await using var ctx = fixture.CreateDbContext();
        await fixture.SeedUserAsync(ctx, uid, $"{uid}@test.com");

        var plan = new InsurancePlan
        {
            PlanName             = "Index PPO",
            PayerName            = "IndexPayer",
            PlanType             = "PPO",
            RoutineDecisionDays  = 14,
            UrgentDecisionDays   = 3,
            EmergentDecisionDays = 1,
            IsActive             = true,
        };
        var proc = new ProcedureCode
        {
            Code                             = "99215",
            Description                      = "Office Visit Level 5",
            RequiresPriorAuth                = true,
            TypicalAuthorizationDurationDays = 30,
            IsActive                         = true,
        };
        ctx.InsurancePlans.Add(plan);
        ctx.ProcedureCodes.Add(proc);
        await ctx.SaveChangesAsync();

        return (plan.ID, proc.ID, uid);
    }

    private static PaRequest BuildApprovedRequest(
        int planId, int procId, string uid,
        string authNumber, string suffix, DateTime now) => new()
    {
        RequestNumber          = $"PA-IX-{suffix}",
        PatientMrn             = $"IX-{uid[..6]}-{suffix}",
        PatientName            = "Index Test Patient",
        PatientDob             = new DateTime(1975, 3, 20, 0, 0, 0, DateTimeKind.Utc),
        InsurancePlanID        = planId,
        ProcedureCodeID        = procId,
        ProviderID             = uid,
        SubmittedByUserID      = uid,
        DiagnosisCode          = "B01.9",
        ClinicalJustification  = "Unique-index integration test justification — meets fifty-char minimum.",
        Status                 = PaStatus.Approved,
        Priority               = PaPriority.Routine,
        SubmittedAt            = now.AddDays(-5),
        DecisionDueDate        = now.AddDays(9),
        DecisionRenderedAt     = now.AddDays(-1),
        ApprovedUnitsRequested = 1,
        ApprovedUnitsGranted   = 1,
        AuthorizationNumber    = authNumber,
        AuthorizationStartDate = now.Date,
        AuthorizationEndDate   = now.AddDays(30),
        CreatedAt              = now,
        UpdatedAt              = now,
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Two approved requests sharing the same non-null AuthorizationNumber must
    /// produce a DbUpdateException (SQL Server unique index violation 2601/2627).
    /// </summary>
    [Fact]
    public async Task AuthorizationNumber_UniqueIndex_PreventsDuplicates()
    {
        var (planId, procId, uid) = await SeedReferenceDataAsync();
        var now      = DateTime.UtcNow;
        var authNum  = $"AUTH-{now:yyyyMMdd}-{uid[..6].ToUpperInvariant()}";

        await using var ctx = fixture.CreateDbContext();

        // First approved request — must succeed.
        ctx.PaRequests.Add(BuildApprovedRequest(planId, procId, uid, authNum, "A1", now));
        await ctx.SaveChangesAsync();

        // Second approved request with the SAME AuthorizationNumber — must fail.
        ctx.PaRequests.Add(BuildApprovedRequest(planId, procId, uid, authNum, "A2", now));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(
            () => ctx.SaveChangesAsync());

        // SQL Server error message includes the index name or "duplicate key".
        var message = ex.InnerException?.Message ?? ex.Message;
        Assert.True(
            message.Contains("UX_PaRequests_AuthorizationNumber", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase),
            $"Expected unique index violation message. Actual: {message}");
    }

    /// <summary>
    /// The partial index (WHERE AuthorizationNumber IS NOT NULL) must allow multiple
    /// rows with NULL AuthorizationNumber — e.g. Draft requests before approval.
    /// </summary>
    [Fact]
    public async Task AuthorizationNumber_UniqueIndex_AllowsMultipleNulls_ForDraftRequests()
    {
        var (planId, procId, uid) = await SeedReferenceDataAsync();
        var now = DateTime.UtcNow;

        await using var ctx = fixture.CreateDbContext();

        // Two Draft requests with null AuthorizationNumber — the partial index must allow this.
        for (var i = 0; i < 2; i++)
        {
            ctx.PaRequests.Add(new PaRequest
            {
                RequestNumber          = $"PA-NL-{uid[..6]}-{i}",
                PatientMrn             = $"NL-{uid[..6]}-{i}",
                PatientName            = "Null Auth Test Patient",
                PatientDob             = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                InsurancePlanID        = planId,
                ProcedureCodeID        = procId,
                ProviderID             = uid,
                SubmittedByUserID      = uid,
                DiagnosisCode          = "C00.0",
                ClinicalJustification  = "Partial-index null test justification — minimum fifty characters!",
                Status                 = PaStatus.Draft,
                Priority               = PaPriority.Routine,
                ApprovedUnitsRequested = 1,
                AuthorizationNumber    = null,  // excluded from unique index by WHERE IS NOT NULL
                CreatedAt              = now,
                UpdatedAt              = now,
            });
        }

        // Both inserts must succeed without a unique index violation.
        var ex = await Record.ExceptionAsync(() => ctx.SaveChangesAsync());
        Assert.Null(ex);
    }
}
