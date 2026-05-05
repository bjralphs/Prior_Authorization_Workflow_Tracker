namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>
/// Immutable record of every status transition on a PaRequest (§7.1).
/// Written by WorkflowService on every valid transition.
/// </summary>
public class PaStatusHistory
{
    public int ID { get; set; }

    public int PaRequestID { get; set; }

    public PaStatus FromStatus { get; set; }

    public PaStatus ToStatus { get; set; }

    /// <summary>
    /// FK to AspNetUsers. For system-initiated transitions (ExpirationJob)
    /// this is the reserved SYSTEM user ID (§8.6).
    /// </summary>
    public string ChangedByUserID { get; set; } = string.Empty;

    /// <summary>Optional free-text reason for the transition.</summary>
    public string? ChangeReason { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public PaRequest PaRequest { get; set; } = null!;
    public ApplicationUser ChangedByUser { get; set; } = null!;
}
