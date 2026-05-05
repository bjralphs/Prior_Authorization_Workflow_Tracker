namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Contract for the append-only audit trail service (§11).
/// All state-changing operations in every other service inject and call this.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Writes a single audit entry attributed to the currently authenticated user.
    /// Must not throw — failures are caught and logged to Serilog so that a failed
    /// audit write does not roll back the business operation.
    /// </summary>
    Task LogAsync(
        string entityName,
        string entityId,
        string action,
        string? oldValues = null,
        string? newValues = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a single audit entry attributed to the SYSTEM user (§11).
    /// Use for background-job operations (e.g. ExpirationJob) that run without
    /// an active HTTP/Blazor context and therefore have no authenticated user.
    /// Must not throw — failures are caught and logged to Serilog.
    /// </summary>
    Task LogAsSystemAsync(
        string entityName,
        string entityId,
        string action,
        string? oldValues = null,
        string? newValues = null,
        CancellationToken cancellationToken = default);
}
