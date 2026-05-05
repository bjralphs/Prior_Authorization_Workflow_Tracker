namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>
/// Lookup table of standardized denial reason codes (§7.1).
/// IsAppealable and AppealDeadlineDays drive BR-008 logic in WorkflowService.
/// </summary>
public class DenialReason
{
    public int ID { get; set; }

    /// <summary>Short code, e.g. "MED-NOT-NECESSARY".</summary>
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsAppealable { get; set; } = true;

    /// <summary>
    /// Calendar days from DecisionRenderedAt within which Specialist may file an appeal (BR-008).
    /// </summary>
    public int AppealDeadlineDays { get; set; }

    // Navigation
    public ICollection<PaRequest> PaRequests { get; set; } = [];
}
