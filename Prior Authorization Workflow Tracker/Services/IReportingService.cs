using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Read-only aggregate queries for dashboards, charts, and exports (§12.2, FR-009).
/// All methods use AsNoTracking() (NFR-002). Backed by LINQ-to-SQL queries whose
/// equivalents are also expressed as SQL views (vw_*) in the migration for SQL Server
/// optimizer hints and reduced round-trips in reporting contexts.
/// </summary>
public interface IReportingService
{
    /// <summary>
    /// Returns count of PA requests grouped by status, with overdue flag
    /// (DecisionDueDate &lt; today for non-terminal statuses).
    /// Used by the Dashboard KPI tiles.
    /// </summary>
    Task<IReadOnlyList<RequestStatusSummary>> GetStatusSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns denial counts by denial reason within the specified date range.
    /// DateOnly parameters use UTC date boundaries.
    /// </summary>
    Task<IReadOnlyList<DenialReasonSummary>> GetDenialsByReasonAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Returns monthly submission/approval/denial volumes for the last <paramref name="months"/> months.
    /// Used by the trend chart on the Reports page.
    /// </summary>
    Task<IReadOnlyList<MonthlyVolume>> GetMonthlyVolumeAsync(
        int months = 12, CancellationToken ct = default);

    /// <summary>
    /// Returns approved authorizations expiring within the next <paramref name="daysAhead"/> days.
    /// Used by the Dashboard expiring-soon tile and the Reports page.
    /// </summary>
    Task<IReadOnlyList<ExpiringAuthorization>> GetExpiringAuthorizationsAsync(
        int daysAhead = 30, CancellationToken ct = default);

    /// <summary>
    /// Returns KPI tile summary for the Dashboard (FR-009).
    /// The <paramref name="myUserId"/> parameter scopes "PendingMyAction" to the caller.
    /// </summary>
    Task<DashboardSummary> GetDashboardSummaryAsync(
        string myUserId, CancellationToken ct = default);

    /// <summary>
    /// Returns average turnaround days (SubmittedAt → DecisionRenderedAt) for
    /// Approved and Denied requests grouped by priority within the date range.
    /// Used by the Turnaround Time report tab (§12.2 report 5).
    /// </summary>
    Task<IReadOnlyList<TurnaroundSummary>> GetTurnaroundTimeAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Returns submission counts per provider (ProviderID) within the date range.
    /// Used by the Provider Submission Volume report tab (§12.2 report 6).
    /// </summary>
    Task<IReadOnlyList<ProviderVolumeSummary>> GetProviderVolumeAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Returns approval rate (approved/decided %) broken down by priority for the
    /// given date range. "Decided" = Approved + Denied requests in the window.
    /// Used by the Approval Rate report tab (§12.2 report 3).
    /// </summary>
    Task<IReadOnlyList<ApprovalRateSummary>> GetApprovalRateAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

    // ── Role-differentiated dashboard methods (§12.1, T-B3) ──────────────────

    /// <summary>Dashboard KPIs scoped to a single specialist's requests.</summary>
    Task<SpecialistDashboard> GetSpecialistDashboardAsync(
        string specialistId, CancellationToken ct = default);

    /// <summary>Dashboard KPIs scoped to a single provider's patient requests.</summary>
    Task<ProviderDashboard> GetProviderDashboardAsync(
        string providerId, CancellationToken ct = default);

    /// <summary>Dashboard KPIs for the Payer Reviewer (all pending-review work).</summary>
    Task<ReviewerDashboard> GetReviewerDashboardAsync(
        string reviewerId, CancellationToken ct = default);

    /// <summary>Dashboard KPIs for the Billing Manager (financial-focused view).</summary>
    Task<BillingDashboard> GetBillingDashboardAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the last <paramref name="count"/> status transitions on requests submitted
    /// by <paramref name="specialistId"/>, ordered newest-first (§12.1 GAP-02).
    /// </summary>
    Task<IReadOnlyList<RecentActivityItem>> GetRecentActivityAsync(
        string specialistId, int count = 10, CancellationToken ct = default);
}
