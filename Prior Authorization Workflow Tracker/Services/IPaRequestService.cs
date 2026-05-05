using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Models;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Manages PA request CRUD operations (FR-001, FR-002, FR-003, §8.1–§8.3).
/// All workflow status transitions are delegated to IWorkflowService.
/// All business rules are delegated to IBusinessRuleService.
/// </summary>
public interface IPaRequestService
{
    // ── Create / Update / Delete ──────────────────────────────────────────────

    /// <summary>
    /// Creates a new Draft request. Minimum required: PatientMrn, InsurancePlanID, ProcedureCodeID.
    /// Generates RequestNumber from the IDENTITY ID (BUG-011 mitigation).
    /// Writes a "Created" audit entry.
    /// </summary>
    Task<PaRequest> CreateDraftAsync(CreatePaRequestModel model, CancellationToken ct = default);

    /// <summary>
    /// Updates a Draft or PendingInfo request. Rejects edits to any other status.
    /// Writes an "Updated" audit entry.
    /// </summary>
    Task UpdateDraftAsync(int requestId, CreatePaRequestModel model, CancellationToken ct = default);

    /// <summary>
    /// Hard-deletes a Draft request (BR-014).
    /// Only the creating Specialist or an Admin may call this.
    /// </summary>
    Task DeleteDraftAsync(int requestId, CancellationToken ct = default);

    // ── Query ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a single request by ID with role-aware visibility.
    /// Provider role only sees requests where ProviderID matches their user ID.
    /// Returns null when not found or not visible to the caller.
    /// </summary>
    Task<PaRequest?> GetByIdAsync(int requestId, CancellationToken ct = default);

    /// <summary>
    /// Returns the paged, filtered, sorted request queue visible to the current user (FR-002).
    /// Provider sees only their own patients' requests.
    /// All other roles see everything.
    /// Uses AsNoTracking() per NFR-002.
    /// </summary>
    Task<(IReadOnlyList<PaRequest> Items, int TotalCount)> GetQueueAsync(
        PaRequestFilter filter, CancellationToken ct = default);

    // ── Comments ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns comments for a request. Internal-only comments are excluded for Provider callers
    /// at the query layer (BUG-009 mitigation).
    /// </summary>
    Task<IReadOnlyList<PaComment>> GetCommentsAsync(int requestId, CancellationToken ct = default);

    /// <summary>
    /// Adds a comment to a request.
    /// Internal-only comments may only be added by Specialist, Reviewer, or Admin.
    /// </summary>
    Task<PaComment> AddCommentAsync(
        int requestId, string text, bool isInternal, CancellationToken ct = default);

    // ── Documents ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all documents attached to a request, ordered ascending by UploadedAt (FR-003).
    /// </summary>
    Task<IReadOnlyList<PaDocument>> GetDocumentsAsync(
        int requestId, CancellationToken ct = default);

    /// <summary>
    /// Attaches document metadata to a request (T-C4 / §8.3, §10.1 Upload documents permission).
    /// In v1, no file bytes are persisted — StoragePath is a placeholder (known limitation §18).
    /// Roles permitted: Specialist, Treating Provider, Payer Reviewer, Admin.
    /// Writes a "Uploaded" audit entry for AuditLogs compliance (§11).
    /// </summary>
    Task<PaDocument> AddDocumentAsync(
        int requestId,
        string fileName,
        string documentType,
        string contentType,
        long fileSizeBytes,
        CancellationToken ct = default);

    // ── Status history ────────────────────────────────────────────────────────

    /// <summary>Returns the full status history for a request, ordered ascending by ChangedAt.</summary>
    Task<IReadOnlyList<PaStatusHistory>> GetStatusHistoryAsync(
        int requestId, CancellationToken ct = default);
}
