using Microsoft.EntityFrameworkCore;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;
using Prior_Authorization_Workflow_Tracker.Services.Models;
using Prior_Authorization_Workflow_Tracker.Services.Validation;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Implementation of all PA business rule checks (BR-001–BR-014, §9).
/// Uses IDbContextFactory for independent DB reads so rule checks are not
/// entangled with the calling service's change-tracked context.
/// </summary>
public sealed class BusinessRuleService : IBusinessRuleService
{
    private static readonly PaStatus[] ActiveStatuses =
        [PaStatus.Submitted, PaStatus.PendingInfo, PaStatus.UnderReview, PaStatus.Appealed];

    private static readonly PaStatus[] WithdrawProhibitedStatuses =
        [PaStatus.Approved, PaStatus.Expired, PaStatus.Denied, PaStatus.Draft, PaStatus.Withdrawn];

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDateTimeProvider _clock;
    private readonly PaRequestSubmitValidator _submitValidator = new();

    public BusinessRuleService(IDbContextFactory<AppDbContext> dbFactory, IDateTimeProvider clock)
    {
        _dbFactory = dbFactory;
        _clock = clock;
    }

    // ── BR-001, BR-003, BR-004, BR-005, BR-010 ───────────────────────────────

    public async Task ValidateForSubmissionAsync(PaRequest request, CancellationToken ct = default)
    {
        // Field-level: BR-003, BR-004, BR-010 (and required fields)
        var result = await _submitValidator.ValidateAsync(request, ct);
        if (!result.IsValid)
        {
            var err = result.Errors[0];
            throw new BusinessRuleViolationException(ExtractRuleId(err.ErrorMessage), err.ErrorMessage);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // BR-001: Procedure code must require prior auth
        var proc = await db.ProcedureCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ID == request.ProcedureCodeID, ct)
            ?? throw new EntityNotFoundException(nameof(ProcedureCode), request.ProcedureCodeID.ToString());

        if (!proc.RequiresPriorAuth)
            throw new BusinessRuleViolationException("BR-001",
                $"BR-001: Procedure code {proc.Code} – {proc.Description} does not require prior authorization.");

        // BR-005: No duplicate active request for the same patient + procedure + plan
        // Request ID = 0 when not yet saved — exclude nothing from DB check.
        var duplicate = await db.PaRequests
            .AsNoTracking()
            .AnyAsync(r =>
                r.ID != request.ID &&
                r.PatientMrn == request.PatientMrn &&
                r.ProcedureCodeID == request.ProcedureCodeID &&
                r.InsurancePlanID == request.InsurancePlanID &&
                ActiveStatuses.Contains(r.Status),
                ct);

        if (duplicate)
            throw new BusinessRuleViolationException("BR-005",
                "BR-005: An active prior authorization request already exists for this patient, procedure code, and insurance plan.");
    }

    // ── BR-006, BR-007, BR-011, BR-012 ───────────────────────────────────────

    public async Task ValidateForApprovalAsync(
        PaRequest request, ApprovalDecisionModel decision, CancellationToken ct = default)
    {
        var validator = new ApprovalDecisionValidator(_clock);
        var result = await validator.ValidateAsync(decision, ct);
        if (!result.IsValid)
        {
            var err = result.Errors[0];
            throw new BusinessRuleViolationException(ExtractRuleId(err.ErrorMessage), err.ErrorMessage);
        }

        // BR-007: Granted must not exceed requested
        if (decision.ApprovedUnitsGranted > request.ApprovedUnitsRequested)
            throw new BusinessRuleViolationException("BR-007",
                $"BR-007: Approved units granted ({decision.ApprovedUnitsGranted}) cannot exceed " +
                $"units requested ({request.ApprovedUnitsRequested}).");
    }

    // ── BR-008 ────────────────────────────────────────────────────────────────

