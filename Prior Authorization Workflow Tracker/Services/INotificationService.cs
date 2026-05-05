using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Creates and manages in-app notification records (§7.1 Notifications, §8.5, §8.6).
/// Email delivery is a stub in v1 — notifications are persisted to the Notifications
/// table only (PRD §18 known limitation).
/// </summary>
public interface INotificationService
{
    /// <summary>Creates a new in-app notification for the specified recipient.</summary>
    Task CreateAsync(
        string recipientUserId,
        int? requestId,
        string message,
        NotificationType notificationType,
        CancellationToken ct = default);

    /// <summary>Returns all unread notifications for the user (for the notification centre page).</summary>
    Task<IReadOnlyList<Notification>> GetUnreadAsync(string userId, CancellationToken ct = default);

    /// <summary>Returns the count of unread notifications for the nav-menu badge.</summary>
    Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default);

    /// <summary>Marks a single notification read. Enforces ownership so users cannot mark others' notifications.</summary>
    Task MarkReadAsync(int notificationId, string userId, CancellationToken ct = default);

    /// <summary>Marks all of the specified user's notifications as read.</summary>
    Task MarkAllReadAsync(string userId, CancellationToken ct = default);
}
