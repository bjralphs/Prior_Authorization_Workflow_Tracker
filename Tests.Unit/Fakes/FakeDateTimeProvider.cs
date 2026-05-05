using Prior_Authorization_Workflow_Tracker.Services.Abstractions;

namespace Tests.Unit.Fakes;

/// <summary>
/// Deterministic clock for unit tests.
/// Inject via IDateTimeProvider to control time-sensitive business rules
/// (BUG-007, BR-008, BR-011, expiration logic).
/// </summary>
public sealed class FakeDateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc);

    public FakeDateTimeProvider() { }

    public FakeDateTimeProvider(DateTime fixedNow)
    {
        UtcNow = fixedNow;
    }
}
