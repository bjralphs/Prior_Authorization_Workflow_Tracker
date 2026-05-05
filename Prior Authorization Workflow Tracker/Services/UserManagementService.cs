using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prior_Authorization_Workflow_Tracker.Constants;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// User account management for the Admin role (FR-008, §8.8).
/// </summary>
public sealed class UserManagementService : IUserManagementService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditService _audit;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IDbContextFactory<AppDbContext> dbFactory,
        ICurrentUserService currentUser,
        IAuditService audit,
        ILogger<UserManagementService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _dbFactory   = dbFactory;
        _currentUser = currentUser;
        _audit       = audit;
        _logger      = logger;
    }

    public async Task<IReadOnlyList<(ApplicationUser User, IList<string> Roles)>> GetAllUsersAsync(
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Exclude the reserved SYSTEM account from admin listings
        var users = await db.Users
            .AsNoTracking()
            .Where(u => u.Id != DbSeeder.SystemUserId)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        // Batch-load all role assignments in a single join query instead of
        // calling GetRolesAsync per user (O(1) queries vs O(N)) — REF-01 fix.
        var userIds = users.Select(u => u.Id).ToHashSet();
        var roleAssignments = await db.UserRoles
            .AsNoTracking()
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct);
        var rolesByUser = roleAssignments
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToList());

        return users
            .Select(u => (u, (IList<string>)rolesByUser.GetValueOrDefault(u.Id, [])))
            .ToList();
    }

    public async Task<ApplicationUser?> GetByIdAsync(string userId, CancellationToken ct = default)
    {
        return await _userManager.FindByIdAsync(userId);
    }

    public async Task<(ApplicationUser User, string TemporaryPassword)> CreateUserAsync(
        string email,
        string fullName,
        string role,
        string? department,
        CancellationToken ct = default)
    {
        RequireAdmin();

        // Ensure the role exists before proceeding
        if (!await _roleManager.RoleExistsAsync(role))
            throw new BusinessRuleViolationException("USER-001",
                $"Role '{role}' does not exist. Valid roles: {string.Join(", ", new[] { Roles.Specialist, Roles.Provider, Roles.BillingManager, Roles.Reviewer, Roles.Admin })}.");

        var tempPassword = GenerateTemporaryPassword();
        var user = new ApplicationUser
        {
            UserName   = email,
            Email      = email,
            FullName   = fullName,
            Department = department,
            IsActive   = true,
            CreatedAt  = DateTime.UtcNow,
        };

        var createResult = await _userManager.CreateAsync(user, tempPassword);
        if (!createResult.Succeeded)
        {
            var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
            throw new BusinessRuleViolationException("USER-002", $"Failed to create user: {errors}");
        }

        var roleResult = await _userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            // Best-effort: clean up the created user if role assignment fails
            await _userManager.DeleteAsync(user);
            var errors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
            throw new BusinessRuleViolationException("USER-003", $"Failed to assign role: {errors}");
        }

        await _audit.LogAsync("User", user.Id, "Created",
            newValues: $"{{\"Email\":\"{email}\",\"Role\":\"{role}\"}}",
            cancellationToken: ct);

        return (user, tempPassword);
    }

    public async Task DeactivateUserAsync(string userId, CancellationToken ct = default)
    {
        RequireAdmin();

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new EntityNotFoundException("User", userId);

        if (userId == DbSeeder.SystemUserId)
            throw new BusinessRuleViolationException("USER-004",
                "The SYSTEM reserved account cannot be deactivated.");

        if (userId == _currentUser.UserId)
            throw new BusinessRuleViolationException("USER-005",
                "Administrators cannot deactivate their own account.");

        user.IsActive = false;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new BusinessRuleViolationException("USER-006", $"Failed to deactivate user: {errors}");
        }

        await _audit.LogAsync("User", userId, "Deactivated",
            oldValues: "{\"IsActive\":true}",
            newValues: "{\"IsActive\":false}",
            cancellationToken: ct);
    }

    public async Task ReactivateUserAsync(string userId, CancellationToken ct = default)
    {
        RequireAdmin();

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new EntityNotFoundException("User", userId);

        user.IsActive = true;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new BusinessRuleViolationException("USER-007", $"Failed to reactivate user: {errors}");
        }

        await _audit.LogAsync("User", userId, "Reactivated",
            oldValues: "{\"IsActive\":false}",
            newValues: "{\"IsActive\":true}",
            cancellationToken: ct);
    }

    public async Task ChangeRoleAsync(string userId, string newRole, CancellationToken ct = default)
    {
        RequireAdmin();

        if (!await _roleManager.RoleExistsAsync(newRole))
            throw new BusinessRuleViolationException("USER-008",
                $"Role '{newRole}' does not exist.");

        var user = await _userManager.FindByIdAsync(userId)
            ?? throw new EntityNotFoundException("User", userId);

        var currentRoles = await _userManager.GetRolesAsync(user);
        var oldRole = currentRoles.FirstOrDefault() ?? "(none)";

        if (currentRoles.Count > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                var errors = string.Join("; ", removeResult.Errors.Select(e => e.Description));
                throw new BusinessRuleViolationException("USER-009", $"Failed to remove current roles: {errors}");
            }
        }

        var addResult = await _userManager.AddToRoleAsync(user, newRole);
        if (!addResult.Succeeded)
        {
            var errors = string.Join("; ", addResult.Errors.Select(e => e.Description));
            throw new BusinessRuleViolationException("USER-010", $"Failed to assign role '{newRole}': {errors}");
        }

        await _audit.LogAsync("User", userId, "RoleChanged",
            oldValues: $"{{\"Role\":\"{oldRole}\"}}",
            newValues: $"{{\"Role\":\"{newRole}\"}}",
            cancellationToken: ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RequireAdmin()
    {
        if (!_currentUser.IsActive)
            throw new AuthorizationException(
                $"User account '{_currentUser.UserName}' is inactive.");

        if (!_currentUser.IsInRole(Roles.Admin))
            throw new AuthorizationException(
                $"User '{_currentUser.UserName}' must be an Administrator to manage user accounts.");
    }

    /// <summary>
    /// Generates a temporary password that satisfies the Identity password policy (§15 NFR-003):
    /// ≥ 8 chars, uppercase, digit, special character.
    /// </summary>
    private static string GenerateTemporaryPassword()
    {
        var suffix = Random.Shared.Next(1000, 9999);
        return $"Temp@{suffix}!";
    }
}
