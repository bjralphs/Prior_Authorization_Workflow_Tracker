using System.ComponentModel.DataAnnotations;
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
    [Required(ErrorMessage = "Patient MRN is required.")]
    [MaxLength(50, ErrorMessage = "Patient MRN must be 50 characters or fewer.")]
    public string PatientMrn { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Insurance plan is required.")]
    public int InsurancePlanID { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Procedure code is required.")]
    public int ProcedureCodeID { get; set; }

    // ── Full fields required on submission ────────────────────────────────────
    [Required(ErrorMessage = "Patient name is required.")]
    [MaxLength(200, ErrorMessage = "Patient name must be 200 characters or fewer.")]
    public string PatientName { get; set; } = string.Empty;

    // PatientDob is validated manually in the form (parsed outside EditContext).
    public DateTime PatientDob { get; set; }

    [Required(ErrorMessage = "Treating provider is required.")]
    public string ProviderID { get; set; } = string.Empty;

    [Required(ErrorMessage = "Diagnosis code (ICD-10) is required.")]
    [MaxLength(10, ErrorMessage = "Diagnosis code must be 10 characters or fewer.")]
    [RegularExpression(@"^[A-Z]\d{2}(\.\d{1,4})?$", ErrorMessage = "Enter a valid ICD-10 code (e.g. M17.11).")]
    public string DiagnosisCode { get; set; } = string.Empty;

    /// <summary>BR-010: minimum 50 characters enforced on submission.</summary>
    [Required(ErrorMessage = "Clinical justification is required.")]
    [MinLength(50, ErrorMessage = "Clinical justification must be at least 50 characters.")]
    public string ClinicalJustification { get; set; } = string.Empty;

    public PaPriority Priority { get; set; } = PaPriority.Routine;

    /// <summary>BR-004: must be 1–999.</summary>
    [Range(1, 999, ErrorMessage = "Units requested must be between 1 and 999.")]
    public int ApprovedUnitsRequested { get; set; } = 1;
}
