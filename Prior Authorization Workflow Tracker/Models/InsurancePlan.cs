namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>
/// Insurance plan from which PA SLA deadlines are derived (BR-002, §7.1).
/// </summary>
public class InsurancePlan
{
    public int ID { get; set; }

    public string PlanName { get; set; } = string.Empty;

    public string PayerName { get; set; } = string.Empty;

    /// <summary>HMO | PPO | EPO | Medicare | Medicaid</summary>
    public string PlanType { get; set; } = string.Empty;

    /// <summary>Calendar days from submission to decision for Routine priority (BR-002).</summary>
    public int RoutineDecisionDays { get; set; }

    /// <summary>Calendar days for Urgent priority (BR-002).</summary>
    public int UrgentDecisionDays { get; set; }

    /// <summary>Calendar days for Emergent priority; default 1 (BR-002).</summary>
    public int EmergentDecisionDays { get; set; } = 1;

    public string? PhoneNumber { get; set; }

    public string? FaxNumber { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<PaRequest> PaRequests { get; set; } = [];
}
