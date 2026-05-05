using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Implementation of IReportingService using LINQ-to-SQL queries (§12.2, FR-009).
/// All queries use AsNoTracking() per NFR-002. Each method opens a fresh DB context
/// via IDbContextFactory to ensure independent, short-lived connections.
///
/// SQL views (vw_PaRequestSummary, vw_DenialsByReason, vw_RequestVolumeByMonth,
/// vw_ExpiringAuthorizations) are defined in the EF Core migration
/// AddReportingViews and expose the same logic for SQL Server query optimization.
/// They are NOT mapped here — the service uses LINQ for testability with InMemory.
/// </summary>
public sealed class ReportingService : IReportingService
{
    private static readonly PaStatus[] ActiveStatuses =
    [
        PaStatus.Submitted, PaStatus.UnderReview, PaStatus.PendingInfo, PaStatus.Appealed,
    ];

    private static readonly PaStatus[] TerminalStatuses =
    [
        PaStatus.Approved, PaStatus.Denied, PaStatus.Expired, PaStatus.Withdrawn,
    ];

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ReportingService> _logger;

    public ReportingService(
        IDbContextFactory<AppDbContext> dbFactory,
        IDateTimeProvider clock,
        ILogger<ReportingService> logger)
    {
        _dbFactory = dbFactory;
        _clock     = clock;
        _logger    = logger;
    }

    // ── Status Summary ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RequestStatusSummary>> GetStatusSummaryAsync(
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var today = _clock.UtcNow.Date;

