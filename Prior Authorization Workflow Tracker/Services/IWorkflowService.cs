using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Enforces the PA request state machine (FR-004, FR-005, FR-007, §8.4–§8.7).
///
/// Each method:
///   1. Verifies the caller is active (BUG-012 mitigation)
///   2. Checks the caller's role against the transition permission table
///   3. Loads the PaRequest and checks it is in the expected status
///   4. Calls IBusinessRuleService for BR-specific validation
///   5. Applies field changes, transitions the status, writes PaStatusHistory
///   6. Calls IAuditService (fire-and-forget; never blocks the operation)
///   7. Calls INotificationService (fire-and-forget; never blocks)
///   8. Returns the updated PaRequest entity
///
/// Throws:
///   WorkflowTransitionException    — transition not in valid state machine
///   BusinessRuleViolationException — a BR-xxx rule was violated
///   AuthorizationException         — caller lacks the required role or is inactive
///   EntityNotFoundException        — the request or a referenced lookup was not found
///   DbUpdateConcurrencyException   — optimistic concurrency conflict on SaveChanges
/// </summary>
public interface IWorkflowService
{
    /// <summary>
    /// Draft → Submitted (Specialist / Admin).
    /// Validates BR-001, BR-003, BR-004, BR-005, BR-010.
    /// Calculates DecisionDueDate from plan SLA × priority (BR-002).
    /// </summary>
    Task<PaRequest> SubmitAsync(int requestId, CancellationToken ct = default);

    /// <summary>
    /// Submitted → UnderReview (Reviewer / Admin).
    /// Records the reviewer on the request.
    /// </summary>
    Task<PaRequest> BeginReviewAsync(int requestId, CancellationToken ct = default);

    /// <summary>
    /// UnderReview → Approved (Reviewer / Admin).
    /// Validates BR-006, BR-007, BR-011, BR-012.
    /// Generates AuthorizationNumber in format AUTH-YYYYMMDD-XXXXX.
    /// Notifies the submitting Specialist.
    /// </summary>
    Task<PaRequest> ApproveAsync(int requestId, ApprovalDecisionModel decision, CancellationToken ct = default);

    /// <summary>
    /// UnderReview → Denied (Reviewer / Admin).
    /// Notifies the submitting Specialist including appeal window if applicable.
    /// </summary>
    Task<PaRequest> DenyAsync(int requestId, int denialReasonId, string? notes, CancellationToken ct = default);

    /// <summary>
    /// Submitted | UnderReview → PendingInfo (Reviewer / Admin).
    /// Notifies the submitting Specialist.
    /// </summary>
    Task<PaRequest> RequestAdditionalInfoAsync(int requestId, string reason, CancellationToken ct = default);

    /// <summary>
    /// PendingInfo → Submitted (Specialist / Admin).
    /// Validates BR-013 (new content required).
    /// Recalculates DecisionDueDate.
    /// </summary>
    Task<PaRequest> ResubmitAsync(int requestId, CancellationToken ct = default);

    /// <summary>
    /// Denied → Appealed (Specialist / Admin).
    /// Validates BR-008 (appeal deadline).
    /// </summary>
    Task<PaRequest> AppealAsync(int requestId, CancellationToken ct = default);

    /// <summary>
    /// Submitted | PendingInfo | UnderReview | Appealed → Withdrawn (Specialist / Provider / Admin).
    /// Validates BR-009 (withdrawal prohibited from Approved, Expired, Denied).
    /// </summary>
    Task<PaRequest> WithdrawAsync(int requestId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Approved → Expired. Called by ExpirationJob using the SYSTEM user — not role-checked.
    /// Notifies the submitting Specialist and assigned Provider.
    /// </summary>
    Task<PaRequest> ExpireAsync(int requestId, CancellationToken ct = default);

    /// <summary>
    /// Admin-only: forces a request to any target status, bypassing normal workflow transitions (§8.8).
    /// Requires: Admin role, the request must exist, and a non-empty justification.
    /// Writes "AdminOverride" audit entry per §11 logged events.
    /// </summary>
    Task<PaRequest> AdminOverrideAsync(
        int requestId,
        PaStatus newStatus,
        string justification,
        CancellationToken ct = default);

    /// <summary>Returns true when the (from → to) pair exists in the valid transition table.</summary>
    bool IsTransitionValid(PaStatus from, PaStatus to);
}
