using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Prior_Authorization_Workflow_Tracker.Constants;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services;
using Tests.Unit.Fakes;

namespace Tests.Unit.Services;

/// <summary>
/// Unit tests for UserManagementService (T-D3, §8.8, §13).
/// Uses mocked UserManager / RoleManager plus an in-memory AppDbContext.
/// </summary>
public class UserManagementServiceTests
{
    // ── Builder helpers ───────────────────────────────────────────────────────

    private static Mock<UserManager<ApplicationUser>> BuildUserManager(
        IUserStore<ApplicationUser>? store = null)
    {
        store ??= new Mock<IUserStore<ApplicationUser>>().Object;
        return new Mock<UserManager<ApplicationUser>>(
            store, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static Mock<RoleManager<IdentityRole>> BuildRoleManager()
    {
        var store = new Mock<IRoleStore<IdentityRole>>();
        return new Mock<RoleManager<IdentityRole>>(
            store.Object, null!, null!, null!, null!);
    }

    private static AppDbContext MakeDb(string name)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AppDbContext(opts);
    }

    private static UserManagementService Build(
        string dbName,
        FakeCurrentUserService? user = null,
        Mock<UserManager<ApplicationUser>>? userMgr = null,
        Mock<RoleManager<IdentityRole>>? roleMgr = null,
        Mock<IAuditService>? audit = null)
    {
        user    ??= FakeCurrentUserService.AsAdmin();
        userMgr ??= BuildUserManager();
        roleMgr ??= BuildRoleManager();
        audit   ??= new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => MakeDb(dbName));

        return new UserManagementService(
            userMgr.Object, roleMgr.Object, factory.Object,
            user, audit.Object, NullLogger<UserManagementService>.Instance);
    }

    // ── GetAllUsersAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllUsersAsync_ExcludesSystemUser()
    {
        const string dbName = "GetAllUsers_ExcludeSystem";
        await using (var db = MakeDb(dbName))
        {
            db.Users.AddRange(
                new ApplicationUser { Id = DbSeeder.SystemUserId, UserName = "system@pademo.internal", FullName = "SYSTEM", IsActive = true, CreatedAt = DateTime.UtcNow },
                new ApplicationUser { Id = "user-1", UserName = "alice@pademo.com", Email = "alice@pademo.com", FullName = "Alice Smith", IsActive = true, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var userMgr = BuildUserManager();
        userMgr.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>()))
               .ReturnsAsync(new List<string> { Roles.Specialist });

        var svc = Build(dbName, userMgr: userMgr);

        var result = await svc.GetAllUsersAsync();

        Assert.Single(result);
        Assert.Equal("user-1", result[0].User.Id);
        Assert.DoesNotContain(result, r => r.User.Id == DbSeeder.SystemUserId);
    }

    // ── CreateUserAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateUserAsync_NonAdmin_ThrowsAuthorizationException()
    {
        var specialist = FakeCurrentUserService.AsSpecialist();
        var svc = Build("create_non_admin", user: specialist);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.CreateUserAsync("new@test.com", "New User", Roles.Specialist, null));
    }

