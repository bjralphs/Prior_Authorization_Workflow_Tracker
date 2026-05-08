using Prior_Authorization_Workflow_Tracker.Services.Abstractions;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>
/// Resolves the "current user" from DemoSessionState rather than HttpContext.
/// </summary>
public sealed class DemoCurrentUserService : ICurrentUserService
{
    private readonly DemoSessionState _session;
    private readonly DemoDataStore    _store;

    public DemoCurrentUserService(DemoSessionState session, DemoDataStore store)
    {
        _session = session;
        _store   = store;
    }

    private DemoUser? ActiveUser =>
        _store.Users.FirstOrDefault(u => u.Id == _session.CurrentUserId);

    public string UserId   => ActiveUser?.Id       ?? string.Empty;
    public string UserName => ActiveUser?.FullName  ?? "Demo User";
    public bool   IsActive => ActiveUser?.IsActive  ?? true;
    public string? IpAddress => "127.0.0.1";

    public bool IsInRole(string role) =>
        string.Equals(ActiveUser?.Role, role, StringComparison.OrdinalIgnoreCase);
}
