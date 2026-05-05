using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Models;

// ── Reporting DTOs ────────────────────────────────────────────────────────────

/// <summary>Count of PA requests grouped by workflow status.</summary>
public sealed record RequestStatusSummary(
    PaStatus Status,
    int Count,
    int OverdueCount);

/// <summary>Denial counts by denial reason for a date range.</summary>
public sealed record DenialReasonSummary(
    string ReasonCode,
    string Description,
    int Count,
    double Percentage,
    bool IsAppealable);

/// <summary>Monthly submission/decision volumes for trend charts.</summary>
public sealed record MonthlyVolume(
    int Year,
    int Month,
    int Submitted,
    int Approved,
    int Denied);

/// <summary>Approved authorizations expiring within the look-ahead window.</summary>
public sealed record ExpiringAuthorization(
    int RequestId,
    string RequestNumber,
    string PatientName,
    string AuthorizationNumber,
    DateTime AuthorizationEndDate,
    int DaysUntilExpiry);

/// <summary>Dashboard KPI tile data (FR-009).</summary>
public sealed record DashboardSummary(
    int OpenRequests,
    int PendingMyAction,
    int OverdueRequests,
    int ExpiringSoon,
    int SubmittedThisMonth,
    int ApprovedThisMonth,
    int DeniedThisMonth);

/// <summary>
/// Average turnaround time (calendar days, Submitted → Decision) grouped by priority.
/// Used by the Turnaround Time report tab (§12.2 report 5).
/// </summary>
public sealed record TurnaroundSummary(
    string Priority,
    double AverageDays,
    int Count);

/// <summary>
/// Submission volume per provider for a date range.
/// Used by the Provider Submission Volume report tab (§12.2 report 6).
/// </summary>
public sealed record ProviderVolumeSummary(
    string ProviderDisplayName,
    int SubmissionCount,
    int ApprovedCount,
    int DeniedCount);

/// <summary>
/// Approval-rate breakdown for a date range, split by priority (§12.2 report 3).
/// Approval rate = Approved / (Approved + Denied + Withdrawn + Expired) for decided requests.
/// </summary>
public sealed record ApprovalRateSummary(
    string Priority,
    int Approved,
    int Denied,
    int Total,
    double ApprovalRatePct);

// ── Role-differentiated dashboard DTOs (§12.1, T-B3) ─────────────────────────

/// <summary>Dashboard panel for the Authorization Specialist role.</summary>
public sealed record SpecialistDashboard(
    int MyDrafts,
    int MySubmitted,
    int MyPendingInfo,
    int MyApprovedThisMonth,
    int MyDeniedThisMonth,
    int MyExpiringSoon,
    /// <summary>Count of the specialist's own active requests whose DecisionDueDate has passed (§12.1 GAP-01).</summary>
    int MyOverdueRequests);

/// <summary>Dashboard panel for the Treating Provider role.</summary>
public sealed record ProviderDashboard(
    int MySubmitted,
    int MyUnderReview,
    int MyPendingInfo,
    int MyApprovedThisMonth,
    int MyExpiringSoon);

/// <summary>Dashboard panel for the Payer Reviewer role.</summary>
public sealed record ReviewerDashboard(
    int AwaitingReview,
    int UnderReviewByMe,
    int PendingInfoTotal,
    int ApprovedThisMonth,
    int DeniedThisMonth,
    int OverdueRequests,
    /// <summary>Requests in Submitted or UnderReview status for more than 5 calendar days (§12.1 GAP-05).</summary>
    int PendingMoreThan5Days,
    /// <summary>Average days from SubmittedAt → DecisionRenderedAt for decisions made this month (§12.1 GAP-06). Zero when no decisions this month.</summary>
    double AvgDecisionDaysThisMonth);

/// <summary>Dashboard panel for the Billing Manager role.</summary>
public sealed record BillingDashboard(
    int ApprovedActive,
    int ExpiringSoon,
    int ExpiredThisMonth,
    int DeniedThisMonth,
    int ApprovedThisMonth,
    double ApprovalRatePct,
    /// <summary>Count of all requests currently in an active (non-terminal) status (§12.1 GAP-03).</summary>
    int PendingRequests);

// ── Specialist activity feed DTO (§12.1 GAP-02) ───────────────────────────────

/// <summary>
/// One entry in the specialist's recent activity feed (last N status transitions
/// on requests the specialist submitted).
/// </summary>
public sealed record RecentActivityItem(
    int RequestId,
    string RequestNumber,
    string PatientName,
    PaStatus NewStatus,
    DateTime OccurredAt);
