using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services;
using Tests.Unit.Fakes;

namespace Tests.Unit.Services;

/// <summary>
/// Unit tests for NotificationService (T15, §13.3).
/// </summary>
public class NotificationServiceTests
{
    private static AppDbContext MakeDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(opts);
    }

    private static NotificationService Build(string dbName, FakeDateTimeProvider? clock = null)
    {
        clock ??= new FakeDateTimeProvider();
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => MakeDb(dbName));
        return new NotificationService(factory.Object, clock, NullLogger<NotificationService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_PersistsNotificationWithIsReadFalse()
    {
        var svc = Build("notif_create");

        await svc.CreateAsync("user-1", requestId: 5, "Your request was approved.",
            NotificationType.DecisionRendered);

        var db = MakeDb("notif_create");
        var saved = await db.Notifications.FirstOrDefaultAsync(n => n.UserID == "user-1");
        Assert.NotNull(saved);
        Assert.False(saved.IsRead);
        Assert.Equal(5, saved.PaRequestID);
        Assert.Equal(NotificationType.DecisionRendered, saved.NotificationType);
    }

    [Fact]
    public async Task CreateAsync_EmptyUserId_DoesNotThrowAndDoesNotPersist()
    {
        var svc = Build("notif_empty_uid");

        var ex = await Record.ExceptionAsync(
            () => svc.CreateAsync("", null, "msg", NotificationType.StatusChanged));

        Assert.Null(ex);
        var db = MakeDb("notif_empty_uid");
        Assert.Equal(0, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task GetUnreadAsync_ReturnsOnlyUnreadForUser()
    {
        var svc = Build("notif_unread");
        var db = MakeDb("notif_unread");

        db.Notifications.Add(new Notification
        {
            UserID = "user-1", Message = "Unread", NotificationType = NotificationType.StatusChanged,
            IsRead = false, CreatedAt = DateTime.UtcNow,
        });
        db.Notifications.Add(new Notification
        {
            UserID = "user-1", Message = "Already read", NotificationType = NotificationType.StatusChanged,
            IsRead = true, CreatedAt = DateTime.UtcNow,
        });
        db.Notifications.Add(new Notification
        {
            UserID = "user-2", Message = "Other user", NotificationType = NotificationType.StatusChanged,
            IsRead = false, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await svc.GetUnreadAsync("user-1");

        Assert.Single(result);
        Assert.Equal("Unread", result[0].Message);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsCorrectCount()
    {
        var svc = Build("notif_count");
        var db = MakeDb("notif_count");

        for (var i = 0; i < 3; i++)
        {
            db.Notifications.Add(new Notification
            {
                UserID = "user-1", Message = $"Notif {i}",
                NotificationType = NotificationType.StatusChanged,
                IsRead = false, CreatedAt = DateTime.UtcNow,
            });
        }
        db.Notifications.Add(new Notification
        {
            UserID = "user-1", Message = "Read",
            NotificationType = NotificationType.StatusChanged,
            IsRead = true, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var count = await svc.GetUnreadCountAsync("user-1");
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task MarkReadAsync_UpdatesIsReadAndReadAt()
    {
        var clock = new FakeDateTimeProvider { UtcNow = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc) };
        var svc = Build("notif_markread", clock);
        var db = MakeDb("notif_markread");

        db.Notifications.Add(new Notification
        {
            ID = 1, UserID = "user-1", Message = "Test",
            NotificationType = NotificationType.StatusChanged,
            IsRead = false, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await svc.MarkReadAsync(notificationId: 1, userId: "user-1");

        // Use a fresh context to avoid stale tracking state
        var freshDb = MakeDb("notif_markread");
        var updated = await freshDb.Notifications.FindAsync(1);
        Assert.NotNull(updated);
        Assert.True(updated.IsRead);
        Assert.Equal(clock.UtcNow, updated.ReadAt);
    }

    [Fact]
    public async Task MarkReadAsync_WrongOwner_DoesNotUpdate()
    {
        // Security: user-2 cannot mark user-1's notification as read
        var svc = Build("notif_markread_wrongowner");
        var db = MakeDb("notif_markread_wrongowner");

        db.Notifications.Add(new Notification
        {
            ID = 1, UserID = "user-1", Message = "Test",
            NotificationType = NotificationType.StatusChanged,
            IsRead = false, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await svc.MarkReadAsync(notificationId: 1, userId: "user-2"); // wrong owner

        var freshDb = MakeDb("notif_markread_wrongowner");
        var n = await freshDb.Notifications.FindAsync(1);
        Assert.NotNull(n);
        Assert.False(n.IsRead); // unchanged
    }

    [Fact]
    public async Task MarkAllReadAsync_MarksAllUnreadForUser()
    {
        var svc = Build("notif_markallread");
        var db = MakeDb("notif_markallread");

        for (var i = 0; i < 3; i++)
        {
            db.Notifications.Add(new Notification
            {
                UserID = "user-1", Message = $"Notif {i}",
                NotificationType = NotificationType.StatusChanged,
                IsRead = false, CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();

        await svc.MarkAllReadAsync("user-1");

        var remaining = await db.Notifications
            .CountAsync(n => n.UserID == "user-1" && !n.IsRead);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public async Task ExpirationAlert_ShouldCreateNotificationForSpecialistAndProvider()
    {
        // §13.3 named scenario: NotificationService_ExpirationAlert_ShouldCreateNotificationForSpecialistAndProvider
        var svc = Build("notif_expiry");

        await svc.CreateAsync("specialist-id", requestId: 7,
            "Authorization has expired.", NotificationType.ExpirationAlert);
        await svc.CreateAsync("provider-id", requestId: 7,
            "Authorization has expired.", NotificationType.ExpirationAlert);

        var db = MakeDb("notif_expiry");
        var notifications = await db.Notifications
            .Where(n => n.PaRequestID == 7)
            .ToListAsync();

        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, n => Assert.Equal(NotificationType.ExpirationAlert, n.NotificationType));
        Assert.Contains(notifications, n => n.UserID == "specialist-id");
        Assert.Contains(notifications, n => n.UserID == "provider-id");
    }
}
