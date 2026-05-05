using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Manages user accounts (FR-008, §8.8).
/// Wraps ASP.NET Core Identity UserManager/RoleManager with audit logging.
/// Only Administrators may call mutating methods on this service.
/// </summary>
public interface IUserManagementService
{
    /// <summary>Returns all users with their current roles. Uses AsNoTracking read.</summary>
    Task<IReadOnlyList<(ApplicationUser User, IList<string> Roles)>> GetAllUsersAsync(
        CancellationToken ct = default);

    /// <summary>Returns a single user by ID, or null when not found.</summary>
    Task<ApplicationUser?> GetByIdAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new user account, assigns the specified role, and generates a
    /// temporary password. Writes a "Created" audit entry.
    /// </summary>
    Task<(ApplicationUser User, string TemporaryPassword)> CreateUserAsync(
        string email,
        string fullName,
        string role,
        string? department,
        CancellationToken ct = default);

    /// <summary>
    /// Sets IsActive = false on the user account. Writes a "Deactivated" audit entry.
    /// Does not delete the Identity record (preserves FK references in AuditLogs).
    /// </summary>
    Task DeactivateUserAsync(string userId, CancellationToken ct = default);

    /// <summary>Sets IsActive = true. Writes a "Reactivated" audit entry.</summary>
    Task ReactivateUserAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Removes all current roles from the user and assigns newRole.
    /// Writes a "RoleChanged" audit entry.
    /// </summary>
    Task ChangeRoleAsync(string userId, string newRole, CancellationToken ct = default);
}
