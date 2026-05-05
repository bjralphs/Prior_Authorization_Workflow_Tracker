namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>
/// In-app notification record (§7.1).
/// Email delivery is a stub in v1 — notifications are persisted here only (§18 known limitations).
/// </summary>
public class Notification
{
    public int ID { get; set; }

    /// <summary>FK to AspNetUsers; the intended recipient.</summary>
    public string UserID { get; set; } = string.Empty;

    /// <summary>Nullable: some system notifications may not relate to a specific request.</summary>
    public int? PaRequestID { get; set; }

    public string Message { get; set; } = string.Empty;

    public NotificationType NotificationType { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAt { get; set; }

    // Navigation
    public ApplicationUser User { get; set; } = null!;
    public PaRequest? PaRequest { get; set; }
}
