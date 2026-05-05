using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Models;

/// <summary>
/// DTO for creating or updating a PA request in Draft or PendingInfo state.
/// Minimum 3 fields are required for a Draft save (§8.1):
///   PatientMrn, InsurancePlanID, ProcedureCodeID.
/// All remaining fields are validated by WorkflowService.SubmitAsync on submission.
/// </summary>
public sealed class CreatePaRequestModel
{
    // ── Minimum fields for Draft save ─────────────────────────────────────────
    public string PatientMrn { get; set; } = string.Empty;
    public int InsurancePlanID { get; set; }
    public int ProcedureCodeID { get; set; }

    // ── Full fields required on submission ────────────────────────────────────
    public string PatientName { get; set; } = string.Empty;
    public DateTime PatientDob { get; set; }
    public string ProviderID { get; set; } = string.Empty;
    public string DiagnosisCode { get; set; } = string.Empty;

    /// <summary>BR-010: minimum 50 characters enforced on submission.</summary>
    public string ClinicalJustification { get; set; } = string.Empty;

    public PaPriority Priority { get; set; } = PaPriority.Routine;

    /// <summary>BR-004: must be 1–999.</summary>
    public int ApprovedUnitsRequested { get; set; }
}
