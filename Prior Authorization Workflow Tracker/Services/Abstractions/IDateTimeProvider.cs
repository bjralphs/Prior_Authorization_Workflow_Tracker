namespace Prior_Authorization_Workflow_Tracker.Services.Abstractions;

/// <summary>
/// Abstracts the system clock to enable deterministic testing of expiry logic (§13.4, BUG-007).
/// Inject this wherever DateTime.UtcNow would otherwise be used in service code.
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>Current UTC time. Returns DateTime.UtcNow in production; a fixed value in tests.</summary>
    DateTime UtcNow { get; }
}
