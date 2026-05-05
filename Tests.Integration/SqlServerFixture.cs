using Microsoft.EntityFrameworkCore;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Models;
using Testcontainers.MsSql;
using Xunit;

namespace Tests.Integration;

/// <summary>
/// xUnit collection fixture that starts a single Testcontainers SQL Server container
/// for all integration test classes and applies all EF Core migrations exactly once.
///
/// Container lifecycle: StartAsync in InitializeAsync, DisposeAsync when the collection ends.
/// Test isolation: each test method seeds unique data using per-invocation GUIDs.
///
/// Shared via <see cref="SqlServerCollection"/> so only one container starts per test run.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    /// <summary>Full SQL Server connection string for the Testcontainers-managed instance.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Apply all three EF Core migrations:
        //   InitialCreate → AddReportingViews (vw_*) → AddAuthorizationNumberUniqueIndex
        await using var ctx = CreateDbContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a new tracked <see cref="AppDbContext"/> connected to the container.</summary>
    public AppDbContext CreateDbContext()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new AppDbContext(opts);
    }

    /// <summary>
    /// Inserts a minimal <see cref="ApplicationUser"/> (AspNetUsers row) required as
    /// the FK target for PaRequest.SubmittedByUserID, ProviderID, and ReviewerUserID.
    /// Saves and commits the user before returning, so subsequent operations in the
    /// same or a different context can reference it.
    /// </summary>
    public async Task SeedUserAsync(AppDbContext ctx, string id, string email)
    {
        ctx.Users.Add(new ApplicationUser
        {
            Id                 = id,
            UserName           = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email              = email,
            NormalizedEmail    = email.ToUpperInvariant(),
            EmailConfirmed     = true,
            SecurityStamp      = Guid.NewGuid().ToString(),
            ConcurrencyStamp   = Guid.NewGuid().ToString(),
            FullName           = email,
            IsActive           = true,
            CreatedAt          = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }
}

/// <summary>
/// xUnit collection definition that maps the "SqlServer" collection name to
/// <see cref="SqlServerFixture"/>. All test classes decorated with
/// <c>[Collection("SqlServer")]</c> share one container instance.
/// </summary>
[CollectionDefinition("SqlServer")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture> { }
