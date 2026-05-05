using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Prior_Authorization_Workflow_Tracker.Components;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Middleware;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;
using Serilog;
// ── Serilog bootstrap logger ──────────────────────────────────────────────────
// Captures startup errors before the full host is built.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog (replaces default Microsoft logging) ───────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) =>
    {
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .Enrich.WithMachineName()
           .Enrich.WithThreadId();
    });

    // ── Entity Framework Core + SQL Server ────────────────────────────────────
    builder.Services.AddDbContextFactory<AppDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            sql => sql.EnableRetryOnFailure(maxRetryCount: 3)));

    // ── ASP.NET Core Identity ─────────────────────────────────────────────────
    builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Password policy — PRD §15 NFR-003 specifies PBKDF2-SHA256 (Identity default)
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireDigit = true;

        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.MaxFailedAccessAttempts = 5;

        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

    // ── Authentication / Authorization ────────────────────────────────────────
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();

    // ── HttpContextAccessor — required for ICurrentUserService (BLIND-003) ────
    builder.Services.AddHttpContextAccessor();

    // ── Application services ──────────────────────────────────────────────────
    builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
    builder.Services.AddScoped<IAuditService, AuditService>();
    builder.Services.AddScoped<IBusinessRuleService, BusinessRuleService>();
    builder.Services.AddScoped<INotificationService, NotificationService>();
    builder.Services.AddScoped<IWorkflowService, WorkflowService>();
    builder.Services.AddScoped<IPaRequestService, PaRequestService>();
    builder.Services.AddScoped<IUserManagementService, UserManagementService>();
    builder.Services.AddScoped<IReportingService, ReportingService>();
    builder.Services.AddScoped<ICsvExportService, CsvExportService>();
    builder.Services.AddScoped<ToastService>(); // Scoped: each Blazor circuit gets its own queue (GAP-13)
    // Register ExpirationJob as singleton so IExpirationJobTrigger can be resolved
    // by the Admin UI without needing to reference the BackgroundService type directly (PRD §8.8).
    builder.Services.AddSingleton<ExpirationJob>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ExpirationJob>());
    builder.Services.AddSingleton<IExpirationJobTrigger>(sp => sp.GetRequiredService<ExpirationJob>());
    builder.Services.AddScoped<DbSeeder>();

    // ── Razor Pages (required for Identity login/logout pages) ────────────────
    builder.Services.AddRazorPages();

    // ── Razor Components (Blazor Server) ─────────────────────────────────────
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    var app = builder.Build();

    // ── Apply EF Core migrations (all environments, NFR-010) ──────────────────
    using (var migrationScope = app.Services.CreateScope())
    {
        var db = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    // ── Seed demo data (Development only) ────────────────────────────────────
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        await seeder.SeedAsync();
    }

    // ── HTTP pipeline ─────────────────────────────────────────────────────────
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapRazorPages();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}

