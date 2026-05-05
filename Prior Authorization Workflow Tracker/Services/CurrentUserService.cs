using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Scoped Blazor Server implementation of ICurrentUserService.
/// Reads user identity from AuthenticationStateProvider and loads
/// the ApplicationUser to check IsActive (BUG-012 mitigation).
///
/// IpAddress is captured once from IHttpContextAccessor during the initial
/// HTTP request before the Blazor circuit upgrades to WebSocket (BLIND-003 mitigation).
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Lazily resolved and cached within the circuit scope
    private string? _userId;
    private string? _userName;
    private bool? _isActive;
    private string? _ipAddress;
    private System.Security.Claims.ClaimsPrincipal? _user;

    public CurrentUserService(
        AuthenticationStateProvider authStateProvider,
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _authStateProvider = authStateProvider;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;

        // Capture IP from the initial HTTP context (unavailable later over SignalR)
        _ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    }

    public string UserId => _userId ??= GetClaimValue(System.Security.Claims.ClaimTypes.NameIdentifier) ?? string.Empty;

    public string UserName => _userName ??= GetClaimValue(System.Security.Claims.ClaimTypes.Name) ?? string.Empty;

    public bool IsInRole(string role) => GetUser()?.IsInRole(role) ?? false;

    public bool IsActive
    {
        get
        {
            if (_isActive.HasValue) return _isActive.Value;
            // Synchronous lookup acceptable here — called infrequently per circuit
            var user = _userManager.FindByIdAsync(UserId).GetAwaiter().GetResult();
            _isActive = user?.IsActive ?? false;
            return _isActive.Value;
        }
    }

    public string? IpAddress => _ipAddress;

    // ── helpers ───────────────────────────────────────────────────────────────

    private System.Security.Claims.ClaimsPrincipal? GetUser()
    {
        if (_user is not null) return _user;

        // When called from a Razor Page (e.g. Login/Logout), IHttpContextAccessor has
        // the authenticated principal and ServerAuthenticationStateProvider is not available.
        // Fall back to HttpContext.User to avoid InvalidOperationException (BLIND-003).
        var httpUser = _httpContextAccessor.HttpContext?.User;
        if (httpUser?.Identity?.IsAuthenticated == true)
        {
            _user = httpUser;
            return _user;
        }

        try
        {
            var authState = _authStateProvider.GetAuthenticationStateAsync().GetAwaiter().GetResult();
            _user = authState.User;
        }
        catch (InvalidOperationException)
        {
            // Outside a Blazor circuit and no authenticated HTTP context — return null.
            _user = httpUser;
        }

        return _user;
    }

    private string? GetClaimValue(string claimType)
        => GetUser()?.FindFirst(claimType)?.Value;
}
