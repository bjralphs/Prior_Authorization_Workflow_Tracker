namespace Prior_Authorization_Workflow_Tracker.Models;

/// <summary>
/// Append-only compliance audit trail (§7.1, §11).
/// Application layer must never issue UPDATE or DELETE against this table.
/// CorrelationId links to the Serilog structured log entry (BLIND-002).
/// IpAddress is captured from a cached connection-time value because HttpContext
/// is unavailable during subsequent SignalR messages (BLIND-003).
/// </summary>
public class AuditLog
{
    public long ID { get; set; }

    /// <summary>e.g. "PaRequest" | "User" | "PaReport"</summary>
    public string EntityName { get; set; } = string.Empty;

    public string EntityID { get; set; } = string.Empty;

    /// <summary>e.g. "StatusChanged" | "DecisionRendered" | "Login" | "FailedLogin"</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>JSON snapshot of changed fields before the operation.</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON snapshot of changed fields after the operation.</summary>
    public string? NewValues { get; set; }

    public string UserID { get; set; } = string.Empty;

    /// <summary>Denormalized for log durability (user may later be deactivated/renamed).</summary>
    public string? UserName { get; set; }

    /// <summary>Cached at circuit connection time (BLIND-003 mitigation).</summary>
    public string? IpAddress { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Links to Serilog CorrelationId enricher value (BLIND-002).</summary>
    public string? CorrelationId { get; set; }
}
