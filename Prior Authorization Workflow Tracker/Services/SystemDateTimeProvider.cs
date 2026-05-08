using Prior_Authorization_Workflow_Tracker.Services.Abstractions;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>Production clock implementation — returns DateTime.UtcNow.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
