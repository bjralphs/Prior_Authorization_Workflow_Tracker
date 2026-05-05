using Microsoft.EntityFrameworkCore;
using Prior_Authorization_Workflow_Tracker.Models;
using Xunit;

namespace Tests.Integration;

/// <summary>
/// Integration tests for EF Core optimistic concurrency via the RowVersion timestamp column.
///
/// PRD §13.3 key scenario:
///   WorkflowService_ConcurrentDecision_ShouldThrowDbUpdateConcurrencyException
///
/// Why integration (not unit) tests?
///   EF Core InMemory does NOT enforce IsRowVersion() / timestamp concurrency tokens.
///   Only a real SQL Server instance applies the database-level ROWVERSION check and
///   raises a conflict when a stale RowVersion is detected during UPDATE.
///
/// Test strategy:
///   1. Seed a PaRequest in UnderReview status.
///   2. Load the same row in two independent DbContext instances (ctxA, ctxB) — each
///      captures the current RowVersion (v1).
///   3. ctxA approves the request and calls SaveChangesAsync → succeeds; SQL Server
///      auto-increments RowVersion to v2.
///   4. ctxB holds the stale v1 RowVersion and tries to deny the same request.
///      EF Core generates WHERE RowVersion = v1, which matches 0 rows (DB now has v2)
///      and throws DbUpdateConcurrencyException.
/// </summary>
[Collection("SqlServer")]
public sealed class WorkflowConcurrencyTests(SqlServerFixture fixture)
{
    // ── Seed helper ───────────────────────────────────────────────────────────

    private async Task<(int requestId, string userId, int denialReasonId)>
        SeedUnderReviewScenarioAsync()
    {
        var uid = $"rv-{Guid.NewGuid():N}"; // unique per invocation

        await using var ctx = fixture.CreateDbContext();
        await fixture.SeedUserAsync(ctx, uid, $"{uid}@test.com");

        var plan = new InsurancePlan
        {
            PlanName            = "Concurrency PPO",
            PayerName           = "ConcurrentPayer",
            PlanType            = "PPO",
            RoutineDecisionDays = 14,
            UrgentDecisionDays  = 3,
            EmergentDecisionDays = 1,
            IsActive            = true,
        };
        var proc = new ProcedureCode
        {
            Code                              = "99213",
            Description                       = "Office Visit",
            RequiresPriorAuth                 = true,
            TypicalAuthorizationDurationDays  = 30,
            IsActive                          = true,
        };
        var denial = new DenialReason
        {
            Code              = "MND",
            Description       = "Not medically necessary",
            IsAppealable      = true,
            AppealDeadlineDays = 30,
        };
        ctx.InsurancePlans.Add(plan);
        ctx.ProcedureCodes.Add(proc);
        ctx.DenialReasons.Add(denial);
        await ctx.SaveChangesAsync();

        var request = new PaRequest
        {
            RequestNumber          = $"PA-RV-{uid[..8]}",
            PatientMrn             = $"MRN-{uid[..8]}",
            PatientName            = "Concurrency Test Patient",
            PatientDob             = new DateTime(1985, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            InsurancePlanID        = plan.ID,
            ProcedureCodeID        = proc.ID,
            ProviderID             = uid,
            SubmittedByUserID      = uid,
            ReviewerUserID         = uid,
            DiagnosisCode          = "A00.0",
            ClinicalJustification  = "Integration test justification — meets the fifty-character minimum.",
            Status                 = PaStatus.UnderReview,
            Priority               = PaPriority.Routine,
            SubmittedAt            = DateTime.UtcNow.AddDays(-3),
            DecisionDueDate        = DateTime.UtcNow.AddDays(11),
            ApprovedUnitsRequested = 5,
            CreatedAt              = DateTime.UtcNow,
            UpdatedAt              = DateTime.UtcNow,
        };
        ctx.PaRequests.Add(request);
        await ctx.SaveChangesAsync();

        return (request.ID, uid, denial.ID);
    }

    // ── PRD §13.3 primary scenario ────────────────────────────────────────────

    /// <summary>
    /// PRD §13.3: WorkflowService_ConcurrentDecision_ShouldThrowDbUpdateConcurrencyException
    ///
    /// Reviewer A and Reviewer B simultaneously load the same UnderReview request.
    /// Reviewer A's save (Approve) succeeds and bumps the SQL Server ROWVERSION.
    /// Reviewer B's subsequent save (Deny) fails with DbUpdateConcurrencyException
    /// because EF Core detects that the RowVersion in B's tracked entity no longer
    /// matches the current database value.
    /// </summary>
    [Fact]
    public async Task WorkflowService_ConcurrentDecision_ShouldThrowDbUpdateConcurrencyException()
    {
        // Arrange
        var (requestId, uid, denialReasonId) = await SeedUnderReviewScenarioAsync();

        // Load the same request in two separate tracking contexts (simulates two concurrent HTTP requests).
        await using var ctxA = fixture.CreateDbContext();
        await using var ctxB = fixture.CreateDbContext();

        var reqA = await ctxA.PaRequests.FindAsync(requestId);
        var reqB = await ctxB.PaRequests.FindAsync(requestId);

        Assert.NotNull(reqA);
        Assert.NotNull(reqB);

        // Both contexts captured the same RowVersion at load time.
        Assert.Equal(reqA.RowVersion, reqB.RowVersion);

        var now = DateTime.UtcNow;

        // ── Reviewer A approves (first writer wins) ───────────────────────────
        reqA.Status                = PaStatus.Approved;
        reqA.UpdatedAt             = now;
        reqA.DecisionRenderedAt    = now;
        reqA.ApprovedUnitsGranted  = 3;
        reqA.AuthorizationNumber   = $"AUTH-{now:yyyyMMdd}-A1B2C3";
        reqA.AuthorizationStartDate = now.Date;
        reqA.AuthorizationEndDate   = now.AddDays(30);
        ctxA.PaStatusHistories.Add(new PaStatusHistory
        {
            PaRequestID     = requestId,
            FromStatus      = PaStatus.UnderReview,
            ToStatus        = PaStatus.Approved,
            ChangedByUserID = uid,
            ChangedAt       = now,
        });
        await ctxA.SaveChangesAsync(); // succeeds; SQL Server bumps ROWVERSION to v2

        // ── Reviewer B tries to deny (holds stale v1 RowVersion — must fail) ──
        reqB.Status             = PaStatus.Denied;
        reqB.UpdatedAt          = now;
        reqB.DecisionRenderedAt = now;
        reqB.DenialReasonID     = denialReasonId;
        ctxB.PaStatusHistories.Add(new PaStatusHistory
        {
            PaRequestID     = requestId,
            FromStatus      = PaStatus.UnderReview,
            ToStatus        = PaStatus.Denied,
            ChangedByUserID = uid,
            ChangedAt       = now,
        });

        // Assert: EF Core issues UPDATE ... WHERE RowVersion = v1, matches 0 rows → exception
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => ctxB.SaveChangesAsync());
    }

