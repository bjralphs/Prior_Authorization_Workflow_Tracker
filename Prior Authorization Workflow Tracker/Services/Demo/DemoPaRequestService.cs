using Prior_Authorization_Workflow_Tracker.Constants;
using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>Full CRUD + filtering against the in-memory DemoDataStore.</summary>
public sealed class DemoPaRequestService : IPaRequestService
{
    private readonly DemoDataStore          _store;
    private readonly ICurrentUserService   _currentUser;
    private readonly IAuditService          _audit;
    private readonly IBusinessRuleService   _rules;
    private readonly IDateTimeProvider      _clock;

    public DemoPaRequestService(
        DemoDataStore store,
        ICurrentUserService currentUser,
        IAuditService audit,
        IBusinessRuleService rules,
        IDateTimeProvider clock)
    {
        _store       = store;
        _currentUser = currentUser;
        _audit       = audit;
        _rules       = rules;
        _clock       = clock;
    }

    public async Task<PaRequest> CreateDraftAsync(CreatePaRequestModel model, CancellationToken ct = default)
    {
        var plan = _store.InsurancePlans.FirstOrDefault(p => p.ID == model.InsurancePlanID)
            ?? throw new EntityNotFoundException("InsurancePlan", model.InsurancePlanID.ToString());
        var proc = _store.ProcedureCodes.FirstOrDefault(p => p.ID == model.ProcedureCodeID)
            ?? throw new EntityNotFoundException("ProcedureCode", model.ProcedureCodeID.ToString());

        var id = _store.NextRequestId();
        var now = _clock.UtcNow;

        var provider = _store.Users.FirstOrDefault(u => u.Id == model.ProviderID);

        var request = new PaRequest
        {
            ID                      = id,
            RequestNumber           = $"PA-{now.Year}-{id:D5}",
            PatientMrn              = model.PatientMrn,
            PatientName             = model.PatientName,
            PatientDob              = model.PatientDob,
            InsurancePlanID         = model.InsurancePlanID,
            InsurancePlan           = plan,
            ProviderID              = model.ProviderID,
            ProcedureCodeID         = model.ProcedureCodeID,
            ProcedureCode           = proc,
            DiagnosisCode           = model.DiagnosisCode,
            ClinicalJustification   = model.ClinicalJustification,
            Priority                = model.Priority,
            ApprovedUnitsRequested  = model.ApprovedUnitsRequested,
            Status                  = PaStatus.Draft,
            SubmittedByUserID       = _currentUser.UserId,
            CreatedAt = now,
            UpdatedAt               = now,
        };

        lock (_store.Requests) { _store.Requests.Add(request); }

        _store.StatusHistory.Add(new PaStatusHistory
        {
            ID              = _store.NextHistoryId(),
            PaRequestID     = id,
            FromStatus      = PaStatus.Draft,
            ToStatus        = PaStatus.Draft,
            ChangedByUserID = _currentUser.UserId ?? "SYSTEM",
            ChangedAt       = now,
            ChangeReason    = "Request created",
        });

        await _audit.LogAsync("PaRequest", id.ToString(), "Created", null, null, ct);
        return request;
    }

    public async Task UpdateDraftAsync(int requestId, CreatePaRequestModel model, CancellationToken ct = default)
    {
        var r = _store.Requests.FirstOrDefault(r => r.ID == requestId)
            ?? throw new EntityNotFoundException("PaRequest", requestId.ToString());

        if (r.Status is not (PaStatus.Draft or PaStatus.PendingInfo))
            throw new WorkflowTransitionException(r.Status, r.Status);

        var plan = _store.InsurancePlans.FirstOrDefault(p => p.ID == model.InsurancePlanID)
            ?? throw new EntityNotFoundException("InsurancePlan", model.InsurancePlanID.ToString());
        var proc = _store.ProcedureCodes.FirstOrDefault(p => p.ID == model.ProcedureCodeID)
            ?? throw new EntityNotFoundException("ProcedureCode", model.ProcedureCodeID.ToString());

        r.PatientMrn            = model.PatientMrn;
        r.PatientName           = model.PatientName;
        r.PatientDob            = model.PatientDob;
        r.InsurancePlanID       = model.InsurancePlanID;
        r.InsurancePlan         = plan;
        r.ProviderID            = model.ProviderID;
        r.ProcedureCodeID       = model.ProcedureCodeID;
        r.ProcedureCode         = proc;
        r.DiagnosisCode         = model.DiagnosisCode;
        r.ClinicalJustification = model.ClinicalJustification;
        r.Priority              = model.Priority;
        r.ApprovedUnitsRequested= model.ApprovedUnitsRequested;
        r.UpdatedAt             = _clock.UtcNow;

        await _audit.LogAsync("PaRequest", r.ID.ToString(), "Updated", null, null, ct);
    }

    public async Task DeleteDraftAsync(int requestId, CancellationToken ct = default)
    {
        var r = _store.Requests.FirstOrDefault(r => r.ID == requestId)
            ?? throw new EntityNotFoundException("PaRequest", requestId.ToString());

        bool isAdmin = _currentUser.IsInRole(Roles.Admin);
        _rules.ValidateCanHardDelete(r, _currentUser.UserId, isAdmin);

        lock (_store.Requests) { _store.Requests.Remove(r); }
        await _audit.LogAsync("PaRequest", r.ID.ToString(), "Deleted", null, null, ct);
    }

    public Task<PaRequest?> GetByIdAsync(int requestId, CancellationToken ct = default)
    {
        var r = _store.Requests.FirstOrDefault(r => r.ID == requestId);

        // Providers only see their own patients
        if (r != null && _currentUser.IsInRole(Roles.Provider) && r.ProviderID != _currentUser.UserId)
            r = null;

        return Task.FromResult(r);
    }

