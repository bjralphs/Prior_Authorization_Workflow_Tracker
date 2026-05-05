using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prior_Authorization_Workflow_Tracker.Constants;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// State machine implementation for PA request lifecycle transitions (§8.4, FR-004/005/007).
/// </summary>
public sealed class WorkflowService : IWorkflowService
{
    // ── Transition table ──────────────────────────────────────────────────────
    // Key:   (fromStatus, toStatus)
    // Value: roles permitted to trigger this transition (empty = SYSTEM only)
    private static readonly IReadOnlyDictionary<(PaStatus, PaStatus), string[]> Transitions =
        new Dictionary<(PaStatus, PaStatus), string[]>
        {
            [(PaStatus.Draft,        PaStatus.Submitted)]   = [Roles.Specialist, Roles.Admin],
            [(PaStatus.Submitted,    PaStatus.UnderReview)] = [Roles.Reviewer,   Roles.Admin],
            [(PaStatus.Submitted,    PaStatus.PendingInfo)] = [Roles.Reviewer,   Roles.Admin],
            [(PaStatus.PendingInfo,  PaStatus.Submitted)]   = [Roles.Specialist, Roles.Admin],
            [(PaStatus.UnderReview,  PaStatus.Approved)]    = [Roles.Reviewer,   Roles.Admin],
            [(PaStatus.UnderReview,  PaStatus.Denied)]      = [Roles.Reviewer,   Roles.Admin],
            [(PaStatus.UnderReview,  PaStatus.PendingInfo)] = [Roles.Reviewer,   Roles.Admin],
            [(PaStatus.Approved,     PaStatus.Expired)]     = [],  // SYSTEM only
            [(PaStatus.Denied,       PaStatus.Appealed)]    = [Roles.Specialist, Roles.Admin],
            [(PaStatus.Appealed,     PaStatus.UnderReview)] = [Roles.Reviewer,   Roles.Admin],
            [(PaStatus.Appealed,     PaStatus.Denied)]      = [Roles.Reviewer,   Roles.Admin],
            [(PaStatus.Appealed,     PaStatus.Approved)]    = [Roles.Reviewer,   Roles.Admin],
            [(PaStatus.Submitted,    PaStatus.Withdrawn)]   = [Roles.Specialist, Roles.Provider, Roles.Admin],
            [(PaStatus.PendingInfo,  PaStatus.Withdrawn)]   = [Roles.Specialist, Roles.Provider, Roles.Admin],
            [(PaStatus.UnderReview,  PaStatus.Withdrawn)]   = [Roles.Specialist, Roles.Provider, Roles.Admin],
            [(PaStatus.Appealed,     PaStatus.Withdrawn)]   = [Roles.Specialist, Roles.Provider, Roles.Admin],
        };

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditService _audit;
    private readonly IBusinessRuleService _rules;
    private readonly INotificationService _notifications;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        IDbContextFactory<AppDbContext> dbFactory,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IAuditService audit,
        IBusinessRuleService rules,
        INotificationService notifications,
        ILogger<WorkflowService> logger)
    {
        _dbFactory     = dbFactory;
        _currentUser   = currentUser;
        _clock         = clock;
        _audit         = audit;
        _rules         = rules;
        _notifications = notifications;
        _logger        = logger;
    }

    public bool IsTransitionValid(PaStatus from, PaStatus to) =>
        Transitions.ContainsKey((from, to));

    // ── Draft → Submitted ─────────────────────────────────────────────────────

    public async Task<PaRequest> SubmitAsync(int requestId, CancellationToken ct = default)
    {
        RequireActive();
        RequireRole(PaStatus.Draft, PaStatus.Submitted);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await LoadAsync(db, requestId, ct);
        EnsureStatus(request, PaStatus.Draft, PaStatus.Submitted);

        await _rules.ValidateForSubmissionAsync(request, ct);

        // BR-002: Calculate DecisionDueDate from plan SLA + priority
        var plan = await db.InsurancePlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ID == request.InsurancePlanID, ct)
            ?? throw new EntityNotFoundException(nameof(InsurancePlan), request.InsurancePlanID.ToString());

        var slaDays = request.Priority switch
        {
            PaPriority.Urgent   => plan.UrgentDecisionDays,
            PaPriority.Emergent => plan.EmergentDecisionDays,
            _                   => plan.RoutineDecisionDays,
        };

        var now = _clock.UtcNow;
        request.SubmittedAt    = now;
        request.DecisionDueDate = now.AddDays(slaDays);

        await ApplyTransitionAsync(db, request, PaStatus.Draft, PaStatus.Submitted, reason: null, ct);

        await _audit.LogAsync("PaRequest", request.ID.ToString(), "StatusChanged",
            oldValues: Snapshot("Status", PaStatus.Draft),
            newValues: Snapshot("Status", PaStatus.Submitted, "SubmittedAt", now.ToString("O")),
            cancellationToken: ct);

        return request;
    }

    // ── Submitted | Appealed → UnderReview ──────────────────────────────────

    public async Task<PaRequest> BeginReviewAsync(int requestId, CancellationToken ct = default)
    {
        RequireActive();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await LoadAsync(db, requestId, ct);

        // FR-004: accepts Submitted → UnderReview (new request) and
        // Appealed → UnderReview (appeal reopened for secondary review, §8.7)
        if (request.Status != PaStatus.Submitted && request.Status != PaStatus.Appealed)
            throw new WorkflowTransitionException(request.Status, PaStatus.UnderReview);

        RequireRole(request.Status, PaStatus.UnderReview);

        var fromStatus = request.Status;
        request.ReviewerUserID = _currentUser.UserId;

        await ApplyTransitionAsync(db, request, fromStatus, PaStatus.UnderReview, reason: null, ct);

        await _audit.LogAsync("PaRequest", request.ID.ToString(), "StatusChanged",
            oldValues: Snapshot("Status", fromStatus),
            newValues: Snapshot("Status", PaStatus.UnderReview, "ReviewerUserID", _currentUser.UserId),
            cancellationToken: ct);

        return request;
    }

    // ── UnderReview | Appealed → Approved ────────────────────────────────────

    public async Task<PaRequest> ApproveAsync(
        int requestId, ApprovalDecisionModel decision, CancellationToken ct = default)
    {
        RequireActive();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await LoadAsync(db, requestId, ct);

        // FR-004: accepts UnderReview → Approved (standard decision) and
        // Appealed → Approved (appeal overturned by reviewer, §8.7)
        if (request.Status != PaStatus.UnderReview && request.Status != PaStatus.Appealed)
            throw new WorkflowTransitionException(request.Status, PaStatus.Approved);

        RequireRole(request.Status, PaStatus.Approved);

        await _rules.ValidateForApprovalAsync(request, decision, ct);

        var fromStatus = request.Status;
        var now = _clock.UtcNow;
        request.ApprovedUnitsGranted   = decision.ApprovedUnitsGranted;
        request.AuthorizationStartDate = decision.AuthorizationStartDate;
        request.AuthorizationEndDate   = decision.AuthorizationEndDate;
        request.AuthorizationNumber    = GenerateAuthorizationNumber(now);
        request.DecisionRenderedAt     = now;
        request.ReviewerUserID         = _currentUser.UserId;

        await ApplyTransitionAsync(db, request, fromStatus, PaStatus.Approved, reason: null, ct);

        await _audit.LogAsync("PaRequest", request.ID.ToString(), "DecisionRendered",
            oldValues: Snapshot("Status", fromStatus),
            newValues: Snapshot("Status", PaStatus.Approved, "AuthorizationNumber", request.AuthorizationNumber!),
            cancellationToken: ct);

        await _notifications.CreateAsync(
            request.SubmittedByUserID,
            requestId,
            $"Authorization {request.RequestNumber} approved. Auth#: {request.AuthorizationNumber}.",
            NotificationType.DecisionRendered,
            ct);

        return request;
    }

    // ── UnderReview | Appealed → Denied ──────────────────────────────────────

    public async Task<PaRequest> DenyAsync(
        int requestId, int denialReasonId, string? notes, CancellationToken ct = default)
    {
        RequireActive();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await LoadAsync(db, requestId, ct);

        // FR-004: accepts UnderReview → Denied (standard decision) and
        // Appealed → Denied (appeal upheld — denial stands, §8.7)
        if (request.Status != PaStatus.UnderReview && request.Status != PaStatus.Appealed)
            throw new WorkflowTransitionException(request.Status, PaStatus.Denied);

        RequireRole(request.Status, PaStatus.Denied);

        var denial = await db.DenialReasons
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ID == denialReasonId, ct)
            ?? throw new EntityNotFoundException(nameof(DenialReason), denialReasonId.ToString());

        var fromStatus = request.Status;
        var now = _clock.UtcNow;
        request.DenialReasonID     = denialReasonId;
        request.DenialNotes        = notes;
        request.DecisionRenderedAt = now;
        request.ReviewerUserID     = _currentUser.UserId;

        await ApplyTransitionAsync(db, request, fromStatus, PaStatus.Denied, reason: null, ct);

        await _audit.LogAsync("PaRequest", request.ID.ToString(), "DecisionRendered",
            oldValues: Snapshot("Status", fromStatus),
            newValues: Snapshot("Status", PaStatus.Denied, "DenialReasonID", denialReasonId.ToString()),
            cancellationToken: ct);

        var appealNote = denial.IsAppealable
            ? $" You have {denial.AppealDeadlineDays} days to file an appeal."
            : " This denial is not appealable.";

        await _notifications.CreateAsync(
            request.SubmittedByUserID, requestId,
            $"Authorization {request.RequestNumber} denied: {denial.Description}.{appealNote}",
            NotificationType.DecisionRendered, ct);

        if (denial.IsAppealable)
        {
            var deadline = now.AddDays(denial.AppealDeadlineDays);
            await _notifications.CreateAsync(
                request.SubmittedByUserID, requestId,
                $"Appeal window for {request.RequestNumber} closes {deadline:yyyy-MM-dd}.",
                NotificationType.AppealWindow, ct);
        }

        return request;
    }

    // ── Submitted | UnderReview → PendingInfo ─────────────────────────────────

    public async Task<PaRequest> RequestAdditionalInfoAsync(
        int requestId, string reason, CancellationToken ct = default)
    {
        RequireActive();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await LoadAsync(db, requestId, ct);

        if (request.Status != PaStatus.Submitted && request.Status != PaStatus.UnderReview)
            throw new WorkflowTransitionException(request.Status, PaStatus.PendingInfo);

        RequireRole(request.Status, PaStatus.PendingInfo);

        if (request.Status == PaStatus.UnderReview)
            request.ReviewerUserID ??= _currentUser.UserId;

        var fromStatus = request.Status;
        await ApplyTransitionAsync(db, request, fromStatus, PaStatus.PendingInfo, reason, ct);

        await _audit.LogAsync("PaRequest", request.ID.ToString(), "StatusChanged",
            oldValues: Snapshot("Status", fromStatus),
            newValues: Snapshot("Status", PaStatus.PendingInfo),
            cancellationToken: ct);

        await _notifications.CreateAsync(
            request.SubmittedByUserID, requestId,
            $"Additional information is required for {request.RequestNumber}. Reason: {reason}",
            NotificationType.StatusChanged, ct);

        return request;
    }

    // ── PendingInfo → Submitted ───────────────────────────────────────────────

    public async Task<PaRequest> ResubmitAsync(int requestId, CancellationToken ct = default)
    {
        RequireActive();
        RequireRole(PaStatus.PendingInfo, PaStatus.Submitted);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await LoadAsync(db, requestId, ct);
        EnsureStatus(request, PaStatus.PendingInfo, PaStatus.Submitted);

        await _rules.ValidateResubmissionAsync(requestId, ct);

        // Recalculate DecisionDueDate from the new submission time
        var plan = await db.InsurancePlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ID == request.InsurancePlanID, ct)
            ?? throw new EntityNotFoundException(nameof(InsurancePlan), request.InsurancePlanID.ToString());

        var slaDays = request.Priority switch
        {
            PaPriority.Urgent   => plan.UrgentDecisionDays,
            PaPriority.Emergent => plan.EmergentDecisionDays,
            _                   => plan.RoutineDecisionDays,
        };

        var now = _clock.UtcNow;
        request.SubmittedAt     = now;
        request.DecisionDueDate = now.AddDays(slaDays);

        await ApplyTransitionAsync(db, request, PaStatus.PendingInfo, PaStatus.Submitted, reason: null, ct);

        await _audit.LogAsync("PaRequest", request.ID.ToString(), "StatusChanged",
            oldValues: Snapshot("Status", PaStatus.PendingInfo),
            newValues: Snapshot("Status", PaStatus.Submitted),
            cancellationToken: ct);

        return request;
    }

    // ── Denied → Appealed ─────────────────────────────────────────────────────

    public async Task<PaRequest> AppealAsync(int requestId, CancellationToken ct = default)
    {
        RequireActive();
        RequireRole(PaStatus.Denied, PaStatus.Appealed);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await LoadAsync(db, requestId, ct);
        EnsureStatus(request, PaStatus.Denied, PaStatus.Appealed);

        await _rules.ValidateAppealEligibilityAsync(request, ct);

        await ApplyTransitionAsync(db, request, PaStatus.Denied, PaStatus.Appealed, reason: null, ct);

        await _audit.LogAsync("PaRequest", request.ID.ToString(), "AppealFiled",
            oldValues: Snapshot("Status", PaStatus.Denied),
            newValues: Snapshot("Status", PaStatus.Appealed),
            cancellationToken: ct);

        return request;
    }

    // ── * → Withdrawn ─────────────────────────────────────────────────────────

    public async Task<PaRequest> WithdrawAsync(
        int requestId, string reason, CancellationToken ct = default)
    {
        RequireActive();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await LoadAsync(db, requestId, ct);

        _rules.ValidateCanWithdraw(request);  // BR-009 (also blocks Draft, BUG-013 mitigation)
        RequireRole(request.Status, PaStatus.Withdrawn);

        var fromStatus = request.Status;
        await ApplyTransitionAsync(db, request, fromStatus, PaStatus.Withdrawn, reason, ct);

        await _audit.LogAsync("PaRequest", request.ID.ToString(), "Withdrawn",
            oldValues: Snapshot("Status", fromStatus),
            newValues: Snapshot("Status", PaStatus.Withdrawn),
            cancellationToken: ct);

        return request;
    }

    // ── Approved → Expired (SYSTEM) ───────────────────────────────────────────

    public async Task<PaRequest> ExpireAsync(int requestId, CancellationToken ct = default)
    {
        // Not role-checked: only called by ExpirationJob using the SYSTEM user.
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await LoadAsync(db, requestId, ct);

        // Guard against double-expiration (BUG-008 mitigation)
        if (request.Status != PaStatus.Approved)
            throw new WorkflowTransitionException(request.Status, PaStatus.Expired);

        var now = _clock.UtcNow;
        request.Status    = PaStatus.Expired;
        request.UpdatedAt = now;

        db.PaStatusHistories.Add(new PaStatusHistory
        {
            PaRequestID     = requestId,
            FromStatus      = PaStatus.Approved,
            ToStatus        = PaStatus.Expired,
            ChangedByUserID = DbSeeder.SystemUserId,
            ChangeReason    = "Authorization period expired.",
            ChangedAt       = now,
        });

        await db.SaveChangesAsync(ct); // may throw DbUpdateConcurrencyException

        // Delegate to IAuditService.LogAsSystemAsync so the audit write is consistent
        // with all other audit entries and is covered by the same error-swallowing contract.
        // Background-job context has no authenticated user, so LogAsSystemAsync uses the
        // hardcoded SYSTEM identity (DbSeeder.SystemUserId / "SYSTEM").
        await _audit.LogAsSystemAsync("PaRequest", request.ID.ToString(), "Expired",
            oldValues: Snapshot("Status", PaStatus.Approved),
            newValues: Snapshot("Status", PaStatus.Expired),
            cancellationToken: ct);

        await _notifications.CreateAsync(
            request.SubmittedByUserID, requestId,
            $"Authorization {request.AuthorizationNumber} for {request.RequestNumber} has expired.",
            NotificationType.ExpirationAlert, ct);

        if (!string.IsNullOrEmpty(request.ProviderID))
        {
            await _notifications.CreateAsync(
                request.ProviderID, requestId,
                $"Authorization {request.AuthorizationNumber} for {request.RequestNumber} has expired.",
                NotificationType.ExpirationAlert, ct);
        }

        return request;
    }

    // ── Admin override (T-C5 / §8.8 / §11 AuditLogs) ─────────────────────────

    public async Task<PaRequest> AdminOverrideAsync(
        int requestId,
        PaStatus newStatus,
        string justification,
        CancellationToken ct = default)
    {
        RequireActive();

        if (!_currentUser.IsInRole(Roles.Admin))
            throw new AuthorizationException(
                "Only Administrators may use the workflow override feature.");

        if (string.IsNullOrWhiteSpace(justification))
            throw new BusinessRuleViolationException("OVERRIDE",
                "A justification is required for an administrative workflow override.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await LoadAsync(db, requestId, ct);

        var fromStatus = request.Status;
        var now = _clock.UtcNow;

        request.Status    = newStatus;
        request.UpdatedAt = now;

        db.PaStatusHistories.Add(new PaStatusHistory
        {
            PaRequestID     = requestId,
            FromStatus      = fromStatus,
            ToStatus        = newStatus,
            ChangedByUserID = _currentUser.UserId,
            ChangeReason    = $"[Admin Override] {justification}",
            ChangedAt       = now,
        });

        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("PaRequest", request.ID.ToString(), "AdminOverride",
            oldValues: Snapshot("Status", fromStatus),
            newValues: Snapshot("Status", newStatus, "Justification", justification),
            cancellationToken: ct);

        _logger.LogWarning(
            "Admin {User} overrode request {RequestId} status from {From} to {To}. Justification: {Justification}",
            _currentUser.UserName, requestId, fromStatus, newStatus, justification);

        return request;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<PaRequest> LoadAsync(
        AppDbContext db, int requestId, CancellationToken ct)
    {
        return await db.PaRequests
            .FirstOrDefaultAsync(r => r.ID == requestId, ct)
            ?? throw new EntityNotFoundException(nameof(PaRequest), requestId.ToString());
    }

    private static void EnsureStatus(PaRequest request, PaStatus expected, PaStatus target)
    {
        if (request.Status != expected)
            throw new WorkflowTransitionException(request.Status, target);
    }

    private void RequireActive()
    {
        if (!_currentUser.IsActive)
            throw new AuthorizationException(
                $"User account '{_currentUser.UserName}' is inactive and may not perform operations.");
    }

    private void RequireRole(PaStatus from, PaStatus to)
    {
        if (!Transitions.TryGetValue((from, to), out var allowed))
            throw new WorkflowTransitionException(from, to);

        if (allowed.Length > 0 && !allowed.Any(_currentUser.IsInRole))
            throw new AuthorizationException(
                $"User '{_currentUser.UserName}' does not have permission for transition " +
                $"{from} → {to}. Required role(s): {string.Join(", ", allowed)}.");
    }

    private async Task ApplyTransitionAsync(
        AppDbContext db, PaRequest request,
        PaStatus from, PaStatus to,
        string? reason, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        request.Status    = to;
        request.UpdatedAt = now;

        db.PaStatusHistories.Add(new PaStatusHistory
        {
            PaRequestID     = request.ID,
            FromStatus      = from,
            ToStatus        = to,
            ChangedByUserID = _currentUser.UserId,
            ChangeReason    = reason,
            ChangedAt       = now,
        });

        await db.SaveChangesAsync(ct); // throws DbUpdateConcurrencyException on RowVersion conflict
    }

    /// <summary>Builds a minimal JSON snapshot string for AuditLog.OldValues/NewValues.</summary>
    private static string Snapshot(params object[] pairs)
    {
        var parts = new List<string>();
        for (var i = 0; i + 1 < pairs.Length; i += 2)
            parts.Add($"\"{pairs[i]}\":\"{pairs[i + 1]}\"");
        return "{" + string.Join(",", parts) + "}";
    }

    private static string GenerateAuthorizationNumber(DateTime utcNow)
    {
        // Use cryptographically strong random bytes to minimise collision probability
        // (3 bytes → 6 hex chars → ~16.7 million distinct values per calendar day).
        Span<byte> buf = stackalloc byte[3];
        RandomNumberGenerator.Fill(buf);
        var suffix = Convert.ToHexString(buf); // uppercase hex, e.g. "A3F02C"
        return $"AUTH-{utcNow:yyyyMMdd}-{suffix}";
    }
}
