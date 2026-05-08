using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Prior_Authorization_Workflow_Tracker.Components;
using Prior_Authorization_Workflow_Tracker.Services;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;
using Prior_Authorization_Workflow_Tracker.Services.Demo;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// ── Demo: clock ───────────────────────────────────────────────────────────────
builder.Services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

// ── Demo: role-switcher state + auth ─────────────────────────────────────────
builder.Services.AddSingleton<DemoSessionState>();
builder.Services.AddScoped<AuthenticationStateProvider, DemoAuthStateProvider>();
builder.Services.AddScoped<ICurrentUserService, DemoCurrentUserService>();
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

// ── Demo: in-memory data store (singleton so all services share the same data)
builder.Services.AddSingleton<DemoDataStore>();

// ── Demo: service implementations ────────────────────────────────────────────
builder.Services.AddScoped<IAuditService, DemoAuditService>();
builder.Services.AddScoped<IBusinessRuleService, DemoBusinessRuleService>();
builder.Services.AddScoped<INotificationService, DemoNotificationService>();
builder.Services.AddScoped<IWorkflowService, DemoWorkflowService>();
builder.Services.AddScoped<IPaRequestService, DemoPaRequestService>();
builder.Services.AddScoped<IUserManagementService, DemoUserManagementService>();
builder.Services.AddScoped<IReportingService, DemoReportingService>();
builder.Services.AddScoped<ICsvExportService, DemoCsvExportService>();
builder.Services.AddScoped<IExpirationJobTrigger, DemoExpirationJobTrigger>();
builder.Services.AddScoped<ToastService>();

await builder.Build().RunAsync();

