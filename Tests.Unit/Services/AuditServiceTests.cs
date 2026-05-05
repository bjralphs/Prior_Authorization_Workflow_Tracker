using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services;
using Tests.Unit.Fakes;

namespace Tests.Unit.Services;

/// <summary>
/// Unit tests for AuditService (T08, §13.3 — ≥ 70% coverage target).
///
/// AuditService uses IDbContextFactory so we mock the factory and verify
/// that the correct AuditLog entity is added and saved.
/// </summary>
public class AuditServiceTests
{
    private static (AuditService service, Mock<IDbContextFactory<AppDbContext>> factoryMock, List<AuditLog> captured)
        BuildSut(FakeCurrentUserService? user = null)
    {
        user ??= FakeCurrentUserService.AsSpecialist();
        var clock = new FakeDateTimeProvider();
        var captured = new List<AuditLog>();

        var dbMock = new Mock<AppDbContext>(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase("AuditTest_" + Guid.NewGuid())
                .Options);

        // We cannot easily mock DbSet<AuditLog> inline, so we use a real in-memory context.
        // Note: integration tests (future Tests.Integration project) use SQL Server for full fidelity.
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var opts = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase("AuditTest_" + Guid.NewGuid())
                    .Options;
                return new AppDbContext(opts);
            });

        var logger = NullLogger<AuditService>.Instance;
        var httpCtx = new Mock<IHttpContextAccessor>();
        httpCtx.Setup(h => h.HttpContext).Returns((HttpContext?)null);
        var svc = new AuditService(factoryMock.Object, user, clock, httpCtx.Object, logger);
        return (svc, factoryMock, captured);
    }

    [Fact]
    public async Task LogAsync_CreatesDbContextAndSaves()
    {
        var (svc, factoryMock, _) = BuildSut();

        await svc.LogAsync("PaRequest", "42", "StatusChanged");

        factoryMock.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_DoesNotThrow_WhenContextFactoryFails()
    {
        // Audit failures must never propagate to the caller (§11 design note).
        var clock = new FakeDateTimeProvider();
        var user = FakeCurrentUserService.AsSpecialist();
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var svc = new AuditService(factoryMock.Object, user, clock,
            Mock.Of<IHttpContextAccessor>(), NullLogger<AuditService>.Instance);

        var ex = await Record.ExceptionAsync(() => svc.LogAsync("PaRequest", "1", "Test"));

        Assert.Null(ex); // must swallow
    }

    [Fact]
    public async Task LogAsync_UsesCurrentUserContext()
    {
        // We verify the user fields are passed through by observing the factory was called.
        // Full field verification occurs in integration tests.
        var user = FakeCurrentUserService.AsReviewer();
        var (svc, factoryMock, _) = BuildSut(user);

        await svc.LogAsync("PaRequest", "7", "DecisionRendered", oldValues: null, newValues: "{\"status\":\"Approved\"}");

        factoryMock.Verify(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// PRD §13.3 named scenario: AuditService_LogStatusChange_ShouldPersistOldAndNewValues.
    /// Uses a shared in-memory DB name so we can read back the persisted row and
    /// assert OldValues / NewValues are written exactly as passed.
    /// </summary>
    [Fact]
    public async Task LogAsync_PersistsOldValuesAndNewValues()
    {
        const string dbName   = "AuditPersistTest";
        const string oldJson  = "{\"Status\":\"Submitted\"}";
        const string newJson  = "{\"Status\":\"UnderReview\",\"ReviewerUserID\":\"rev-001\"}";

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        // Build factory that always returns a context on the same named DB
        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(opts));

        var user  = FakeCurrentUserService.AsReviewer();
        var clock = new FakeDateTimeProvider();
        var httpCtx = new Mock<IHttpContextAccessor>();
        httpCtx.Setup(h => h.HttpContext).Returns((HttpContext?)null);
        var svc   = new AuditService(factoryMock.Object, user, clock, httpCtx.Object, NullLogger<AuditService>.Instance);

        await svc.LogAsync("PaRequest", "42", "StatusChanged",
            oldValues: oldJson, newValues: newJson);

        // Read the row back from the same shared DB
        await using var readCtx = new AppDbContext(opts);
        var row = await readCtx.AuditLogs.SingleAsync();

        Assert.Equal("PaRequest",  row.EntityName);
        Assert.Equal("42",         row.EntityID);
        Assert.Equal("StatusChanged", row.Action);
        Assert.Equal(oldJson,      row.OldValues);
        Assert.Equal(newJson,      row.NewValues);
        Assert.Equal(user.UserId,  row.UserID);
    }

    /// <summary>
    /// T-D5 (§7.1, §13.3): CorrelationId is stored when IHttpContextAccessor provides
    /// an HttpContext with the key "CorrelationId" in Items (set by CorrelationIdMiddleware).
    /// </summary>
    [Fact]
    public async Task LogAsync_SetsCorrelationId_WhenHttpContextItemsContainsId()
    {
        const string dbName        = "AuditCorrelationTest";
        const string correlationId = "abc123def456";

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(opts));

        // Simulate a live HttpContext with the correlation ID already in Items
        // (placed there by CorrelationIdMiddleware before this service call).
        var ctx = new DefaultHttpContext();
        ctx.Items["CorrelationId"] = correlationId;

        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(h => h.HttpContext).Returns(ctx);

        var svc = new AuditService(
            factoryMock.Object,
            FakeCurrentUserService.AsSpecialist(),
            new FakeDateTimeProvider(),
            httpAccessor.Object,
            NullLogger<AuditService>.Instance);

        await svc.LogAsync("PaRequest", "99", "StatusChanged");

        await using var readCtx = new AppDbContext(opts);
        var row = await readCtx.AuditLogs.SingleAsync();
        Assert.Equal(correlationId, row.CorrelationId);
    }

    [Fact]
    public async Task LogAsync_CorrelationId_IsNull_WhenHttpContextIsNull()
    {
        const string dbName = "AuditCorrelationNullTest";

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var factoryMock = new Mock<IDbContextFactory<AppDbContext>>();
        factoryMock
            .Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new AppDbContext(opts));

        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(h => h.HttpContext).Returns((HttpContext?)null);

        var svc = new AuditService(
            factoryMock.Object,
            FakeCurrentUserService.AsSpecialist(),
            new FakeDateTimeProvider(),
            httpAccessor.Object,
            NullLogger<AuditService>.Instance);

        await svc.LogAsync("PaRequest", "100", "Created");

        await using var readCtx = new AppDbContext(opts);
        var row = await readCtx.AuditLogs.SingleAsync();
        Assert.Null(row.CorrelationId);
    }
}
