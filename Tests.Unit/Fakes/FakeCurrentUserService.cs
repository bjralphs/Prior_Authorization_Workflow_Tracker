using Prior_Authorization_Workflow_Tracker.Services.Abstractions;

namespace Tests.Unit.Fakes;

/// <summary>
/// Fake ICurrentUserService for unit tests.
/// Defaults to a Specialist; override Role/UserId per test as needed.
/// </summary>
public sealed class FakeCurrentUserService : ICurrentUserService
{
    public string UserId { get; set; } = "test-specialist-id";
    public string UserName { get; set; } = "specialist@pademo.com";
    public bool IsActive { get; set; } = true;
    public string? IpAddress { get; set; } = "127.0.0.1";

    private readonly HashSet<string> _roles = new(StringComparer.OrdinalIgnoreCase);

    public FakeCurrentUserService(params string[] roles)
    {
        foreach (var r in roles) _roles.Add(r);
    }

    public bool IsInRole(string role) => _roles.Contains(role);

    public static FakeCurrentUserService AsSpecialist() =>
        new("Authorization Specialist") { UserId = "specialist-id", UserName = "specialist@pademo.com" };

    public static FakeCurrentUserService AsReviewer() =>
        new("Payer Reviewer") { UserId = "reviewer-id", UserName = "reviewer@pademo.com" };

    public static FakeCurrentUserService AsAdmin() =>
        new("Administrator") { UserId = "admin-id", UserName = "admin@pademo.com" };

    public static FakeCurrentUserService AsProvider() =>
        new("Treating Provider") { UserId = "provider-id", UserName = "provider@pademo.com" };

    public static FakeCurrentUserService AsBillingManager() =>
        new("Billing Manager") { UserId = "billing-id", UserName = "billing@pademo.com" };
}
