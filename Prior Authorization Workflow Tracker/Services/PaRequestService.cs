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
/// CRUD operations for PA requests (FR-001, FR-002, FR-003, §8.1–§8.3).
/// </summary>
public sealed class PaRequestService : IPaRequestService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditService _audit;
    private readonly IBusinessRuleService _rules;
    private readonly ILogger<PaRequestService> _logger;

    public PaRequestService(
        IDbContextFactory<AppDbContext> dbFactory,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IAuditService audit,
        IBusinessRuleService rules,
        ILogger<PaRequestService> logger)
    {
        _dbFactory   = dbFactory;
        _currentUser = currentUser;
        _clock       = clock;
        _audit       = audit;
        _rules       = rules;
        _logger      = logger;
    }

    // ── Create ────────────────────────────────────────────────────────────────

    public async Task<PaRequest> CreateDraftAsync(CreatePaRequestModel model, CancellationToken ct = default)
    {
        RequireActive();
        RequireAnyRole(Roles.Specialist, Roles.Admin);

        if (string.IsNullOrWhiteSpace(model.PatientMrn))
            throw new BusinessRuleViolationException("DRAFT", "Patient MRN is required for Draft save.");
        if (model.InsurancePlanID == 0)
            throw new BusinessRuleViolationException("DRAFT", "Insurance plan is required for Draft save.");
        if (model.ProcedureCodeID == 0)
            throw new BusinessRuleViolationException("DRAFT", "Procedure code is required for Draft save.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var now = _clock.UtcNow;
        var request = new PaRequest
        {
            PatientMrn            = model.PatientMrn,
            PatientName           = model.PatientName,
            PatientDob            = model.PatientDob,
            InsurancePlanID       = model.InsurancePlanID,
            ProcedureCodeID       = model.ProcedureCodeID,
            ProviderID            = model.ProviderID,
            DiagnosisCode         = model.DiagnosisCode,
            ClinicalJustification = model.ClinicalJustification,
            Priority              = model.Priority,
            ApprovedUnitsRequested = model.ApprovedUnitsRequested,
            SubmittedByUserID     = _currentUser.UserId,
            Status                = PaStatus.Draft,
            CreatedAt             = now,
            UpdatedAt             = now,
        };

        db.PaRequests.Add(request);
        await db.SaveChangesAsync(ct); // assigns request.ID from IDENTITY

        // BUG-011 mitigation: derive RequestNumber from the IDENTITY-assigned ID
        request.RequestNumber = $"PA-{now.Year}-{request.ID:D5}";
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("PaRequest", request.ID.ToString(), "Created",
            newValues: $"{{\"RequestNumber\":\"{request.RequestNumber}\",\"Status\":\"Draft\"}}",
            cancellationToken: ct);

        return request;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task UpdateDraftAsync(
        int requestId, CreatePaRequestModel model, CancellationToken ct = default)
    {
        RequireActive();
        RequireAnyRole(Roles.Specialist, Roles.Admin);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await db.PaRequests
            .FirstOrDefaultAsync(r => r.ID == requestId, ct)
            ?? throw new EntityNotFoundException(nameof(PaRequest), requestId.ToString());

        if (request.Status != PaStatus.Draft && request.Status != PaStatus.PendingInfo)
            throw new BusinessRuleViolationException("EDIT",
                $"Only Draft or PendingInfo requests may be edited. Current status: {request.Status}.");

        // Only the creating specialist (or Admin) may edit
        if (!_currentUser.IsInRole(Roles.Admin) && request.SubmittedByUserID != _currentUser.UserId)
            throw new AuthorizationException(
                "Only the Specialist who created the request or an Admin may edit it.");

        var oldStatus = request.Status;
        request.PatientMrn             = model.PatientMrn;
        request.PatientName            = model.PatientName;
        request.PatientDob             = model.PatientDob;
        request.InsurancePlanID        = model.InsurancePlanID;
        request.ProcedureCodeID        = model.ProcedureCodeID;
        request.ProviderID             = model.ProviderID;
        request.DiagnosisCode          = model.DiagnosisCode;
        request.ClinicalJustification  = model.ClinicalJustification;
        request.Priority               = model.Priority;
        request.ApprovedUnitsRequested = model.ApprovedUnitsRequested;
        request.UpdatedAt              = _clock.UtcNow;

        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("PaRequest", request.ID.ToString(), "Updated",
            oldValues: $"{{\"Status\":\"{oldStatus}\"}}",
            newValues: $"{{\"PatientMrn\":\"{model.PatientMrn}\",\"Status\":\"{oldStatus}\"}}",
            cancellationToken: ct);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public async Task DeleteDraftAsync(int requestId, CancellationToken ct = default)
    {
        RequireActive();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var request = await db.PaRequests
            .FirstOrDefaultAsync(r => r.ID == requestId, ct)
            ?? throw new EntityNotFoundException(nameof(PaRequest), requestId.ToString());

        var isAdmin = _currentUser.IsInRole(Roles.Admin);
        _rules.ValidateCanHardDelete(request, _currentUser.UserId, isAdmin); // BR-014

        db.PaRequests.Remove(request);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("PaRequest", requestId.ToString(), "Deleted",
            oldValues: $"{{\"Status\":\"Draft\",\"RequestNumber\":\"{request.RequestNumber}\"}}",
            cancellationToken: ct);
    }

    // ── Query ─────────────────────────────────────────────────────────────────

    public async Task<PaRequest?> GetByIdAsync(int requestId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var query = db.PaRequests
            .AsNoTracking()
            .Include(r => r.InsurancePlan)
            .Include(r => r.ProcedureCode)
            .Include(r => r.DenialReason)
            .Where(r => r.ID == requestId);

        // Provider can only see their own patients
        if (_currentUser.IsInRole(Roles.Provider) && !_currentUser.IsInRole(Roles.Admin))
            query = query.Where(r => r.ProviderID == _currentUser.UserId);

        return await query.FirstOrDefaultAsync(ct);
    }

    public async Task<(IReadOnlyList<PaRequest> Items, int TotalCount)> GetQueueAsync(
        PaRequestFilter filter, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        IQueryable<PaRequest> query = db.PaRequests
            .AsNoTracking()
            .Include(r => r.InsurancePlan)
            .Include(r => r.ProcedureCode);

        // RBAC: Provider sees only their own patients' requests
        if (_currentUser.IsInRole(Roles.Provider) && !_currentUser.IsInRole(Roles.Admin))
            query = query.Where(r => r.ProviderID == _currentUser.UserId);

        // ── Filters ──────────────────────────────────────────────────────────
        if (filter.Status.HasValue)
            query = query.Where(r => r.Status == filter.Status.Value);

        if (filter.Priority.HasValue)
            query = query.Where(r => r.Priority == filter.Priority.Value);

        if (filter.InsurancePlanID.HasValue)
            query = query.Where(r => r.InsurancePlanID == filter.InsurancePlanID.Value);

        if (filter.ProcedureCodeID.HasValue)
            query = query.Where(r => r.ProcedureCodeID == filter.ProcedureCodeID.Value);

        if (!string.IsNullOrEmpty(filter.ReviewerUserID))
            query = query.Where(r => r.ReviewerUserID == filter.ReviewerUserID);

        if (!string.IsNullOrEmpty(filter.SubmittedByUserID))
            query = query.Where(r => r.SubmittedByUserID == filter.SubmittedByUserID);

        if (filter.SubmittedFrom.HasValue)
            query = query.Where(r => r.SubmittedAt >= filter.SubmittedFrom.Value);

        if (filter.SubmittedTo.HasValue)
            query = query.Where(r => r.SubmittedAt <= filter.SubmittedTo.Value);

        if (!string.IsNullOrWhiteSpace(filter.PatientSearch))
        {
            var term = filter.PatientSearch.Trim();
            query = query.Where(r =>
                r.PatientName.Contains(term) ||
                r.PatientMrn.Contains(term));
        }

        // ── Count before pagination ───────────────────────────────────────────
        var totalCount = await query.CountAsync(ct);

        // ── Sort ─────────────────────────────────────────────────────────────
        query = filter.SortBy switch
        {
            "DecisionDueDate" => filter.SortDescending
                ? query.OrderByDescending(r => r.DecisionDueDate)
                : query.OrderBy(r => r.DecisionDueDate),
            "PatientName" => filter.SortDescending
                ? query.OrderByDescending(r => r.PatientName)
                : query.OrderBy(r => r.PatientName),
            "Priority" => filter.SortDescending
                ? query.OrderByDescending(r => r.Priority)
                : query.OrderBy(r => r.Priority),
            _ => filter.SortDescending
                ? query.OrderByDescending(r => r.SubmittedAt)
                : query.OrderBy(r => r.SubmittedAt),
        };

        // ── Pagination ────────────────────────────────────────────────────────
        var skip = (filter.Page - 1) * filter.PageSize;
        var items = await query
            .Skip(skip)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    // ── Comments ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PaComment>> GetCommentsAsync(
        int requestId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        IQueryable<PaComment> query = db.PaComments
            .AsNoTracking()
            .Where(c => c.PaRequestID == requestId)
            .OrderBy(c => c.AuthoredAt);

        // BUG-009 mitigation: filter internal comments at the query layer for Provider role
        if (_currentUser.IsInRole(Roles.Provider) && !_currentUser.IsInRole(Roles.Admin))
            query = query.Where(c => !c.IsInternalOnly);

        return await query.ToListAsync(ct);
    }

    public async Task<PaComment> AddCommentAsync(
        int requestId, string text, bool isInternal, CancellationToken ct = default)
    {
        RequireActive();

        if (isInternal)
            RequireAnyRole(Roles.Specialist, Roles.Reviewer, Roles.Admin);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Verify the request exists and is visible to the caller
        var requestExists = await db.PaRequests.AnyAsync(r => r.ID == requestId, ct);
        if (!requestExists)
            throw new EntityNotFoundException(nameof(PaRequest), requestId.ToString());

        var comment = new PaComment
        {
            PaRequestID      = requestId,
            CommentText      = text,
            IsInternalOnly   = isInternal,
            AuthoredByUserID = _currentUser.UserId,
            AuthoredAt       = _clock.UtcNow,
        };
        db.PaComments.Add(comment);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("PaComment", comment.ID.ToString(), "Created",
            newValues: $"{{\"PaRequestID\":{requestId},\"IsInternalOnly\":{isInternal.ToString().ToLowerInvariant()}}}",
            cancellationToken: ct);

        return comment;
    }

    // ── Documents ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PaDocument>> GetDocumentsAsync(
        int requestId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PaDocuments
            .AsNoTracking()
            .Where(d => d.PaRequestID == requestId)
            .OrderBy(d => d.UploadedAt)
            .ToListAsync(ct);
    }

    public async Task<PaDocument> AddDocumentAsync(
        int requestId,
        string fileName,
        string documentType,
        string contentType,
        long fileSizeBytes,
        CancellationToken ct = default)
    {
        RequireActive();
        RequireAnyRole(Roles.Specialist, Roles.Provider, Roles.Reviewer, Roles.Admin);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new BusinessRuleViolationException("DOC", "File name is required.");
        if (string.IsNullOrWhiteSpace(documentType))
            throw new BusinessRuleViolationException("DOC", "Document type is required.");
        if (fileSizeBytes < 0)
            throw new BusinessRuleViolationException("DOC", "File size must be non-negative.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var requestExists = await db.PaRequests.AnyAsync(r => r.ID == requestId, ct);
        if (!requestExists)
            throw new EntityNotFoundException(nameof(PaRequest), requestId.ToString());

        var now = _clock.UtcNow;
        var doc = new PaDocument
        {
            PaRequestID      = requestId,
            FileName         = fileName,
            DocumentType     = documentType,
            ContentType      = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            FileSizeBytes    = fileSizeBytes,
            StoragePath      = $"metadata-only/{requestId}/{now:yyyyMMddHHmmss}/{fileName}",
            UploadedByUserID = _currentUser.UserId,
            UploadedAt       = now,
        };

        db.PaDocuments.Add(doc);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("PaDocument", doc.ID.ToString(), "Uploaded",
            newValues: $"{{\"PaRequestID\":{requestId},\"FileName\":\"{fileName}\",\"DocumentType\":\"{documentType}\"}}",
            cancellationToken: ct);

        return doc;
    }

    // ── Status history ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PaStatusHistory>> GetStatusHistoryAsync(
        int requestId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.PaStatusHistories
            .AsNoTracking()
            .Where(h => h.PaRequestID == requestId)
            .OrderBy(h => h.ChangedAt)
            .ToListAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RequireActive()
    {
        if (!_currentUser.IsActive)
            throw new AuthorizationException(
                $"User account '{_currentUser.UserName}' is inactive.");
    }

    private void RequireAnyRole(params string[] roles)
    {
        if (!roles.Any(_currentUser.IsInRole))
            throw new AuthorizationException(
                $"User '{_currentUser.UserName}' does not have any of the required roles: " +
                string.Join(", ", roles) + ".");
    }
}
