namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>
/// Singleton that holds the currently active demo persona.
/// All scoped services read from this to resolve "who is logged in."
/// Raises OnChange whenever the active user is switched so subscribers
/// (e.g. DemoAuthStateProvider) can notify Blazor to re-render.
/// </summary>
public sealed class DemoSessionState
{
    /// <summary>UserId of the currently active demo persona.</summary>
    public string CurrentUserId { get; private set; } = "user-admin-1";

    /// <summary>Raised after CurrentUserId changes.</summary>
    public event Action? OnChange;

    public void SwitchTo(string userId)
    {
        if (CurrentUserId == userId) return;
        CurrentUserId = userId;
        OnChange?.Invoke();
    }
}
