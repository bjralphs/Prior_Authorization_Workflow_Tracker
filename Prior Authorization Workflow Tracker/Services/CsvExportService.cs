using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prior_Authorization_Workflow_Tracker.Constants;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// CsvHelper-based implementation of ICsvExportService (§8.10, FR-010).
///
/// Design decisions:
///  - UTF-8 with BOM (0xEF 0xBB 0xBF) for Excel compatibility on Windows (BUG-010 mitigation).
///  - 18 columns: 16 per PRD §8.10 + SubmittedByUser + Reviewer (both required by §8.10).
///  - RBAC enforced at service layer — Billing Manager and Admin only.
///  - Pagination is ignored: export is unbounded but applies all other active filters
///    so the caller's filter context is respected.
///  - File name: PA_Export_YYYYMMDD_HHmmss.csv using UTC time from IDateTimeProvider.
/// </summary>
public sealed class CsvExportService : ICsvExportService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<CsvExportService> _logger;

    public CsvExportService(
        IDbContextFactory<AppDbContext> dbFactory,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IAuditService audit,
        ILogger<CsvExportService> logger)
    {
        _dbFactory   = dbFactory;
        _currentUser = currentUser;
        _clock       = clock;
        _audit       = audit;
        _logger      = logger;
    }

    public async Task<(byte[] Data, string FileName)> ExportRequestsAsync(
        PaRequestFilter filter, CancellationToken ct = default)
    {
        if (!_currentUser.IsInRole(Roles.BillingManager) && !_currentUser.IsInRole(Roles.Admin))
            throw new AuthorizationException("Only Billing Manager and Admin roles can export data.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Build the same query as PaRequestService.GetQueueAsync — no pagination
        IQueryable<PaRequest> query = db.PaRequests
            .AsNoTracking()
            .Include(r => r.InsurancePlan)
            .Include(r => r.ProcedureCode)
            .Include(r => r.DenialReason);

        if (filter.Status.HasValue)
            query = query.Where(r => r.Status == filter.Status.Value);
        if (filter.Priority.HasValue)
            query = query.Where(r => r.Priority == filter.Priority.Value);
        if (filter.InsurancePlanID.HasValue)
            query = query.Where(r => r.InsurancePlanID == filter.InsurancePlanID.Value);
        if (filter.ProcedureCodeID.HasValue)
            query = query.Where(r => r.ProcedureCodeID == filter.ProcedureCodeID.Value);
        if (filter.SubmittedFrom.HasValue)
            query = query.Where(r => r.SubmittedAt >= filter.SubmittedFrom.Value);
        if (filter.SubmittedTo.HasValue)
            query = query.Where(r => r.SubmittedAt <= filter.SubmittedTo.Value);

        var rows = await query
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(ct);

        // Resolve display names for SubmittedByUser and ReviewerUser in a single query (PRD §8.10)
        var userIds = rows
            .SelectMany(r => new[] { r.SubmittedByUserID, r.ReviewerUserID })
            .Where(id => id is not null)
            .Distinct()
            .ToList();

        var userNames = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u =>
                string.IsNullOrWhiteSpace(u.FullName) ? (u.Email ?? u.Id) : u.FullName, ct);

        _logger.LogInformation(
            "CSV export: {Count} rows by {User}", rows.Count, _currentUser.UserName);

        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            NewLine         = "\r\n",
        };

        // UTF-8 with BOM for Excel Windows compatibility (BUG-010 mitigation)
        using var ms     = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv    = new CsvWriter(writer, csvConfig);

        // 18 columns: 16 per PRD §8.10 + SubmittedByUser + Reviewer
        WriteHeader(csv);
        await csv.NextRecordAsync();

        foreach (var r in rows)
        {
            WriteRow(csv, r, userNames);
            await csv.NextRecordAsync();
        }

        await writer.FlushAsync(ct);

        var now      = _clock.UtcNow;
        var fileName = $"PA_Export_{now:yyyyMMdd_HHmmss}.csv";

        // §11: audit export downloads so compliance officers can review who pulled data
        await _audit.LogAsync(
            "PaReport",
            _currentUser.UserId ?? "unknown",
            "ExportDownloaded",
            newValues: $"{{\"fileName\":\"{fileName}\",\"rowCount\":{rows.Count}}}");

        return (ms.ToArray(), fileName);
    }

    // ── Column definitions (PRD §8.10) ────────────────────────────────────────────────────────
    // 18 columns: 16 per PRD §8.10 + "Submitted By" + "Reviewer" (both required by §8.10).

    private static void WriteHeader(CsvWriter csv)
    {
        var headers = new[]
        {
            "Request Number", "Patient MRN", "Patient Name", "Patient DOB",
            "Insurance Plan", "Payer Name", "Procedure Code", "Procedure Description",
            "Diagnosis Code", "Priority", "Status", "Submitted Date",
            "Decision Due Date", "Decision Date", "Authorization Number", "Denial Reason",
            "Submitted By", "Reviewer",
        };
        foreach (var h in headers) csv.WriteField(h);
    }

    private static void WriteRow(CsvWriter csv, PaRequest r, Dictionary<string, string> userNames)
    {
        csv.WriteField(r.RequestNumber);
        csv.WriteField(r.PatientMrn);
        csv.WriteField(r.PatientName ?? string.Empty);
        csv.WriteField(r.PatientDob.ToString("yyyy-MM-dd"));
        csv.WriteField(r.InsurancePlan?.PlanName ?? r.InsurancePlanID.ToString());
        csv.WriteField(r.InsurancePlan?.PayerName ?? string.Empty);
        csv.WriteField(r.ProcedureCode?.Code ?? r.ProcedureCodeID.ToString());
        csv.WriteField(r.ProcedureCode?.Description ?? string.Empty);
        csv.WriteField(r.DiagnosisCode ?? string.Empty);
        csv.WriteField(r.Priority.ToString());
        csv.WriteField(r.Status.ToString());
        csv.WriteField(r.SubmittedAt.HasValue ? r.SubmittedAt.Value.ToString("yyyy-MM-dd") : string.Empty);
        csv.WriteField(r.DecisionDueDate.HasValue ? r.DecisionDueDate.Value.ToString("yyyy-MM-dd") : string.Empty);
        csv.WriteField(r.DecisionRenderedAt.HasValue ? r.DecisionRenderedAt.Value.ToString("yyyy-MM-dd") : string.Empty);
        csv.WriteField(r.AuthorizationNumber ?? string.Empty);
        csv.WriteField(r.DenialReason?.Description ?? string.Empty);
        // User display names resolved from batch query (PRD §8.10 SubmittedByUser + ReviewerUser)
        csv.WriteField(r.SubmittedByUserID is null ? string.Empty
            : userNames.GetValueOrDefault(r.SubmittedByUserID, r.SubmittedByUserID));
        csv.WriteField(r.ReviewerUserID is null ? string.Empty
            : userNames.GetValueOrDefault(r.ReviewerUserID, r.ReviewerUserID));
    }
}