    public async Task ValidateAppealEligibilityAsync(PaRequest request, CancellationToken ct = default)
    {
        if (request.Status != PaStatus.Denied)
            throw new WorkflowTransitionException(request.Status, PaStatus.Appealed);

        if (!request.DecisionRenderedAt.HasValue)
            throw new BusinessRuleViolationException("BR-008",
                "BR-008: Cannot determine appeal eligibility — no decision date recorded.");

        if (request.DenialReasonID is null)
            throw new BusinessRuleViolationException("BR-008",
                "BR-008: Cannot determine appeal eligibility — no denial reason recorded.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var denial = await db.DenialReasons
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ID == request.DenialReasonID, ct)
            ?? throw new EntityNotFoundException(nameof(DenialReason), request.DenialReasonID.Value.ToString());

        if (!denial.IsAppealable)
            throw new BusinessRuleViolationException("BR-008",
                $"BR-008: Denial reason '{denial.Code}' is not appealable.");

        // BUG-006: appeal window uses <= (inclusive of the deadline day)
        var deadline = request.DecisionRenderedAt.Value.AddDays(denial.AppealDeadlineDays);
        if (_clock.UtcNow.Date > deadline.Date)
            throw new BusinessRuleViolationException("BR-008",
                $"BR-008: Appeal window closed. The deadline was {deadline:yyyy-MM-dd} " +
                $"({denial.AppealDeadlineDays} days from decision on {request.DecisionRenderedAt.Value:yyyy-MM-dd}).");
    }

    // ── BR-009 ────────────────────────────────────────────────────────────────

    public void ValidateCanWithdraw(PaRequest request)
    {
        if (WithdrawProhibitedStatuses.Contains(request.Status))
            throw new BusinessRuleViolationException("BR-009",
                $"BR-009: A request in '{request.Status}' status cannot be withdrawn. " +
                "Draft requests must be deleted; Denied requests must be appealed.");
    }

    // ── BR-013 ────────────────────────────────────────────────────────────────

    public async Task ValidateResubmissionAsync(int requestId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // BUG-014: read from PaStatusHistory, not PaRequest.UpdatedAt
        var lastPendingEntry = await db.PaStatusHistories
            .AsNoTracking()
            .Where(h => h.PaRequestID == requestId && h.ToStatus == PaStatus.PendingInfo)
            .OrderByDescending(h => h.ChangedAt)
            .FirstOrDefaultAsync(ct);

        if (lastPendingEntry is null)
            throw new BusinessRuleViolationException("BR-013",
                "BR-013: Cannot find a PendingInfo entry in the status history for this request.");

        var pendingAt = lastPendingEntry.ChangedAt;

        var hasNewComment = await db.PaComments
            .AsNoTracking()
            .AnyAsync(c => c.PaRequestID == requestId && c.AuthoredAt > pendingAt, ct);

        var hasNewDocument = await db.PaDocuments
            .AsNoTracking()
            .AnyAsync(d => d.PaRequestID == requestId && d.UploadedAt > pendingAt, ct);

        if (!hasNewComment && !hasNewDocument)
            throw new BusinessRuleViolationException("BR-013",
                "BR-013: At least one new comment or document must be added after the request " +
                "entered 'Additional Information Required' status before re-submitting.");
    }

    // ── BR-014 ────────────────────────────────────────────────────────────────

    public void ValidateCanHardDelete(PaRequest request, string callerUserId, bool isAdmin)
    {
        if (request.Status != PaStatus.Draft)
            throw new BusinessRuleViolationException("BR-014",
                "BR-014: Only Draft requests may be permanently deleted. " +
                "All other statuses use soft delete.");

        if (!isAdmin && request.SubmittedByUserID != callerUserId)
            throw new BusinessRuleViolationException("BR-014",
                "BR-014: Only the Specialist who created the request, or an Administrator, " +
                "may permanently delete a draft request.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts "BR-NNN" from a validation message that starts with "BR-NNN: ...".
    /// Falls back to "VALIDATION" when the message does not carry a rule prefix.
    /// </summary>
    private static string ExtractRuleId(string message)
    {
        if (message.StartsWith("BR-", StringComparison.Ordinal))
        {
            var colon = message.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0) return message[..colon];
        }
        return "VALIDATION";
    }
}
