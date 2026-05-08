using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>Persists notifications to the in-memory DemoDataStore.</summary>
public sealed class DemoNotificationService : INotificationService
{
    private readonly DemoDataStore _store;

    public DemoNotificationService(DemoDataStore store) => _store = store;

    public Task CreateAsync(string recipientUserId, int? requestId, string message,
        NotificationType notificationType, CancellationToken ct = default)
    {
        lock (_store.Notifications)
        {
            _store.Notifications.Add(new Notification
            {
                ID               = _store.NextNotifId(),
                UserID           = recipientUserId,
                PaRequestID      = requestId,
                Message          = message,
                NotificationType = notificationType,
                IsRead           = false,
                CreatedAt        = DateTime.UtcNow,
            });
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Notification>> GetUnreadAsync(string userId, CancellationToken ct = default)
    {
        IReadOnlyList<Notification> result = _store.Notifications
            .Where(n => n.UserID == userId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default)
    {
        var count = _store.Notifications.Count(n => n.UserID == userId && !n.IsRead);
        return Task.FromResult(count);
    }

    public Task MarkReadAsync(int notificationId, string userId, CancellationToken ct = default)
    {
        var notif = _store.Notifications.FirstOrDefault(n => n.ID == notificationId && n.UserID == userId);
        if (notif != null)
        {
            notif.IsRead = true;
            notif.ReadAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task MarkAllReadAsync(string userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var n in _store.Notifications.Where(n => n.UserID == userId && !n.IsRead))
        {
            n.IsRead = true;
            n.ReadAt = now;
        }
        return Task.CompletedTask;
    }
}
