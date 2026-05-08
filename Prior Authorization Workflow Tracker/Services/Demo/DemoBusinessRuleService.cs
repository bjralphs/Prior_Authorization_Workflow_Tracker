using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>
/// In-memory implementation of business rules for the demo.
/// Runs the same logical checks as the production service but against
/// in-memory data instead of EF Core queries.
/// </summary>
public sealed class DemoBusinessRuleService : IBusinessRuleService
{
    private readonly DemoDataStore _store;

    public DemoBusinessRuleService(DemoDataStore store) => _store = store;

    public Task ValidateForSubmissionAsync(PaRequest request, CancellationToken ct = default)
    {
        // BR-001: procedure requires prior auth
        var proc = _store.ProcedureCodes.FirstOrDefault(p => p.ID == request.ProcedureCodeID);
        if (proc != null && !proc.RequiresPriorAuth)
            throw new BusinessRuleViolationException("BR-001",
                $"Procedure {proc.Code} does not require prior authorization.");

        // BR-003: ICD-10 format (basic check)
        if (string.IsNullOrWhiteSpace(request.DiagnosisCode) ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.DiagnosisCode, @"^[A-Z]\d{2}(\.\d{1,4})?$"))
            throw new BusinessRuleViolationException("BR-003", "Invalid ICD-10 diagnosis code format.");

        // BR-004: units 1-999
        if (request.ApprovedUnitsRequested is < 1 or > 999)
            throw new BusinessRuleViolationException("BR-004", "Units requested must be between 1 and 999.");

        // BR-005: no duplicate active request
        bool duplicate = _store.Requests.Any(r =>
            r.ID != request.ID &&
            r.PatientMrn == request.PatientMrn &&
            r.InsurancePlanID == request.InsurancePlanID &&
            r.ProcedureCodeID == request.ProcedureCodeID &&
            r.Status is PaStatus.Draft or PaStatus.Submitted or PaStatus.UnderReview or PaStatus.PendingInfo or PaStatus.Appealed);
        if (duplicate)
            throw new BusinessRuleViolationException("BR-005",
                "An active authorization request already exists for this patient, plan, and procedure.");

        // BR-010: clinical justification ≥ 50 chars
        if (string.IsNullOrWhiteSpace(request.ClinicalJustification) || request.ClinicalJustification.Length < 50)
            throw new BusinessRuleViolationException("BR-010",
                "Clinical justification must be at least 50 characters.");

        return Task.CompletedTask;
    }

    public Task ValidateForApprovalAsync(PaRequest request, ApprovalDecisionModel decision, CancellationToken ct = default)
    {
        // BR-006: end date after start date
        if (decision.AuthorizationEndDate <= decision.AuthorizationStartDate)
            throw new BusinessRuleViolationException("BR-006", "Authorization end date must be after start date.");

        // BR-007: granted ≤ requested
        if (decision.ApprovedUnitsGranted > request.ApprovedUnitsRequested)
            throw new BusinessRuleViolationException("BR-007",
                $"Units granted ({decision.ApprovedUnitsGranted}) cannot exceed units requested ({request.ApprovedUnitsRequested}).");

        // BR-011: start date ≥ today
        if (decision.AuthorizationStartDate.Date < DateTime.UtcNow.Date)
            throw new BusinessRuleViolationException("BR-011", "Authorization start date must not be in the past.");

        // BR-012: granted ≥ 1
        if (decision.ApprovedUnitsGranted < 1)
            throw new BusinessRuleViolationException("BR-012", "Approved units granted must be at least 1.");

        return Task.CompletedTask;
    }

    public Task ValidateAppealEligibilityAsync(PaRequest request, CancellationToken ct = default)
    {
        if (request.Status != PaStatus.Denied)
            throw new WorkflowTransitionException(request.Status, PaStatus.Appealed);

        var denial = _store.DenialReasons.FirstOrDefault(d => d.ID == request.DenialReasonID);
        if (denial != null && !denial.IsAppealable)
            throw new BusinessRuleViolationException("BR-008",
                $"Denial reason '{denial.Code}' is not appealable.");

        if (denial != null && request.DecisionRenderedAt.HasValue)
        {
            var deadline = request.DecisionRenderedAt.Value.AddDays(denial.AppealDeadlineDays);
            if (DateTime.UtcNow > deadline)
                throw new BusinessRuleViolationException("BR-008",
                    $"Appeal deadline of {denial.AppealDeadlineDays} days has passed.");
        }

        return Task.CompletedTask;
    }

    public void ValidateCanWithdraw(PaRequest request)
    {
        if (request.Status is PaStatus.Approved or PaStatus.Expired or PaStatus.Denied or PaStatus.Draft)
            throw new BusinessRuleViolationException("BR-009",
                $"Cannot withdraw a request in {request.Status} status.");
    }

    public Task ValidateResubmissionAsync(int requestId, CancellationToken ct = default)
    {
        var request = _store.Requests.FirstOrDefault(r => r.ID == requestId)
            ?? throw new EntityNotFoundException("PaRequest", requestId.ToString());

        // Find when the request last entered PendingInfo
        var pendingInfoEntry = _store.StatusHistory
            .Where(h => h.PaRequestID == requestId && h.ToStatus == PaStatus.PendingInfo)
            .OrderByDescending(h => h.ChangedAt)
            .FirstOrDefault();

        if (pendingInfoEntry == null)
            return Task.CompletedTask; // No PendingInfo history — allow resubmit

        var threshold = pendingInfoEntry.ChangedAt;

        bool hasNewContent =
            _store.Comments.Any(c => c.PaRequestID == requestId && c.AuthoredAt > threshold) ||
            _store.Documents.Any(d => d.PaRequestID == requestId && d.UploadedAt > threshold);

        if (!hasNewContent)
            throw new BusinessRuleViolationException("BR-013",
                "You must provide additional information (a new comment or document) before resubmitting.");

        return Task.CompletedTask;
    }

    public void ValidateCanHardDelete(PaRequest request, string callerUserId, bool isAdmin)
    {
        if (request.Status != PaStatus.Draft)
            throw new BusinessRuleViolationException("BR-014", "Only Draft requests can be deleted.");

        if (!isAdmin && request.SubmittedByUserID != callerUserId)
            throw new Exceptions.AuthorizationException("Only the creating specialist or an admin can delete a draft.");
    }
}
