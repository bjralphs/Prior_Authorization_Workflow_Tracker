namespace Prior_Authorization_Workflow_Tracker.Services.Models;

/// <summary>
/// Fields required when a Payer Reviewer approves a PA request (§8.5).
/// Validated by ApprovalDecisionValidator covering BR-006, BR-011, BR-012;
/// BR-007 (granted ≤ requested) enforced in BusinessRuleService.
/// </summary>
public sealed class ApprovalDecisionModel
{
    /// <summary>BR-012: must be ≥ 1. BR-007: must not exceed PaRequest.ApprovedUnitsRequested.</summary>
    public int ApprovedUnitsGranted { get; set; }

    /// <summary>BR-011: must be ≥ today (checked against IDateTimeProvider.UtcNow).</summary>
    public DateTime AuthorizationStartDate { get; set; }

    /// <summary>BR-006: must be after AuthorizationStartDate.</summary>
    public DateTime AuthorizationEndDate { get; set; }
}
