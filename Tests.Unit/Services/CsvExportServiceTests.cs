using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Prior_Authorization_Workflow_Tracker.Constants;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services;
using Tests.Unit.Fakes;

namespace Tests.Unit.Services;

/// <summary>
/// Unit tests for CsvExportService (T-D4, §8.10 FR-010, §13).
/// Verifies RBAC enforcement, 16-column header output, filename format, and audit event.
/// </summary>
public class CsvExportServiceTests
{
    // ── Builder ───────────────────────────────────────────────────────────────

    private static AppDbContext MakeDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(opts);
    }

    private static (CsvExportService Svc, Mock<IAuditService> AuditMock) Build(
        string dbName,
        FakeCurrentUserService? user = null,
        FakeDateTimeProvider? clock = null)
    {
        user  ??= new FakeCurrentUserService(Roles.BillingManager)
                  { UserId = "billing-id", UserName = "billing@pademo.com" };
        clock ??= new FakeDateTimeProvider();

        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => MakeDb(dbName));

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = new CsvExportService(factory.Object, user, clock, audit.Object, NullLogger<CsvExportService>.Instance);
        return (svc, audit);
    }

    // ── RBAC ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportRequestsAsync_Specialist_ThrowsAuthorizationException()
    {
        var specialist = FakeCurrentUserService.AsSpecialist();
        var (svc, _) = Build("csv_rbac_specialist", user: specialist);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.ExportRequestsAsync(new()));
    }

    [Fact]
    public async Task ExportRequestsAsync_Reviewer_ThrowsAuthorizationException()
    {
        var reviewer = FakeCurrentUserService.AsReviewer();
        var (svc, _) = Build("csv_rbac_reviewer", user: reviewer);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.ExportRequestsAsync(new()));
    }

    // ── Output format ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportRequestsAsync_Returns16ColumnHeader()
    {
        var (svc, _) = Build("csv_header_check");

        var (data, _) = await svc.ExportRequestsAsync(new());

        // UTF-8 BOM (3 bytes) + first line is the header
        var text = System.Text.Encoding.UTF8.GetString(data).TrimStart('\uFEFF');
        var headerLine = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).First();
        var columns = headerLine.Split(',');

        Assert.Equal(18, columns.Length);
    }

    [Fact]
    public async Task ExportRequestsAsync_FileNameFollowsFormat()
    {
        // Set a fixed time so the filename is deterministic
        var clock = new FakeDateTimeProvider(new DateTime(2026, 5, 4, 10, 30, 0, DateTimeKind.Utc));
        var (svc, _) = Build("csv_filename_format", clock: clock);

        var (_, fileName) = await svc.ExportRequestsAsync(new());

        Assert.Equal("PA_Export_20260504_103000.csv", fileName);
    }

    [Fact]
    public async Task ExportRequestsAsync_BillingManager_WritesAuditEvent()
    {
        var (svc, audit) = Build("csv_audit_event");

        await svc.ExportRequestsAsync(new());

        audit.Verify(a => a.LogAsync(
            "PaReport",
            It.IsAny<string>(),
            "ExportDownloaded",
            null,
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExportRequestsAsync_FilterApplied_OnlyMatchingRowsExported()
    {
        const string dbName = "csv_filter_check";
        await using (var db = MakeDb(dbName))
        {
            var plan = new InsurancePlan { ID = 1, PlanName = "Plan A", PayerName = "Payer", PlanType = "PPO", RoutineDecisionDays = 14, UrgentDecisionDays = 3, EmergentDecisionDays = 1, IsActive = true };
            var proc = new ProcedureCode { ID = 1, Code = "27447", Description = "Knee", RequiresPriorAuth = true, TypicalAuthorizationDurationDays = 90, IsActive = true };
            db.InsurancePlans.Add(plan);
            db.ProcedureCodes.Add(proc);
            db.PaRequests.AddRange(
                new PaRequest { RequestNumber = "PA-2026-00001", PatientMrn = "MRN001", PatientDob = new DateTime(1970, 1, 1), InsurancePlanID = 1, ProcedureCodeID = 1, ProviderID = "p1", SubmittedByUserID = "s1", DiagnosisCode = "M17.11", ClinicalJustification = new string('x', 55), ApprovedUnitsRequested = 1, Status = PaStatus.Approved, Priority = PaPriority.Routine, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow },
                new PaRequest { RequestNumber = "PA-2026-00002", PatientMrn = "MRN002", PatientDob = new DateTime(1975, 1, 1), InsurancePlanID = 1, ProcedureCodeID = 1, ProviderID = "p1", SubmittedByUserID = "s1", DiagnosisCode = "M17.11", ClinicalJustification = new string('x', 55), ApprovedUnitsRequested = 1, Status = PaStatus.Denied, Priority = PaPriority.Routine, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var (svc, _) = Build(dbName);
        var filter = new Prior_Authorization_Workflow_Tracker.Services.Models.PaRequestFilter { Status = PaStatus.Approved };

        var (data, _) = await svc.ExportRequestsAsync(filter);
        var text = System.Text.Encoding.UTF8.GetString(data).TrimStart('\uFEFF');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // 1 header + 1 data row (Approved only)
        Assert.Equal(2, lines.Length);
        Assert.Contains("PA-2026-00001", lines[1]);
        Assert.DoesNotContain("PA-2026-00002", text);
    }

    [Fact]
    public async Task ExportRequestsAsync_PriorityFilter_OnlyExportsMatchingRows()
    {
        const string dbName = "csv_priority_filter";
        await using (var db = MakeDb(dbName))
        {
            var plan = new InsurancePlan { ID = 1, PlanName = "Plan A", PayerName = "Payer", PlanType = "PPO", RoutineDecisionDays = 14, UrgentDecisionDays = 3, EmergentDecisionDays = 1, IsActive = true };
            var proc = new ProcedureCode { ID = 1, Code = "27447", Description = "Knee", RequiresPriorAuth = true, TypicalAuthorizationDurationDays = 90, IsActive = true };
            db.InsurancePlans.Add(plan);
            db.ProcedureCodes.Add(proc);
            db.PaRequests.AddRange(
                new PaRequest { RequestNumber = "PA-2026-00010", PatientMrn = "MRN010", PatientDob = new DateTime(1970, 1, 1), InsurancePlanID = 1, ProcedureCodeID = 1, DiagnosisCode = "M17.11", ClinicalJustification = new string('x', 55), ApprovedUnitsRequested = 1, Status = PaStatus.Submitted, Priority = PaPriority.Urgent,  CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow },
                new PaRequest { RequestNumber = "PA-2026-00011", PatientMrn = "MRN011", PatientDob = new DateTime(1975, 1, 1), InsurancePlanID = 1, ProcedureCodeID = 1, DiagnosisCode = "M17.11", ClinicalJustification = new string('x', 55), ApprovedUnitsRequested = 1, Status = PaStatus.Submitted, Priority = PaPriority.Routine, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var (svc, _) = Build(dbName);
        var filter = new Prior_Authorization_Workflow_Tracker.Services.Models.PaRequestFilter
            { Priority = PaPriority.Urgent };

        var (data, _) = await svc.ExportRequestsAsync(filter);
        var text = System.Text.Encoding.UTF8.GetString(data).TrimStart('\uFEFF');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // 1 header + 1 data row (Urgent only)
        Assert.Equal(2, lines.Length);
        Assert.Contains("PA-2026-00010", lines[1]);
        Assert.DoesNotContain("PA-2026-00011", text);
    }
}
