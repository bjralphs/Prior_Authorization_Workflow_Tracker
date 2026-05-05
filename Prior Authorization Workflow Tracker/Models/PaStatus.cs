namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>
/// Lifecycle states for a Prior Authorization request.
/// All valid transitions are enforced by WorkflowService.
/// </summary>
public enum PaStatus
{
    Draft,
    Submitted,
    PendingInfo,
    UnderReview,
    Approved,
    Denied,
    Appealed,
    Expired,
    Withdrawn
}
