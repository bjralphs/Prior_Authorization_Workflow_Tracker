using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>
/// Computes all reporting aggregates from the in-memory DemoDataStore using LINQ.
/// Mirrors the SQL views from the production service.
/// </summary>
public sealed class DemoReportingService : IReportingService
{
    private readonly DemoDataStore _store;

    public DemoReportingService(DemoDataStore store) => _store = store;

    private static readonly PaStatus[] TerminalStatuses =
        [PaStatus.Approved, PaStatus.Denied, PaStatus.Expired, PaStatus.Withdrawn];

    public Task<IReadOnlyList<RequestStatusSummary>> GetStatusSummaryAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var result = _store.Requests
            .GroupBy(r => r.Status)
            .Select(g => new RequestStatusSummary(
                g.Key,
                g.Count(),
                g.Count(r => !TerminalStatuses.Contains(r.Status) &&
                              r.DecisionDueDate.HasValue && r.DecisionDueDate < now)))
            .ToList();
        return Task.FromResult<IReadOnlyList<RequestStatusSummary>>(result);
    }

    public Task<IReadOnlyList<DenialReasonSummary>> GetDenialsByReasonAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue);

        var denied = _store.Requests
            .Where(r => r.Status == PaStatus.Denied &&
                        r.DecisionRenderedAt >= fromDt && r.DecisionRenderedAt <= toDt &&
                        r.DenialReasonID.HasValue)
            .ToList();

        var total = denied.Count;
        var result = denied
            .GroupBy(r => r.DenialReasonID!.Value)
            .Select(g =>
            {
                var reason = _store.DenialReasons.FirstOrDefault(d => d.ID == g.Key);
                return new DenialReasonSummary(
                    reason?.Code        ?? "UNKNOWN",
                    reason?.Description ?? "Unknown",
                    g.Count(),
                    total == 0 ? 0 : Math.Round(g.Count() * 100.0 / total, 1),
                    reason?.IsAppealable ?? false);
            })
            .OrderByDescending(s => s.Count)
            .ToList();

        return Task.FromResult<IReadOnlyList<DenialReasonSummary>>(result);
    }

    public Task<IReadOnlyList<MonthlyVolume>> GetMonthlyVolumeAsync(int months = 12, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-months);
        var result = new List<MonthlyVolume>();

        for (int i = months - 1; i >= 0; i--)
        {
            var target = DateTime.UtcNow.AddMonths(-i);
            var yr = target.Year;
            var mo = target.Month;

            var submitted = _store.Requests.Count(r =>
                r.SubmittedAt.HasValue && r.SubmittedAt.Value.Year == yr && r.SubmittedAt.Value.Month == mo);
            var approved = _store.Requests.Count(r =>
                r.Status == PaStatus.Approved &&
                r.DecisionRenderedAt.HasValue &&
                r.DecisionRenderedAt.Value.Year == yr && r.DecisionRenderedAt.Value.Month == mo);
            var denied = _store.Requests.Count(r =>
                r.Status == PaStatus.Denied &&
                r.DecisionRenderedAt.HasValue &&
                r.DecisionRenderedAt.Value.Year == yr && r.DecisionRenderedAt.Value.Month == mo);

            result.Add(new MonthlyVolume(yr, mo, submitted, approved, denied));
        }

        return Task.FromResult<IReadOnlyList<MonthlyVolume>>(result);
    }

    public Task<IReadOnlyList<ExpiringAuthorization>> GetExpiringAuthorizationsAsync(
        int daysAhead = 30, CancellationToken ct = default)
    {
        var now     = DateTime.UtcNow;
        var cutoff  = now.AddDays(daysAhead);
        var result  = _store.Requests
            .Where(r => r.Status == PaStatus.Approved &&
                        r.AuthorizationEndDate.HasValue &&
                        r.AuthorizationEndDate.Value >= now &&
                        r.AuthorizationEndDate.Value <= cutoff)
            .OrderBy(r => r.AuthorizationEndDate)
            .Select(r => new ExpiringAuthorization(
                r.ID,
                r.RequestNumber,
                r.PatientName,
                r.AuthorizationNumber ?? string.Empty,
                r.AuthorizationEndDate!.Value,
                (int)(r.AuthorizationEndDate.Value - now).TotalDays))
            .ToList();

        return Task.FromResult<IReadOnlyList<ExpiringAuthorization>>(result);
    }

    public Task<DashboardSummary> GetDashboardSummaryAsync(string myUserId, CancellationToken ct = default)
    {
        var now         = DateTime.UtcNow;
        var monthStart  = new DateTime(now.Year, now.Month, 1);
        var allActive   = _store.Requests.Where(r => !TerminalStatuses.Contains(r.Status)).ToList();
        var result = new DashboardSummary(
            OpenRequests:       allActive.Count,
            PendingMyAction:    _store.Requests.Count(r =>
                                    r.ReviewerUserID == myUserId &&
                                    r.Status is PaStatus.Submitted or PaStatus.UnderReview),
            OverdueRequests:    allActive.Count(r => r.DecisionDueDate.HasValue && r.DecisionDueDate < now),
            ExpiringSoon:       _store.Requests.Count(r =>
                                    r.Status == PaStatus.Approved &&
                                    r.AuthorizationEndDate.HasValue &&
                                    r.AuthorizationEndDate.Value >= now &&
                                    r.AuthorizationEndDate.Value <= now.AddDays(30)),
            SubmittedThisMonth: _store.Requests.Count(r => r.SubmittedAt >= monthStart),
            ApprovedThisMonth:  _store.Requests.Count(r => r.Status == PaStatus.Approved && r.DecisionRenderedAt >= monthStart),
            DeniedThisMonth:    _store.Requests.Count(r => r.Status == PaStatus.Denied   && r.DecisionRenderedAt >= monthStart));

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<TurnaroundSummary>> GetTurnaroundTimeAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue);

        var decided = _store.Requests
            .Where(r => r.Status is PaStatus.Approved or PaStatus.Denied &&
                        r.SubmittedAt.HasValue && r.DecisionRenderedAt.HasValue &&
                        r.DecisionRenderedAt >= fromDt && r.DecisionRenderedAt <= toDt)
            .ToList();

        var result = decided
            .GroupBy(r => r.Priority.ToString())
            .Select(g => new TurnaroundSummary(
                g.Key,
                g.Average(r => (r.DecisionRenderedAt!.Value - r.SubmittedAt!.Value).TotalDays),
                g.Count()))
            .ToList();

        return Task.FromResult<IReadOnlyList<TurnaroundSummary>>(result);
    }

    public Task<IReadOnlyList<ProviderVolumeSummary>> GetProviderVolumeAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue);

        var result = _store.Requests
            .Where(r => r.SubmittedAt >= fromDt && r.SubmittedAt <= toDt)
            .GroupBy(r => r.ProviderID)
            .Select(g =>
            {
                var user = _store.Users.FirstOrDefault(u => u.Id == g.Key);
                return new ProviderVolumeSummary(
                    user?.FullName ?? g.Key,
                    g.Count(),
                    g.Count(r => r.Status == PaStatus.Approved),
                    g.Count(r => r.Status == PaStatus.Denied));
            })
            .OrderByDescending(s => s.SubmissionCount)
            .ToList();

        return Task.FromResult<IReadOnlyList<ProviderVolumeSummary>>(result);
    }

    public Task<IReadOnlyList<ApprovalRateSummary>> GetApprovalRateAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue);

        var decided = _store.Requests
            .Where(r => r.Status is PaStatus.Approved or PaStatus.Denied &&
                        r.DecisionRenderedAt >= fromDt && r.DecisionRenderedAt <= toDt)
            .ToList();

        var result = decided
            .GroupBy(r => r.Priority.ToString())
            .Select(g =>
            {
                var approved = g.Count(r => r.Status == PaStatus.Approved);
                var denied   = g.Count(r => r.Status == PaStatus.Denied);
                var total    = g.Count();
                return new ApprovalRateSummary(
                    g.Key, approved, denied, total,
                    total == 0 ? 0 : Math.Round(approved * 100.0 / total, 1));
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<ApprovalRateSummary>>(result);
    }

    public Task<SpecialistDashboard> GetSpecialistDashboardAsync(string specialistId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var mine = _store.Requests.Where(r => r.SubmittedByUserID == specialistId).ToList();

        return Task.FromResult(new SpecialistDashboard(
            MyDrafts:           mine.Count(r => r.Status == PaStatus.Draft),
            MySubmitted:        mine.Count(r => r.Status == PaStatus.Submitted),
            MyPendingInfo:      mine.Count(r => r.Status == PaStatus.PendingInfo),
            MyApprovedThisMonth: mine.Count(r => r.Status == PaStatus.Approved && r.DecisionRenderedAt >= monthStart),
            MyDeniedThisMonth:  mine.Count(r => r.Status == PaStatus.Denied    && r.DecisionRenderedAt >= monthStart),
            MyExpiringSoon:     mine.Count(r => r.Status == PaStatus.Approved &&
                                               r.AuthorizationEndDate >= now &&
                                               r.AuthorizationEndDate <= now.AddDays(30)),
            MyOverdueRequests:  mine.Count(r => !TerminalStatuses.Contains(r.Status) &&
                                               r.DecisionDueDate < now)));
    }

    public Task<ProviderDashboard> GetProviderDashboardAsync(string providerId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var mine = _store.Requests.Where(r => r.ProviderID == providerId).ToList();

        return Task.FromResult(new ProviderDashboard(
            MySubmitted:        mine.Count(r => r.Status == PaStatus.Submitted),
            MyUnderReview:      mine.Count(r => r.Status == PaStatus.UnderReview),
            MyPendingInfo:      mine.Count(r => r.Status == PaStatus.PendingInfo),
            MyApprovedThisMonth: mine.Count(r => r.Status == PaStatus.Approved && r.DecisionRenderedAt >= monthStart),
            MyExpiringSoon:     mine.Count(r => r.Status == PaStatus.Approved &&
                                               r.AuthorizationEndDate >= now &&
                                               r.AuthorizationEndDate <= now.AddDays(30))));
    }

    public Task<ReviewerDashboard> GetReviewerDashboardAsync(string reviewerId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var all = _store.Requests.ToList();
        var mine = all.Where(r => r.ReviewerUserID == reviewerId).ToList();

        var pendingMore5 = all
            .Where(r => r.Status is PaStatus.Submitted or PaStatus.UnderReview &&
                        r.SubmittedAt.HasValue &&
                        (now - r.SubmittedAt.Value).TotalDays > 5)
            .Count();

        var decisionsThisMonth = mine
            .Where(r => r.Status is PaStatus.Approved or PaStatus.Denied &&
                        r.DecisionRenderedAt >= monthStart &&
                        r.SubmittedAt.HasValue)
            .ToList();

        var avgDays = decisionsThisMonth.Count == 0
            ? 0
            : decisionsThisMonth.Average(r => (r.DecisionRenderedAt!.Value - r.SubmittedAt!.Value).TotalDays);

        return Task.FromResult(new ReviewerDashboard(
            AwaitingReview:          all.Count(r => r.Status == PaStatus.Submitted),
            UnderReviewByMe:         mine.Count(r => r.Status == PaStatus.UnderReview),
            PendingInfoTotal:        all.Count(r => r.Status == PaStatus.PendingInfo),
            ApprovedThisMonth:       mine.Count(r => r.Status == PaStatus.Approved && r.DecisionRenderedAt >= monthStart),
            DeniedThisMonth:         mine.Count(r => r.Status == PaStatus.Denied   && r.DecisionRenderedAt >= monthStart),
            OverdueRequests:         all.Count(r => !TerminalStatuses.Contains(r.Status) && r.DecisionDueDate < now),
            PendingMoreThan5Days:    pendingMore5,
            AvgDecisionDaysThisMonth: Math.Round(avgDays, 1)));
    }

    public Task<BillingDashboard> GetBillingDashboardAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var all = _store.Requests.ToList();

        var decided = all.Where(r => r.Status is PaStatus.Approved or PaStatus.Denied &&
                                     r.DecisionRenderedAt >= monthStart).ToList();
        var approvedThisMonth = decided.Count(r => r.Status == PaStatus.Approved);
        var total = decided.Count;
        double approvalRate = total == 0 ? 0 : Math.Round(approvedThisMonth * 100.0 / total, 1);

        return Task.FromResult(new BillingDashboard(
            ApprovedActive:    all.Count(r => r.Status == PaStatus.Approved),
            ExpiringSoon:      all.Count(r => r.Status == PaStatus.Approved &&
                                             r.AuthorizationEndDate >= now &&
                                             r.AuthorizationEndDate <= now.AddDays(30)),
            ExpiredThisMonth:  all.Count(r => r.Status == PaStatus.Expired && r.UpdatedAt >= monthStart),
            DeniedThisMonth:   all.Count(r => r.Status == PaStatus.Denied  && r.DecisionRenderedAt >= monthStart),
            ApprovedThisMonth: approvedThisMonth,
            ApprovalRatePct:   approvalRate,
            PendingRequests:   all.Count(r => !TerminalStatuses.Contains(r.Status))));
    }

    public Task<IReadOnlyList<RecentActivityItem>> GetRecentActivityAsync(
        string specialistId, int count = 10, CancellationToken ct = default)
    {
        var myRequestIds = _store.Requests
            .Where(r => r.SubmittedByUserID == specialistId)
            .Select(r => r.ID)
            .ToHashSet();

        IReadOnlyList<RecentActivityItem> result = _store.StatusHistory
            .Where(h => myRequestIds.Contains(h.PaRequestID) &&
                        h.ToStatus != PaStatus.Draft)
            .OrderByDescending(h => h.ChangedAt)
            .Take(count)
            .Select(h =>
            {
                var req = _store.Requests.FirstOrDefault(r => r.ID == h.PaRequestID);
                return new RecentActivityItem(
                    h.PaRequestID,
                    req?.RequestNumber ?? $"PA-{h.PaRequestID}",
                    req?.PatientName   ?? "Unknown",
                    h.ToStatus,
                    h.ChangedAt);
            })
            .ToList();

        return Task.FromResult(result);
    }
}
