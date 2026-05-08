using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>
/// Custom AuthenticationStateProvider for the demo.
/// Builds a ClaimsPrincipal from the active DemoSessionState user so that
/// all Blazor [Authorize(Roles = "...")] attributes and AuthorizeView components
/// work exactly as they do in the production app.
/// </summary>
public sealed class DemoAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly DemoSessionState _session;
    private readonly DemoDataStore    _store;

    public DemoAuthStateProvider(DemoSessionState session, DemoDataStore store)
    {
        _session = session;
        _store   = store;
        _session.OnChange += OnSessionChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = _store.Users.FirstOrDefault(u => u.Id == _session.CurrentUserId);
        ClaimsPrincipal principal;

        if (user == null)
        {
            principal = new ClaimsPrincipal(new ClaimsIdentity());
        }
        else
        {
            var identity = new ClaimsIdentity(
                authenticationType: "Demo",
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role);

            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id));
            identity.AddClaim(new Claim(ClaimTypes.Name,           user.FullName));
            identity.AddClaim(new Claim(ClaimTypes.Email,          user.Email));
            identity.AddClaim(new Claim(ClaimTypes.Role,           user.Role));

            principal = new ClaimsPrincipal(identity);
        }

        return Task.FromResult(new AuthenticationState(principal));
    }

    private void OnSessionChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose() =>
        _session.OnChange -= OnSessionChanged;
}
