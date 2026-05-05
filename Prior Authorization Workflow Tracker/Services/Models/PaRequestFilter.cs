using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Models;

/// <summary>
/// Filter and pagination parameters for the PA request queue (FR-002, §8.2).
/// All fields are optional; null means "no filter on that dimension".
/// </summary>
public sealed class PaRequestFilter
{
    public PaStatus?   Status          { get; set; }
    public PaPriority? Priority        { get; set; }
    public int?        InsurancePlanID { get; set; }
    public int?        ProcedureCodeID { get; set; }
    public string?     ReviewerUserID  { get; set; }
    public string?     SubmittedByUserID { get; set; }
    public DateTime?   SubmittedFrom   { get; set; }
    public DateTime?   SubmittedTo     { get; set; }

    /// <summary>
    /// Optional free-text search across PatientName and PatientMrn (GAP-10, §8.2).
    /// Null or whitespace means no text filter.
    /// </summary>
    public string? PatientSearch { get; set; }

    /// <summary>
    /// Column to sort by. Accepted values: "SubmittedAt", "DecisionDueDate",
    /// "PatientName", "Priority". Defaults to "SubmittedAt".
    /// </summary>
    public string SortBy         { get; set; } = "SubmittedAt";
    public bool   SortDescending { get; set; } = true;

    public int Page     { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
