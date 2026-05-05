using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Orchestrates all business rule enforcement (BR-001–BR-014, §9).
/// Called by WorkflowService and PaRequestService before any state-changing operation.
/// Each method throws a typed exception on rule failure:
///   BusinessRuleViolationException — BR-xxx rule violated
///   WorkflowTransitionException    — illegal transition attempt
///   EntityNotFoundException        — referenced lookup not found
/// </summary>
public interface IBusinessRuleService
{
    /// <summary>
    /// Validates all rules required for the Draft → Submitted transition:
    ///   BR-001 — procedure code requires prior auth
    ///   BR-003 — ICD-10 diagnosis code format  (via PaRequestSubmitValidator)
    ///   BR-004 — units requested 1–999          (via PaRequestSubmitValidator)
    ///   BR-005 — no duplicate active request for same patient + procedure + plan
    ///   BR-010 — clinical justification ≥ 50 chars (via PaRequestSubmitValidator)
    /// </summary>
    Task ValidateForSubmissionAsync(PaRequest request, CancellationToken ct = default);

    /// <summary>
    /// Validates approval decision fields before UnderReview → Approved:
    ///   BR-006 — end date after start date      (via ApprovalDecisionValidator)
    ///   BR-007 — granted units ≤ requested units
    ///   BR-011 — start date ≥ today             (via ApprovalDecisionValidator)
    ///   BR-012 — granted units ≥ 1              (via ApprovalDecisionValidator)
    /// </summary>
    Task ValidateForApprovalAsync(PaRequest request, ApprovalDecisionModel decision, CancellationToken ct = default);

    /// <summary>
    /// Validates Denied → Appealed is within the appeal window (BR-008):
    ///   checks DenialReasons.IsAppealable and AppealDeadlineDays vs DecisionRenderedAt.
    /// </summary>
    Task ValidateAppealEligibilityAsync(PaRequest request, CancellationToken ct = default);

    /// <summary>
    /// Validates a request can be withdrawn (BR-009):
    ///   prohibits Withdrawn from Approved, Expired, Denied, Draft, or already Withdrawn.
    /// </summary>
    void ValidateCanWithdraw(PaRequest request);

    /// <summary>
    /// Validates PendingInfo → Submitted re-submission has new supporting content (BR-013):
    ///   reads PaStatusHistory for the last PendingInfo entry timestamp, then checks
    ///   whether any PaComment or PaDocument was added after that timestamp (BUG-014 mitigation).
    /// </summary>
    Task ValidateResubmissionAsync(int requestId, CancellationToken ct = default);

    /// <summary>
    /// Validates hard delete is permitted (BR-014):
    ///   request must be in Draft status; caller must be the creating Specialist or an Admin.
    /// </summary>
    void ValidateCanHardDelete(PaRequest request, string callerUserId, bool isAdmin);
}
