using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Persists append-only AuditLog records (§11).
///
/// Design decisions:
///  - Uses its own DbContext scope (IDbContextFactory) so audit writes are
///    independent transactions from the calling service's context, preventing
///    a partial business-operation rollback from silently discarding audit rows.
///  - Failures are caught and logged at Error level to Serilog; they do not
///    propagate to the caller (audit must never break the primary operation).
///  - OldValues / NewValues are pre-serialized JSON strings provided by callers
///    to keep this class free of serialization concerns.
/// </summary>
public sealed class AuditService : IAuditService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditService> _logger;

    public AuditService(
        IDbContextFactory<AppDbContext> contextFactory,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditService> logger)
    {
        _contextFactory = contextFactory;
        _currentUser = currentUser;
        _clock = clock;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task LogAsync(
        string entityName,
        string entityId,
        string action,
        string? oldValues = null,
        string? newValues = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var entry = new AuditLog
            {
                EntityName    = entityName,
                EntityID      = entityId,
                Action        = action,
                OldValues     = oldValues,
                NewValues     = newValues,
                UserID        = _currentUser.UserId,
                UserName      = _currentUser.UserName,
                IpAddress     = _currentUser.IpAddress,
                CorrelationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"] as string,
                OccurredAt    = _clock.UtcNow
            };

            db.AuditLogs.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Audit failure must not surface to the user or break the business operation.
            _logger.LogError(ex,
                "Failed to write audit log entry. Entity={EntityName} ID={EntityId} Action={Action}",
                entityName, entityId, action);
        }
    }

    /// <inheritdoc />
    public async Task LogAsSystemAsync(
        string entityName,
        string entityId,
        string action,
        string? oldValues = null,
        string? newValues = null,
        CancellationToken cancellationToken = default)
    {
        // Use hardcoded system identity — no ICurrentUserService, no HTTP context.
        // IpAddress and CorrelationId are intentionally null for background-job entries.
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var entry = new AuditLog
            {
                EntityName    = entityName,
                EntityID      = entityId,
                Action        = action,
                OldValues     = oldValues,
                NewValues     = newValues,
                UserID        = Data.DbSeeder.SystemUserId,
                UserName      = "SYSTEM",
                IpAddress     = null,
                CorrelationId = null,
                OccurredAt    = _clock.UtcNow
            };

            db.AuditLogs.Add(entry);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write SYSTEM audit log entry. Entity={EntityName} ID={EntityId} Action={Action}",
                entityName, entityId, action);
        }
    }
}
