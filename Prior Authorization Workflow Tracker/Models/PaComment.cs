namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>
/// Comment thread entry on a PA request (§7.1).
/// IsInternalOnly = true means the comment is hidden from Provider role (BUG-009 mitigation).
/// Visibility is enforced at the query layer in PaRequestService, not only in the UI.
/// </summary>
public class PaComment
{
    public int ID { get; set; }

    public int PaRequestID { get; set; }

    public string CommentText { get; set; } = string.Empty;

    /// <summary>
    /// When true, this comment is not returned for Provider-role callers.
    /// PaRequestService.GetCommentsAsync filters on this field server-side.
    /// </summary>
    public bool IsInternalOnly { get; set; }

    public string AuthoredByUserID { get; set; } = string.Empty;

    public DateTime AuthoredAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public PaRequest PaRequest { get; set; } = null!;
    public ApplicationUser AuthoredByUser { get; set; } = null!;
}