    public Task<(IReadOnlyList<PaRequest> Items, int TotalCount)> GetQueueAsync(
        PaRequestFilter filter, CancellationToken ct = default)
    {
        IEnumerable<PaRequest> query = _store.Requests;

        // Role scoping
        if (_currentUser.IsInRole(Roles.Provider))
            query = query.Where(r => r.ProviderID == _currentUser.UserId);

        // Filters
        if (filter.Status.HasValue)
            query = query.Where(r => r.Status == filter.Status.Value);
        if (filter.Priority.HasValue)
            query = query.Where(r => r.Priority == filter.Priority.Value);
        if (filter.InsurancePlanID.HasValue)
            query = query.Where(r => r.InsurancePlanID == filter.InsurancePlanID.Value);
        if (filter.ProcedureCodeID.HasValue)
            query = query.Where(r => r.ProcedureCodeID == filter.ProcedureCodeID.Value);
        if (!string.IsNullOrWhiteSpace(filter.ReviewerUserID))
            query = query.Where(r => r.ReviewerUserID == filter.ReviewerUserID);
        if (!string.IsNullOrWhiteSpace(filter.SubmittedByUserID))
            query = query.Where(r => r.SubmittedByUserID == filter.SubmittedByUserID);
        if (filter.SubmittedFrom.HasValue)
            query = query.Where(r => r.SubmittedAt >= filter.SubmittedFrom.Value);
        if (filter.SubmittedTo.HasValue)
            query = query.Where(r => r.SubmittedAt <= filter.SubmittedTo.Value);
        if (!string.IsNullOrWhiteSpace(filter.PatientSearch))
        {
            var term = filter.PatientSearch.ToLowerInvariant();
            query = query.Where(r =>
                r.PatientName.ToLowerInvariant().Contains(term) ||
                r.PatientMrn.ToLowerInvariant().Contains(term));
        }

        var totalCount = query.Count();

        // Sorting
        query = filter.SortBy switch
        {
            "PatientName"     => filter.SortDescending ? query.OrderByDescending(r => r.PatientName)     : query.OrderBy(r => r.PatientName),
            "DecisionDueDate" => filter.SortDescending ? query.OrderByDescending(r => r.DecisionDueDate) : query.OrderBy(r => r.DecisionDueDate),
            "Priority"        => filter.SortDescending ? query.OrderByDescending(r => r.Priority)        : query.OrderBy(r => r.Priority),
            _                 => filter.SortDescending ? query.OrderByDescending(r => r.SubmittedAt ?? r.CreatedAt) : query.OrderBy(r => r.SubmittedAt ?? r.CreatedAt),
        };

        // Paging
        var items = query
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        return Task.FromResult<(IReadOnlyList<PaRequest>, int)>((items, totalCount));
    }

    public Task<IReadOnlyList<PaComment>> GetCommentsAsync(int requestId, CancellationToken ct = default)
    {
        IReadOnlyList<PaComment> result = _store.Comments
            .Where(c => c.PaRequestID == requestId &&
                        (!c.IsInternalOnly || !_currentUser.IsInRole(Roles.Provider)))
            .OrderBy(c => c.AuthoredAt)
            .ToList();
        return Task.FromResult(result);
    }

    public async Task<PaComment> AddCommentAsync(int requestId, string text, bool IsInternalOnly, CancellationToken ct = default)
    {
        if (IsInternalOnly && _currentUser.IsInRole(Roles.Provider))
            throw new Exceptions.AuthorizationException("Providers cannot add internal comments.");

        var comment = new PaComment
        {
            ID           = _store.NextCommentId(),
            PaRequestID  = requestId,
            AuthoredByUserID = _currentUser.UserId,
            CommentText = text,
            IsInternalOnly   = IsInternalOnly,
            AuthoredAt = _clock.UtcNow,
        };
        lock (_store.Comments) { _store.Comments.Add(comment); }
        await _audit.LogAsync("PaComment", comment.ID.ToString(), "Added", null, null, ct);
        return comment;
    }

    public Task<IReadOnlyList<PaDocument>> GetDocumentsAsync(int requestId, CancellationToken ct = default)
    {
        IReadOnlyList<PaDocument> result = _store.Documents
            .Where(d => d.PaRequestID == requestId)
            .OrderBy(d => d.UploadedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public async Task<PaDocument> AddDocumentAsync(int requestId, string fileName, string documentType,
        string contentType, long fileSizeBytes, CancellationToken ct = default)
    {
        var doc = new PaDocument
        {
            ID               = _store.NextDocId(),
            PaRequestID      = requestId,
            FileName         = fileName,
            StoragePath      = "/demo/document",
            FileSizeBytes         = fileSizeBytes,
            ContentType      = contentType,
            UploadedByUserID = _currentUser.UserId,
            UploadedAt       = _clock.UtcNow,
        };
        lock (_store.Documents) { _store.Documents.Add(doc); }
        await _audit.LogAsync("PaDocument", doc.ID.ToString(), "Uploaded", null, null, ct);
        return doc;
    }

    public Task<IReadOnlyList<PaStatusHistory>> GetStatusHistoryAsync(int requestId, CancellationToken ct = default)
    {
        IReadOnlyList<PaStatusHistory> result = _store.StatusHistory
            .Where(h => h.PaRequestID == requestId)
            .OrderBy(h => h.ChangedAt)
            .ToList();
        return Task.FromResult(result);
    }
}
