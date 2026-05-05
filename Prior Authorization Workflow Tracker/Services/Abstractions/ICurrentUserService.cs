namespace Prior_Authorization_Workflow_Tracker.Services.Abstractions;

/// <summary>
/// Provides the identity of the currently authenticated user to service-layer classes.
/// Injected as a scoped service; mocked in unit tests to simulate different role contexts (§13.4).
///
/// In Blazor Server, the user identity is available for the lifetime of the circuit
/// via AuthenticationStateProvider. IsActive is checked here in addition to login
/// to prevent deactivated users from acting during a live circuit (BUG-012 mitigation).
/// </summary>
public interface ICurrentUserService
{
    /// <summary>ASP.NET Core Identity UserId (nvarchar 450).</summary>
    string UserId { get; }

    /// <summary>Display name, denormalized into AuditLogs.</summary>
    string UserName { get; }

    /// <summary>True when the user holds the named role.</summary>
    bool IsInRole(string role);

    /// <summary>
    /// True when the Identity user account is active (IsActive = true).
    /// When false, all mutating service operations must throw AuthorizationException.
    /// </summary>
    bool IsActive { get; }

    /// <summary>Client IP address captured at connection time (BLIND-003 mitigation).</summary>
    string? IpAddress { get; }
}
