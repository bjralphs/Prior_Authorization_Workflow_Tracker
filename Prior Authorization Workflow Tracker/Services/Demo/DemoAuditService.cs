using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>
/// Records audit entries to the in-memory DemoDataStore.
/// Never throws — silently swallows errors like the real AuditService.
/// </summary>
public sealed class DemoAuditService : IAuditService
{
    private readonly DemoDataStore           _store;
    private readonly DemoCurrentUserService  _currentUser;

    public DemoAuditService(DemoDataStore store, DemoCurrentUserService currentUser)
    {
        _store       = store;
        _currentUser = currentUser;
    }

    public Task LogAsync(string entityName, string entityId, string action,
        string? oldValues = null, string? newValues = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_store.AuditLogs)
            {
                _store.AuditLogs.Add(new AuditLog
                {
                    ID         = _store.NextAuditId(),
                    EntityName = entityName,
                    EntityID   = entityId,
                    Action     = action,
                    OldValues  = oldValues,
                    NewValues  = newValues,
                    UserID     = _currentUser.UserId ?? string.Empty,
                    UserName   = _currentUser.UserName,
                    OccurredAt = DateTime.UtcNow,
                });
            }
        }
        catch { /* audit must not throw */ }
        return Task.CompletedTask;
    }

    public Task LogAsSystemAsync(string entityName, string entityId, string action,
        string? oldValues = null, string? newValues = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_store.AuditLogs)
            {
                _store.AuditLogs.Add(new AuditLog
                {
                    ID         = _store.NextAuditId(),
                    EntityName = entityName,
                    EntityID   = entityId,
                    Action     = action,
                    OldValues  = oldValues,
                    NewValues  = newValues,
                    UserID     = "SYSTEM",
                    UserName   = "System",
                    OccurredAt = DateTime.UtcNow,
                });
            }
        }
        catch { /* audit must not throw */ }
        return Task.CompletedTask;
    }
}
