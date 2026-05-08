namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>
/// User entity. In the demo branch this is a plain POCO (no Identity dependency).
/// The real app extends IdentityUser; the demo stores users in DemoDataStore.
/// </summary>
public class ApplicationUser
{
    public string Id { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? SecurityStamp { get; set; }
    public string? ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString();
    public bool LockoutEnabled { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool PhoneNumberConfirmed { get; set; }

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
