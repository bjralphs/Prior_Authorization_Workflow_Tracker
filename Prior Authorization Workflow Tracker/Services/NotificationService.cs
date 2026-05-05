using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Persists in-app notification records.
/// Uses IDbContextFactory so notification writes are independent of the calling
/// service's change-tracked context (same pattern as AuditService).
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IDbContextFactory<AppDbContext> dbFactory,
        IDateTimeProvider clock,
        ILogger<NotificationService> logger)
    {
        _dbFactory = dbFactory;
        _clock = clock;
        _logger = logger;
    }

    public async Task CreateAsync(
        string recipientUserId,
        int? requestId,
        string message,
        NotificationType notificationType,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(recipientUserId)) return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.Notifications.Add(new Notification
            {
                UserID           = recipientUserId,
                PaRequestID      = requestId,
                Message          = message,
                NotificationType = notificationType,
                IsRead           = false,
                CreatedAt        = _clock.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Notification failures must not propagate — same resilience pattern as AuditService.
            _logger.LogError(ex,
                "Failed to create notification for user {UserId} on request {RequestId}",
                recipientUserId, requestId);
        }
    }

    public async Task<IReadOnlyList<Notification>> GetUnreadAsync(
        string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications
            .AsNoTracking()
            .Where(n => n.UserID == userId && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.UserID == userId && !n.IsRead, ct);
    }

    public async Task MarkReadAsync(int notificationId, string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.ID == notificationId && n.UserID == userId, ct);

        if (notification is null) return; // silently ignore: wrong owner or already deleted

        notification.IsRead = true;
        notification.ReadAt = _clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var unread = await db.Notifications
            .Where(n => n.UserID == userId && !n.IsRead)
            .ToListAsync(ct);

        if (unread.Count == 0) return;

        var now = _clock.UtcNow;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
        }
        await db.SaveChangesAsync(ct);
    }
}
