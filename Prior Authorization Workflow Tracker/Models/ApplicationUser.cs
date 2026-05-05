using Microsoft.AspNetCore.Identity;

namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>
/// Extends ASP.NET Core Identity user with application-specific fields (§7.1).
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Display name shown in the UI and denormalized into AuditLogs.</summary>
    public string FullName { get; set; } = string.Empty;

    public string? Department { get; set; }

    /// <summary>
    /// When false the user may not log in and no mutating operations are
    /// permitted during an active circuit (BUG-012 mitigation).
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<PaRequest> SubmittedRequests { get; set; } = [];
    public ICollection<PaRequest> ProviderRequests { get; set; } = [];
    public ICollection<PaRequest> ReviewedRequests { get; set; } = [];
    public ICollection<PaStatusHistory> StatusChanges { get; set; } = [];
    public ICollection<PaDocument> UploadedDocuments { get; set; } = [];
    public ICollection<PaComment> AuthoredComments { get; set; } = [];
    public ICollection<Notification> Notifications { get; set; } = [];
}