    /// <summary>
    /// After a concurrent conflict, the winning save's data must be preserved in the
    /// database without corruption from the rejected concurrent write.
    /// </summary>
    [Fact]
    public async Task WorkflowService_AfterConcurrentConflict_WinnerDataIsPreserved()
    {
        // Arrange
        var (requestId, uid, _) = await SeedUnderReviewScenarioAsync();

        await using var ctxA = fixture.CreateDbContext();
        await using var ctxB = fixture.CreateDbContext();

        var reqA = await ctxA.PaRequests.FindAsync(requestId);
        var reqB = await ctxB.PaRequests.FindAsync(requestId);
        Assert.NotNull(reqA);
        Assert.NotNull(reqB);

        var now    = DateTime.UtcNow;
        var authNo = $"AUTH-{now:yyyyMMdd}-D4E5F6";

        // Reviewer A approves
        reqA.Status                 = PaStatus.Approved;
        reqA.UpdatedAt              = now;
        reqA.DecisionRenderedAt     = now;
        reqA.ApprovedUnitsGranted   = 5;
        reqA.AuthorizationNumber    = authNo;
        reqA.AuthorizationStartDate = now.Date;
        reqA.AuthorizationEndDate   = now.AddDays(30);
        ctxA.PaStatusHistories.Add(new PaStatusHistory
        {
            PaRequestID     = requestId,
            FromStatus      = PaStatus.UnderReview,
            ToStatus        = PaStatus.Approved,
            ChangedByUserID = uid,
            ChangedAt       = now,
        });
        await ctxA.SaveChangesAsync();

        // Reviewer B's concurrent deny must fail
        reqB.Status         = PaStatus.Denied;
        reqB.UpdatedAt      = now;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => ctxB.SaveChangesAsync());

        // Read the committed state in a fresh context — must reflect the winner (Approved)
        await using var ctxRead = fixture.CreateDbContext();
        var committed = await ctxRead.PaRequests.FindAsync(requestId);

        Assert.NotNull(committed);
        Assert.Equal(PaStatus.Approved, committed.Status);
        Assert.Equal(authNo,            committed.AuthorizationNumber);
        Assert.Equal(5,                 committed.ApprovedUnitsGranted);
    }
}