        var groups = await db.PaRequests
            .AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count  = g.Count(),
                Overdue = g.Count(r =>
                    r.DecisionDueDate.HasValue &&
                    r.DecisionDueDate.Value.Date < today),
            })
            .ToListAsync(ct);

        return groups
            .Select(g => new RequestStatusSummary(g.Status, g.Count, g.Overdue))
            .OrderBy(s => s.Status)
            .ToList();
    }

    // ── Denials by Reason ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DenialReasonSummary>> GetDenialsByReasonAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var total = await db.PaRequests
            .AsNoTracking()
            .Where(r => (r.Status == PaStatus.Denied || r.Status == PaStatus.Appealed)
                     && r.DecisionRenderedAt >= fromDt
                     && r.DecisionRenderedAt <= toDt)
            .CountAsync(ct);

        if (total == 0) return [];

        var groups = await db.PaRequests
            .AsNoTracking()
            .Where(r => (r.Status == PaStatus.Denied || r.Status == PaStatus.Appealed)
                     && r.DecisionRenderedAt >= fromDt
                     && r.DecisionRenderedAt <= toDt
                     && r.DenialReasonID.HasValue)
            .Join(db.DenialReasons.AsNoTracking(),
                  r => r.DenialReasonID!.Value,
                  d => d.ID,
                  (r, d) => new { d.Code, d.Description, d.IsAppealable })
            .GroupBy(x => new { x.Code, x.Description, x.IsAppealable })
            .Select(g => new
            {
                g.Key.Code,
                g.Key.Description,
                g.Key.IsAppealable,
                Count = g.Count(),
            })
            .OrderByDescending(g => g.Count)
            .ToListAsync(ct);

        return groups
            .Select(g => new DenialReasonSummary(
                g.Code, g.Description, g.Count,
                Math.Round((double)g.Count / total * 100, 1),
                g.IsAppealable))
            .ToList();
    }

    // ── Monthly Volume ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<MonthlyVolume>> GetMonthlyVolumeAsync(
        int months = 12, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = _clock.UtcNow.AddMonths(-months);

        var rows = await db.PaRequests
            .AsNoTracking()
            .Where(r => r.SubmittedAt.HasValue && r.SubmittedAt.Value >= cutoff)
            .Select(r => new
            {
                Year      = r.SubmittedAt!.Value.Year,
                Month     = r.SubmittedAt!.Value.Month,
                Status    = r.Status,
            })
            .ToListAsync(ct); // client-side grouping to avoid EF InMemory limitations

        var grouped = rows
            .GroupBy(r => new { r.Year, r.Month })
            .Select(g => new MonthlyVolume(
                g.Key.Year,
                g.Key.Month,
                Submitted: g.Count(),
                Approved:  g.Count(r => r.Status == PaStatus.Approved),
                Denied:    g.Count(r => r.Status == PaStatus.Denied)))
            .OrderBy(v => v.Year).ThenBy(v => v.Month)
            .ToList();

        return grouped;
    }

    // ── Expiring Authorizations ───────────────────────────────────────────────

    public async Task<IReadOnlyList<ExpiringAuthorization>> GetExpiringAuthorizationsAsync(
        int daysAhead = 30, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var today   = _clock.UtcNow.Date;
        var cutoff  = today.AddDays(daysAhead);

        var rows = await db.PaRequests
            .AsNoTracking()
            .Where(r => r.Status == PaStatus.Approved
                     && r.AuthorizationEndDate.HasValue
                     && r.AuthorizationEndDate.Value.Date >= today
                     && r.AuthorizationEndDate.Value.Date <= cutoff)
            .OrderBy(r => r.AuthorizationEndDate)
            .Select(r => new
            {
                r.ID,
                r.RequestNumber,
                r.PatientName,
                r.AuthorizationNumber,
                r.AuthorizationEndDate,
            })
            .ToListAsync(ct);

        return rows.Select(r => new ExpiringAuthorization(
            r.ID,
            r.RequestNumber,
            r.PatientName ?? string.Empty,
            r.AuthorizationNumber ?? string.Empty,
            r.AuthorizationEndDate!.Value,
            DaysUntilExpiry: (r.AuthorizationEndDate!.Value.Date - today).Days))
            .ToList();
    }

    // ── Dashboard Summary ─────────────────────────────────────────────────────

    public async Task<DashboardSummary> GetDashboardSummaryAsync(
        string myUserId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var today      = _clock.UtcNow.Date;
        var cutoff     = today.AddDays(30);
        var monthStart = new DateTime(_clock.UtcNow.Year, _clock.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Server-side aggregation: each CountAsync issues a single COUNT query to the
        // DB, avoiding the previous pattern of loading all request rows into memory.
        var q = db.PaRequests.AsNoTracking();

        var openCount = await q
            .Where(r => ActiveStatuses.Contains(r.Status))
            .CountAsync(ct);

        var pendingMyAction = await q
            .Where(r => ActiveStatuses.Contains(r.Status)
                     && (r.ReviewerUserID == myUserId || r.SubmittedByUserID == myUserId))
            .CountAsync(ct);

        var overdue = await q
            .Where(r => ActiveStatuses.Contains(r.Status)
                     && r.DecisionDueDate.HasValue
                     && r.DecisionDueDate.Value.Date < today)
            .CountAsync(ct);

        var expiringSoon = await q
            .Where(r => r.Status == PaStatus.Approved
                     && r.AuthorizationEndDate.HasValue
                     && r.AuthorizationEndDate.Value.Date >= today
                     && r.AuthorizationEndDate.Value.Date <= cutoff)
            .CountAsync(ct);

        var submittedThisMonth = await q
            .Where(r => r.SubmittedAt.HasValue && r.SubmittedAt.Value >= monthStart)
            .CountAsync(ct);

        var approvedThisMonth = await q
            .Where(r => r.Status == PaStatus.Approved
                     && r.DecisionRenderedAt.HasValue
                     && r.DecisionRenderedAt.Value >= monthStart)
            .CountAsync(ct);

        var deniedThisMonth = await q
            .Where(r => r.Status == PaStatus.Denied
                     && r.DecisionRenderedAt.HasValue
                     && r.DecisionRenderedAt.Value >= monthStart)
            .CountAsync(ct);

        return new DashboardSummary(
            openCount, pendingMyAction, overdue,
            expiringSoon, submittedThisMonth, approvedThisMonth, deniedThisMonth);
    }

    // ── Turnaround Time ───────────────────────────────────────────────────────

    /// <summary>
    /// Average calendar days from SubmittedAt to DecisionRenderedAt for Approved/Denied
    /// requests in the date range, grouped by priority (§12.2 report 5).
    /// </summary>
    public async Task<IReadOnlyList<TurnaroundSummary>> GetTurnaroundTimeAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        // Pull decided requests that have both timestamps and fall within the date range
        var rows = await db.PaRequests
            .AsNoTracking()
            .Where(r => (r.Status == PaStatus.Approved || r.Status == PaStatus.Denied)
                     && r.SubmittedAt.HasValue
                     && r.DecisionRenderedAt.HasValue
                     && r.DecisionRenderedAt >= fromDt
                     && r.DecisionRenderedAt <= toDt)
            .Select(r => new
            {
                Priority          = r.Priority.ToString(),
                SubmittedAt       = r.SubmittedAt!.Value,
                DecisionRenderedAt = r.DecisionRenderedAt!.Value,
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.Priority)
            .Select(g => new TurnaroundSummary(
                g.Key,
                AverageDays: Math.Round(g.Average(r => (r.DecisionRenderedAt - r.SubmittedAt).TotalDays), 1),
                Count: g.Count()))
            .OrderBy(s => s.Priority)
            .ToList();
    }

    // ── Provider Submission Volume ─────────────────────────────────────────────

    /// <summary>
    /// Returns submission counts per ProviderID within the date range, joined
    /// to ApplicationUser for display names (§12.2 report 6).
    /// </summary>
    public async Task<IReadOnlyList<ProviderVolumeSummary>> GetProviderVolumeAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        // Load raw rows client-side for InMemory DB compatibility;
        // in production SQL Server, EF translates this to a single join.
        var rows = await db.PaRequests
            .AsNoTracking()
            .Where(r => r.SubmittedAt.HasValue
                     && r.SubmittedAt >= fromDt
                     && r.SubmittedAt <= toDt)
            .Select(r => new { r.ProviderID, r.Status })
            .ToListAsync(ct);

        if (rows.Count == 0) return [];

        var providerIds = rows.Select(r => r.ProviderID).Distinct().ToList();

        // Resolve display names for all involved providers in one query
        var userNames = await db.Users
            .AsNoTracking()
            .Where(u => providerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName != "" ? u.FullName : (u.Email ?? u.Id), ct);

        return rows
            .GroupBy(r => r.ProviderID)
            .Select(g => new ProviderVolumeSummary(
                ProviderDisplayName: userNames.GetValueOrDefault(g.Key, g.Key),
                SubmissionCount:     g.Count(),
                ApprovedCount:       g.Count(r => r.Status == PaStatus.Approved),
                DeniedCount:         g.Count(r => r.Status == PaStatus.Denied)))
            .OrderByDescending(v => v.SubmissionCount)
            .ToList();
    }

    // ── Approval Rate (§12.2 report 3) ───────────────────────────────────────

    public async Task<IReadOnlyList<ApprovalRateSummary>> GetApprovalRateAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var fromDt = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        // Only Approved and Denied are "decided"; Withdrawn/Expired are excluded
        // from the denominator per PRD §12.2 to avoid skewing the approval rate.
        var rows = await db.PaRequests
            .AsNoTracking()
            .Where(r => (r.Status == PaStatus.Approved || r.Status == PaStatus.Denied)
                     && r.DecisionRenderedAt.HasValue
                     && r.DecisionRenderedAt >= fromDt
                     && r.DecisionRenderedAt <= toDt)
            .Select(r => new { r.Priority, r.Status })
            .ToListAsync(ct);

        if (rows.Count == 0) return [];

        return rows
            .GroupBy(r => r.Priority.ToString())
            .Select(g =>
            {
                var approved = g.Count(r => r.Status == PaStatus.Approved);
                var total    = g.Count();
                return new ApprovalRateSummary(
                    Priority:          g.Key,
                    Approved:          approved,
                    Denied:            total - approved,
                    Total:             total,
                    ApprovalRatePct:   total > 0 ? Math.Round(approved * 100.0 / total, 1) : 0);
            })
            .OrderBy(s => s.Priority)
            .ToList();
    }

    // ── Role-differentiated dashboard methods (§12.1, T-B3) ──────────────────

    public async Task<SpecialistDashboard> GetSpecialistDashboardAsync(
        string specialistId, CancellationToken ct = default)
    {
        await using var db  = await _dbFactory.CreateDbContextAsync(ct);
        var today           = _clock.UtcNow.Date;
        var monthStart      = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var thirtyDaysAhead = today.AddDays(30);

        var q = db.PaRequests.AsNoTracking().Where(r => r.SubmittedByUserID == specialistId);

        return new SpecialistDashboard(
            MyDrafts:            await q.Where(r => r.Status == PaStatus.Draft).CountAsync(ct),
            MySubmitted:         await q.Where(r => r.Status == PaStatus.Submitted).CountAsync(ct),
            MyPendingInfo:       await q.Where(r => r.Status == PaStatus.PendingInfo).CountAsync(ct),
            MyApprovedThisMonth: await q.Where(r => r.Status == PaStatus.Approved
                                              && r.DecisionRenderedAt >= monthStart).CountAsync(ct),
            MyDeniedThisMonth:   await q.Where(r => r.Status == PaStatus.Denied
                                              && r.DecisionRenderedAt >= monthStart).CountAsync(ct),
            MyExpiringSoon:      await q.Where(r => r.Status == PaStatus.Approved
                                              && r.AuthorizationEndDate.HasValue
                                              && r.AuthorizationEndDate.Value.Date >= today
                                              && r.AuthorizationEndDate.Value.Date <= thirtyDaysAhead).CountAsync(ct),
            MyOverdueRequests:   await q.Where(r => !TerminalStatuses.Contains(r.Status)
                                              && r.DecisionDueDate.HasValue
                                              && r.DecisionDueDate.Value.Date < today).CountAsync(ct));
    }

    public async Task<ProviderDashboard> GetProviderDashboardAsync(
        string providerId, CancellationToken ct = default)
    {
        await using var db  = await _dbFactory.CreateDbContextAsync(ct);
        var today           = _clock.UtcNow.Date;
        var monthStart      = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var thirtyDaysAhead = today.AddDays(30);

        var q = db.PaRequests.AsNoTracking().Where(r => r.ProviderID == providerId);

        return new ProviderDashboard(
            MySubmitted:         await q.Where(r => r.Status == PaStatus.Submitted).CountAsync(ct),
            MyUnderReview:       await q.Where(r => r.Status == PaStatus.UnderReview).CountAsync(ct),
            MyPendingInfo:       await q.Where(r => r.Status == PaStatus.PendingInfo).CountAsync(ct),
            MyApprovedThisMonth: await q.Where(r => r.Status == PaStatus.Approved
                                              && r.DecisionRenderedAt >= monthStart).CountAsync(ct),
            MyExpiringSoon:      await q.Where(r => r.Status == PaStatus.Approved
                                              && r.AuthorizationEndDate.HasValue
                                              && r.AuthorizationEndDate.Value.Date >= today
                                              && r.AuthorizationEndDate.Value.Date <= thirtyDaysAhead).CountAsync(ct));
    }

    public async Task<ReviewerDashboard> GetReviewerDashboardAsync(
        string reviewerId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var today          = _clock.UtcNow.Date;
        var monthStart     = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var pending5Cutoff = today.AddDays(-5); // requests submitted on or before this date are >5 days old

        var q = db.PaRequests.AsNoTracking();

        // Average decision time this month: load the small set into memory to avoid
        // EF InMemory DateDiff incompatibility; on SQL Server this is typically < 100 rows.
        var decidedThisMonth = await q
            .Where(r => (r.Status == PaStatus.Approved || r.Status == PaStatus.Denied)
                     && r.DecisionRenderedAt >= monthStart
                     && r.SubmittedAt.HasValue
                     && r.DecisionRenderedAt.HasValue)
            .Select(r => new { r.SubmittedAt, r.DecisionRenderedAt })
            .ToListAsync(ct);

        var avgDecisionDays = decidedThisMonth.Count > 0
            ? decidedThisMonth.Average(r => (r.DecisionRenderedAt!.Value - r.SubmittedAt!.Value).TotalDays)
            : 0.0;

        return new ReviewerDashboard(
            AwaitingReview:         await q.Where(r => r.Status == PaStatus.Submitted
                                                    || r.Status == PaStatus.Appealed).CountAsync(ct),
            UnderReviewByMe:        await q.Where(r => r.Status == PaStatus.UnderReview
                                                    && r.ReviewerUserID == reviewerId).CountAsync(ct),
            PendingInfoTotal:       await q.Where(r => r.Status == PaStatus.PendingInfo).CountAsync(ct),
            ApprovedThisMonth:      await q.Where(r => r.Status == PaStatus.Approved
                                                    && r.DecisionRenderedAt >= monthStart).CountAsync(ct),
            DeniedThisMonth:        await q.Where(r => r.Status == PaStatus.Denied
                                                    && r.DecisionRenderedAt >= monthStart).CountAsync(ct),
            OverdueRequests:        await q.Where(r =>
                                         (r.Status == PaStatus.Submitted
                                       || r.Status == PaStatus.UnderReview
                                       || r.Status == PaStatus.PendingInfo)
                                       && r.DecisionDueDate.HasValue
                                       && r.DecisionDueDate.Value.Date < today).CountAsync(ct),
            PendingMoreThan5Days:   await q.Where(r =>
                                         (r.Status == PaStatus.Submitted || r.Status == PaStatus.UnderReview)
                                       && r.SubmittedAt.HasValue
                                       && r.SubmittedAt.Value.Date <= pending5Cutoff).CountAsync(ct),
            AvgDecisionDaysThisMonth: Math.Round(avgDecisionDays, 1));
    }

    public async Task<BillingDashboard> GetBillingDashboardAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var today           = _clock.UtcNow.Date;
        var monthStart      = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var thirtyDaysAhead = today.AddDays(30);

        var q = db.PaRequests.AsNoTracking();

        var approvedActive   = await q.Where(r => r.Status == PaStatus.Approved).CountAsync(ct);
        var expiringSoon     = await q.Where(r => r.Status == PaStatus.Approved
                                              && r.AuthorizationEndDate.HasValue
                                              && r.AuthorizationEndDate.Value.Date >= today
                                              && r.AuthorizationEndDate.Value.Date <= thirtyDaysAhead).CountAsync(ct);
        var expiredThisMonth = await q.Where(r => r.Status == PaStatus.Expired
                                              && r.DecisionRenderedAt >= monthStart).CountAsync(ct);
        var deniedMonth      = await q.Where(r => r.Status == PaStatus.Denied
                                              && r.DecisionRenderedAt >= monthStart).CountAsync(ct);
        var approvedMonth    = await q.Where(r => r.Status == PaStatus.Approved
                                              && r.DecisionRenderedAt >= monthStart).CountAsync(ct);
        var totalMonth       = approvedMonth + deniedMonth;
        var pendingRequests  = await q.Where(r => ActiveStatuses.Contains(r.Status)).CountAsync(ct);

        return new BillingDashboard(
            ApprovedActive:    approvedActive,
            ExpiringSoon:      expiringSoon,
            ExpiredThisMonth:  expiredThisMonth,
            DeniedThisMonth:   deniedMonth,
            ApprovedThisMonth: approvedMonth,
            ApprovalRatePct:   totalMonth > 0 ? Math.Round(approvedMonth * 100.0 / totalMonth, 1) : 0,
            PendingRequests:   pendingRequests);
    }

    public async Task<IReadOnlyList<RecentActivityItem>> GetRecentActivityAsync(
        string specialistId, int count = 10, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var results = await db.PaStatusHistories
            .AsNoTracking()
            .Where(h => h.PaRequest.SubmittedByUserID == specialistId)
            .OrderByDescending(h => h.ChangedAt)
            .Take(count)
            .Select(h => new RecentActivityItem(
                h.PaRequestID,
                h.PaRequest.RequestNumber,
                h.PaRequest.PatientName,
                h.ToStatus,
                h.ChangedAt))
            .ToListAsync(ct);

        return results;
    }
}
