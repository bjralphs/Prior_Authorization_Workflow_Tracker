namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Allows Admin users to manually invoke the expiration check on demand (PRD §8.8).
/// Implemented by ExpirationJob so it can be injected into Blazor pages without
/// directly referencing the BackgroundService type.
/// </summary>
public interface IExpirationJobTrigger
{
    /// <summary>
    /// Runs the expiration check synchronously and returns the count of requests expired.
    /// </summary>
    Task<int> TriggerAsync(CancellationToken ct = default);
}
