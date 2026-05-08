using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>
/// Read-only view of demo users.
/// Mutating operations (create/deactivate/role change) are supported
/// with in-memory changes so the Admin panel is interactive in the demo.
/// </summary>
public sealed class DemoUserManagementService : IUserManagementService
{
    private readonly DemoDataStore _store;
    private readonly IAuditService _audit;

    public DemoUserManagementService(DemoDataStore store, IAuditService audit)
    {
        _store = store;
        _audit = audit;
    }

    public Task<IReadOnlyList<(ApplicationUser User, IList<string> Roles)>> GetAllUsersAsync(CancellationToken ct = default)
    {
        IReadOnlyList<(ApplicationUser, IList<string>)> result = _store.Users
            .Select(u => (ToAppUser(u), (IList<string>)[u.Role]))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<ApplicationUser?> GetByIdAsync(string userId, CancellationToken ct = default)
    {
        var u = _store.Users.FirstOrDefault(u => u.Id == userId);
        return Task.FromResult(u == null ? null : ToAppUser(u));
    }

    public async Task<(ApplicationUser User, string TemporaryPassword)> CreateUserAsync(
        string email, string fullName, string role, string? department, CancellationToken ct = default)
    {
        var id = $"user-new-{_store.NextAuditId()}";
        var user = new DemoUser
        {
            Id         = id,
            FullName   = fullName,
            Email      = email,
            Role       = role,
            Department = department,
            IsActive   = true,
        };
        // DemoUser list is read-only; we work around by casting
        ((List<DemoUser>)_store.Users).Add(user);

        await _audit.LogAsync("ApplicationUser", id, "Created", null, null, ct);
        return (ToAppUser(user), "Demo@1234");
    }

    public async Task DeactivateUserAsync(string userId, CancellationToken ct = default)
    {
        var u = _store.Users.FirstOrDefault(u => u.Id == userId);
        if (u != null) u.IsActive = false;
        await _audit.LogAsync("ApplicationUser", userId, "Deactivated", null, null, ct);
    }

    public async Task ReactivateUserAsync(string userId, CancellationToken ct = default)
    {
        var u = _store.Users.FirstOrDefault(u => u.Id == userId);
        if (u != null) u.IsActive = true;
        await _audit.LogAsync("ApplicationUser", userId, "Reactivated", null, null, ct);
    }

    public async Task ChangeRoleAsync(string userId, string newRole, CancellationToken ct = default)
    {
        var u = _store.Users.FirstOrDefault(u => u.Id == userId);
        if (u != null) u.Role = newRole;
        await _audit.LogAsync("ApplicationUser", userId, "RoleChanged", null, newRole, ct);
    }

    private static ApplicationUser ToAppUser(DemoUser u) => new()
    {
        Id         = u.Id,
        UserName   = u.Email,
        Email      = u.Email,
        FullName   = u.FullName,
        Department = u.Department,
        IsActive   = u.IsActive,
    };
}