    [Fact]
    public async Task CreateUserAsync_InvalidRole_ThrowsBusinessRuleViolationException()
    {
        var roleMgr = BuildRoleManager();
        roleMgr.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var svc = Build("create_invalid_role", roleMgr: roleMgr);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.CreateUserAsync("new@test.com", "New User", "NonExistentRole", null));
    }

    [Fact]
    public async Task CreateUserAsync_ValidInput_ReturnsUserAndPassword()
    {
        var roleMgr = BuildRoleManager();
        roleMgr.Setup(r => r.RoleExistsAsync(Roles.Specialist)).ReturnsAsync(true);

        var userMgr = BuildUserManager();
        userMgr.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
               .ReturnsAsync(IdentityResult.Success);
        userMgr.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
               .ReturnsAsync(IdentityResult.Success);

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = Build("create_valid", userMgr: userMgr, roleMgr: roleMgr, audit: audit);

        var (user, password) = await svc.CreateUserAsync("new@test.com", "New User", Roles.Specialist, "Billing");

        Assert.Equal("new@test.com", user.Email);
        Assert.Equal("New User", user.FullName);
        Assert.NotEmpty(password);
        // Verify audit event was written
        audit.Verify(a => a.LogAsync("User", It.IsAny<string>(), "Created",
            null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── DeactivateUserAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateUserAsync_NonAdmin_ThrowsAuthorizationException()
    {
        var specialist = FakeCurrentUserService.AsSpecialist();
        var svc = Build("deactivate_non_admin", user: specialist);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.DeactivateUserAsync("some-user-id"));
    }

    [Fact]
    public async Task DeactivateUserAsync_SystemUser_ThrowsBusinessRuleViolation()
    {
        var userMgr = BuildUserManager();
        var user = new ApplicationUser { Id = DbSeeder.SystemUserId, UserName = "system@pademo.internal", IsActive = true };
        userMgr.Setup(u => u.FindByIdAsync(DbSeeder.SystemUserId)).ReturnsAsync(user);

        var svc = Build("deactivate_system", userMgr: userMgr);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.DeactivateUserAsync(DbSeeder.SystemUserId));

        Assert.Equal("USER-004", ex.RuleId);
    }

    [Fact]
    public async Task DeactivateUserAsync_CannotDeactivateSelf()
    {
        var admin = FakeCurrentUserService.AsAdmin(); // UserId = "admin-id"
        var userMgr = BuildUserManager();
        var target = new ApplicationUser { Id = "admin-id", UserName = "admin@pademo.com", IsActive = true };
        userMgr.Setup(u => u.FindByIdAsync("admin-id")).ReturnsAsync(target);

        var svc = Build("deactivate_self", user: admin, userMgr: userMgr);

        var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.DeactivateUserAsync("admin-id"));

        Assert.Equal("USER-005", ex.RuleId);
    }

    [Fact]
    public async Task DeactivateUserAsync_HappyPath_SetsIsActiveFalse()
    {
        var admin = FakeCurrentUserService.AsAdmin(); // UserId = "admin-id"
        var userMgr = BuildUserManager();
        var target = new ApplicationUser { Id = "other-user", UserName = "other@pademo.com", IsActive = true };
        userMgr.Setup(u => u.FindByIdAsync("other-user")).ReturnsAsync(target);
        userMgr.Setup(u => u.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = Build("deactivate_happy", user: admin, userMgr: userMgr, audit: audit);

        await svc.DeactivateUserAsync("other-user");

        Assert.False(target.IsActive);
        audit.Verify(a => a.LogAsync("User", "other-user", "Deactivated",
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ChangeRoleAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeRoleAsync_HappyPath_WritesAuditEntry()
    {
        var admin = FakeCurrentUserService.AsAdmin();
        var roleMgr = BuildRoleManager();
        roleMgr.Setup(r => r.RoleExistsAsync(Roles.BillingManager)).ReturnsAsync(true);

        var userMgr = BuildUserManager();
        var target = new ApplicationUser { Id = "user-42", UserName = "user@pademo.com" };
        userMgr.Setup(u => u.FindByIdAsync("user-42")).ReturnsAsync(target);
        userMgr.Setup(u => u.GetRolesAsync(target))
               .ReturnsAsync(new List<string> { Roles.Specialist });
        userMgr.Setup(u => u.RemoveFromRolesAsync(target, It.IsAny<IEnumerable<string>>()))
               .ReturnsAsync(IdentityResult.Success);
        userMgr.Setup(u => u.AddToRoleAsync(target, Roles.BillingManager))
               .ReturnsAsync(IdentityResult.Success);

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = Build("change_role_happy", user: admin, userMgr: userMgr, roleMgr: roleMgr, audit: audit);

        await svc.ChangeRoleAsync("user-42", Roles.BillingManager);

        audit.Verify(a => a.LogAsync("User", "user-42", "RoleChanged",
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeRoleAsync_InvalidRole_ThrowsBusinessRuleViolation()
    {
        var roleMgr = BuildRoleManager();
        roleMgr.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        var svc = Build("change_role_invalid", roleMgr: roleMgr);

        await Assert.ThrowsAsync<BusinessRuleViolationException>(
            () => svc.ChangeRoleAsync("user-1", "FakeRole"));
    }

    // ── ReactivateUserAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ReactivateUserAsync_NonAdmin_ThrowsAuthorizationException()
    {
        var specialist = FakeCurrentUserService.AsSpecialist();
        var svc = Build("reactivate_non_admin", user: specialist);

        await Assert.ThrowsAsync<AuthorizationException>(
            () => svc.ReactivateUserAsync("some-user-id"));
    }

    [Fact]
    public async Task ReactivateUserAsync_HappyPath_SetsIsActiveTrue()
    {
        var admin = FakeCurrentUserService.AsAdmin();
        var userMgr = BuildUserManager();
        var target = new ApplicationUser { Id = "inactive-user", UserName = "inactive@pademo.com", IsActive = false };
        userMgr.Setup(u => u.FindByIdAsync("inactive-user")).ReturnsAsync(target);
        userMgr.Setup(u => u.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var svc = Build("reactivate_happy", user: admin, userMgr: userMgr, audit: audit);

        await svc.ReactivateUserAsync("inactive-user");

        Assert.True(target.IsActive);
        audit.Verify(a => a.LogAsync("User", "inactive-user", "Reactivated",
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReactivateUserAsync_UnknownUser_ThrowsEntityNotFoundException()
    {
        var admin = FakeCurrentUserService.AsAdmin();
        var userMgr = BuildUserManager();
        userMgr.Setup(u => u.FindByIdAsync("ghost-user"))
               .ReturnsAsync((ApplicationUser?)null);

        var svc = Build("reactivate_not_found", user: admin, userMgr: userMgr);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => svc.ReactivateUserAsync("ghost-user"));
    }
}
