using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>
/// In-memory workflow state machine.  Mirrors the logic of the production
/// WorkflowService but operates entirely on the DemoDataStore list.
/// </summary>
public sealed class DemoWorkflowService : IWorkflowService
{
    private readonly DemoDataStore          _store;
    private readonly DemoCurrentUserService _currentUser;
    private readonly IBusinessRuleService   _rules;
    private readonly IAuditService          _audit;
    private readonly INotificationService   _notif;
    private readonly IDateTimeProvider      _clock;

    public DemoWorkflowService(
        DemoDataStore store,
        DemoCurrentUserService currentUser,
        IBusinessRuleService rules,
        IAuditService audit,
        INotificationService notif,
        IDateTimeProvider clock)
    {
        _store       = store;
        _currentUser = currentUser;
        _rules       = rules;
        _audit       = audit;
        _notif       = notif;
        _clock       = clock;
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private PaRequest Require(int id)
        => _store.Requests.FirstOrDefault(r => r.ID == id)
           ?? throw new EntityNotFoundException("PaRequest", id.ToString());

    private void RequireRole(params string[] roles)
    {
        if (!roles.Any(r => _currentUser.IsInRole(r)))
            throw new Exceptions.AuthorizationException(
                $"Role '{_currentUser.IsInRole(Constants.Roles.Admin)}' not permitted for this action.");
    }

    private void AddHistory(PaRequest r, PaStatus from, PaStatus to, string notes)
    {
        _store.StatusHistory.Add(new PaStatusHistory
        {
            ID              = _store.NextHistoryId(),
            PaRequestID     = r.ID,
            FromStatus      = from,
            ToStatus        = to,
            ChangedByUserID = _currentUser.UserId ?? "SYSTEM",
            ChangedAt       = _clock.UtcNow,
            ChangeReason    = notes,
        });
    }

    // ── SubmitAsync ───────────────────────────────────────────────────────────
    public async Task<PaRequest> SubmitAsync(int requestId, CancellationToken ct = default)
    {
        RequireRole(Constants.Roles.Specialist, Constants.Roles.Admin);
        var r = Require(requestId);

        if (r.Status != PaStatus.Draft)
            throw new WorkflowTransitionException(r.Status, PaStatus.Submitted);

        await _rules.ValidateForSubmissionAsync(r, ct);

        var plan = _store.InsurancePlans.First(p => p.ID == r.InsurancePlanID);
        int slaDays = r.Priority switch
        {
            PaPriority.Emergent => plan.EmergentDecisionDays,
            PaPriority.Urgent    => plan.UrgentDecisionDays,
            _                    => plan.RoutineDecisionDays,
        };

        var prev = r.Status;
        r.Status          = PaStatus.Submitted;
        r.SubmittedAt     = _clock.UtcNow;
        r.DecisionDueDate = _clock.UtcNow.AddDays(slaDays);
        r.UpdatedAt       = _clock.UtcNow;

        AddHistory(r, prev, PaStatus.Submitted, "Request submitted for review");
        await _audit.LogAsync("PaRequest", r.ID.ToString(), "Submitted", null, null, ct);
        return r;
    }

    // ── BeginReviewAsync ──────────────────────────────────────────────────────
    public async Task<PaRequest> BeginReviewAsync(int requestId, CancellationToken ct = default)
    {
        RequireRole(Constants.Roles.Reviewer, Constants.Roles.Admin);
        var r = Require(requestId);

        if (r.Status != PaStatus.Submitted)
            throw new WorkflowTransitionException(r.Status, PaStatus.UnderReview);

        var prev = r.Status;
        r.Status          = PaStatus.UnderReview;
        r.ReviewerUserID  = _currentUser.UserId;
        r.UpdatedAt       = _clock.UtcNow;

        AddHistory(r, prev, PaStatus.UnderReview, $"Review started by {_currentUser.UserName}");
        await _audit.LogAsync("PaRequest", r.ID.ToString(), "BeginReview", null, null, ct);
        return r;
    }

    // ── ApproveAsync ──────────────────────────────────────────────────────────
    public async Task<PaRequest> ApproveAsync(int requestId, ApprovalDecisionModel decision, CancellationToken ct = default)
    {
        RequireRole(Constants.Roles.Reviewer, Constants.Roles.Admin);
        var r = Require(requestId);

        if (r.Status != PaStatus.UnderReview)
            throw new WorkflowTransitionException(r.Status, PaStatus.Approved);

        await _rules.ValidateForApprovalAsync(r, decision, ct);

        var prev = r.Status;
        r.Status                  = PaStatus.Approved;
        r.ApprovedUnitsGranted    = decision.ApprovedUnitsGranted;
        r.AuthorizationStartDate  = decision.AuthorizationStartDate;
        r.AuthorizationEndDate    = decision.AuthorizationEndDate;
        r.DecisionRenderedAt      = _clock.UtcNow;
        r.UpdatedAt               = _clock.UtcNow;
        r.AuthorizationNumber     = $"AUTH-{_clock.UtcNow:yyyyMMdd}-{r.ID:D5}";

        AddHistory(r, prev, PaStatus.Approved, $"Approved. Auth# {r.AuthorizationNumber}");
        await _audit.LogAsync("PaRequest", r.ID.ToString(), "Approved", null, null, ct);
        await _notif.CreateAsync(r.SubmittedByUserID, r.ID,
            $"{r.RequestNumber} for {r.PatientName} has been Approved.",
            NotificationType.StatusChanged, ct);
        return r;
    }

    // ── DenyAsync ─────────────────────────────────────────────────────────────
    public async Task<PaRequest> DenyAsync(int requestId, int denialReasonId, string? notes, CancellationToken ct = default)
    {
        RequireRole(Constants.Roles.Reviewer, Constants.Roles.Admin);
        var r = Require(requestId);

        if (r.Status != PaStatus.UnderReview)
            throw new WorkflowTransitionException(r.Status, PaStatus.Denied);

        var denial = _store.DenialReasons.FirstOrDefault(d => d.ID == denialReasonId)
            ?? throw new EntityNotFoundException("DenialReason", denialReasonId.ToString());

        var prev = r.Status;
        r.Status             = PaStatus.Denied;
        r.DenialReasonID     = denial.ID;
        r.DenialReason       = denial;
        r.DenialNotes = notes;
        r.DecisionRenderedAt = _clock.UtcNow;
        r.UpdatedAt          = _clock.UtcNow;

        AddHistory(r, prev, PaStatus.Denied, notes ?? denial.Description);
        await _audit.LogAsync("PaRequest", r.ID.ToString(), "Denied", null, null, ct);
        await _notif.CreateAsync(r.SubmittedByUserID, r.ID,
            $"{r.RequestNumber} for {r.PatientName} has been Denied.",
            NotificationType.StatusChanged, ct);
        return r;
    }

    // ── RequestAdditionalInfoAsync ────────────────────────────────────────────
    public async Task<PaRequest> RequestAdditionalInfoAsync(int requestId, string reason, CancellationToken ct = default)
    {
        RequireRole(Constants.Roles.Reviewer, Constants.Roles.Admin);
        var r = Require(requestId);

        if (r.Status is not (PaStatus.Submitted or PaStatus.UnderReview))
            throw new WorkflowTransitionException(r.Status, PaStatus.PendingInfo);

        var prev = r.Status;
        r.Status    = PaStatus.PendingInfo;
        r.UpdatedAt = _clock.UtcNow;
        if (string.IsNullOrEmpty(r.ReviewerUserID)) r.ReviewerUserID = _currentUser.UserId;

        AddHistory(r, prev, PaStatus.PendingInfo, reason);
        await _audit.LogAsync("PaRequest", r.ID.ToString(), "PendingInfo", null, null, ct);
        await _notif.CreateAsync(r.SubmittedByUserID, r.ID,
            $"{r.RequestNumber} for {r.PatientName} requires additional information.",
            NotificationType.StatusChanged, ct);
        return r;
    }

    // ── ResubmitAsync ─────────────────────────────────────────────────────────
    public async Task<PaRequest> ResubmitAsync(int requestId, CancellationToken ct = default)
    {
        RequireRole(Constants.Roles.Specialist, Constants.Roles.Admin);
        var r = Require(requestId);

        if (r.Status != PaStatus.PendingInfo)
            throw new WorkflowTransitionException(r.Status, PaStatus.Submitted);

        await _rules.ValidateResubmissionAsync(requestId, ct);

        var plan = _store.InsurancePlans.First(p => p.ID == r.InsurancePlanID);
        int slaDays = r.Priority switch
        {
            PaPriority.Emergent => plan.EmergentDecisionDays,
            PaPriority.Urgent    => plan.UrgentDecisionDays,
            _                    => plan.RoutineDecisionDays,
        };

        var prev = r.Status;
        r.Status          = PaStatus.Submitted;
        r.SubmittedAt     = _clock.UtcNow;
        r.DecisionDueDate = _clock.UtcNow.AddDays(slaDays);
        r.UpdatedAt       = _clock.UtcNow;

        AddHistory(r, prev, PaStatus.Submitted, "Resubmitted with additional information");
        await _audit.LogAsync("PaRequest", r.ID.ToString(), "Resubmitted", null, null, ct);
        return r;
    }

    // ── AppealAsync ───────────────────────────────────────────────────────────
    public async Task<PaRequest> AppealAsync(int requestId, CancellationToken ct = default)
    {
        RequireRole(Constants.Roles.Specialist, Constants.Roles.Admin);
        var r = Require(requestId);

        await _rules.ValidateAppealEligibilityAsync(r, ct);

        var prev = r.Status;
        r.Status    = PaStatus.Appealed;
        r.UpdatedAt = _clock.UtcNow;

        AddHistory(r, prev, PaStatus.Appealed, "Appeal filed");
        await _audit.LogAsync("PaRequest", r.ID.ToString(), "Appealed", null, null, ct);
        return r;
    }

    // ── WithdrawAsync ─────────────────────────────────────────────────────────
    public async Task<PaRequest> WithdrawAsync(int requestId, string reason, CancellationToken ct = default)
    {
        RequireRole(Constants.Roles.Specialist, Constants.Roles.Provider, Constants.Roles.Admin);
        var r = Require(requestId);

        _rules.ValidateCanWithdraw(r);

        var prev = r.Status;
        r.Status    = PaStatus.Withdrawn;
        r.UpdatedAt = _clock.UtcNow;

        AddHistory(r, prev, PaStatus.Withdrawn, reason);
        await _audit.LogAsync("PaRequest", r.ID.ToString(), "Withdrawn", null, null, ct);
        return r;
    }

    // ── ExpireAsync ───────────────────────────────────────────────────────────
    public async Task<PaRequest> ExpireAsync(int requestId, CancellationToken ct = default)
    {
        var r = Require(requestId);

        if (r.Status != PaStatus.Approved)
            throw new WorkflowTransitionException(r.Status, PaStatus.Expired);

        var prev = r.Status;
        r.Status    = PaStatus.Expired;
        r.UpdatedAt = _clock.UtcNow;

        AddHistory(r, prev, PaStatus.Expired, "Authorization expired");
        await _audit.LogAsSystemAsync("PaRequest", r.ID.ToString(), "Expired", null, null, ct);
        await _notif.CreateAsync(r.SubmittedByUserID, r.ID,
            $"{r.RequestNumber} authorization has expired.",
            NotificationType.ExpirationAlert, ct);
        return r;
    }

    // ── AdminOverrideAsync ────────────────────────────────────────────────────
    public async Task<PaRequest> AdminOverrideAsync(int requestId, PaStatus newStatus, string justification, CancellationToken ct = default)
    {
        RequireRole(Constants.Roles.Admin);
        if (string.IsNullOrWhiteSpace(justification))
            throw new BusinessRuleViolationException("BR-Admin", "Justification is required for admin override.");

        var r = Require(requestId);
        var prev = r.Status;
        r.Status    = newStatus;
        r.UpdatedAt = _clock.UtcNow;

        AddHistory(r, prev, newStatus, $"[Admin Override] {justification}");
        await _audit.LogAsync("PaRequest", r.ID.ToString(), "AdminOverride",
            prev.ToString(), newStatus.ToString(), ct);
        return r;
    }

    // ── IsTransitionValid ─────────────────────────────────────────────────────
    public bool IsTransitionValid(PaStatus from, PaStatus to)
    {
        return (from, to) switch
        {
            (PaStatus.Draft,       PaStatus.Submitted)    => true,
            (PaStatus.Submitted,   PaStatus.UnderReview)  => true,
            (PaStatus.UnderReview, PaStatus.Approved)     => true,
            (PaStatus.UnderReview, PaStatus.Denied)       => true,
            (PaStatus.UnderReview, PaStatus.PendingInfo)  => true,
            (PaStatus.PendingInfo, PaStatus.Submitted)    => true,
            (PaStatus.Denied,      PaStatus.Appealed)     => true,
            (PaStatus.Appealed,    PaStatus.UnderReview)  => true,
            (PaStatus.Approved,    PaStatus.Expired)      => true,
            (_, PaStatus.Withdrawn)                        => true,
            _ => false
        };
    }
}
