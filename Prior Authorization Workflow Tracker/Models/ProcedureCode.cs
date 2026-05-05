namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>
/// CPT procedure code. BR-001: only codes with RequiresPriorAuth = true may be submitted.
/// TypicalAuthorizationDurationDays is distinct from the payer SLA — it pre-populates
/// AuthorizationEndDate on approval (§7.1 column notes).
/// </summary>
public class ProcedureCode
{
    public int ID { get; set; }

    /// <summary>CPT code, e.g. "99213".</summary>
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>If false, submission is rejected with BR-001.</summary>
    public bool RequiresPriorAuth { get; set; } = true;

    /// <summary>
    /// Expected approved authorization duration in days; used to suggest
    /// AuthorizationEndDate = AuthorizationStartDate + this value on approval.
    /// </summary>
    public int TypicalAuthorizationDurationDays { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<PaRequest> PaRequests { get; set; } = [];
}
