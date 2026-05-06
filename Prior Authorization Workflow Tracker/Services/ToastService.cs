namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Scoped service that manages the toast notification queue per Blazor circuit (§16.2, GAP-13).
/// Scoped lifetime ensures toasts raised in one browser tab do not bleed into other tabs.
/// Components subscribe to <see cref="OnChange"/> and render the queue.
/// </summary>
public sealed class ToastService
{
    public event Action? OnChange;

    private readonly List<ToastMessage> _messages = [];

    public IReadOnlyList<ToastMessage> Messages => _messages;

    /// <summary>Displays a toast with the specified level and message.</summary>
    /// <remarks>Use the ShowSuccess/ShowError/ShowWarning overloads — this base overload is called internally.</remarks>

    public void ShowSuccess(string message, string? linkText = null, string? linkUrl = null)
        => Show(message, ToastLevel.Success, linkText: linkText, linkUrl: linkUrl);
    public void ShowError(string message)   => Show(message, ToastLevel.Error, durationMs: 8000);
    public void ShowWarning(string message) => Show(message, ToastLevel.Warning);

    public void Show(string message, ToastLevel level = ToastLevel.Info, int durationMs = 4000,
        string? linkText = null, string? linkUrl = null)
    {
        var toast = new ToastMessage(Guid.NewGuid(), message, level, durationMs, linkText, linkUrl);
        _messages.Add(toast);
        OnChange?.Invoke();

        // Schedule auto-dismiss
        _ = Task.Delay(durationMs).ContinueWith(_ =>
        {
            Dismiss(toast.Id);
        });
    }

    public void Dismiss(Guid id)
    {
        var removed = _messages.RemoveAll(m => m.Id == id);
        if (removed > 0) OnChange?.Invoke();
    }
}

public sealed record ToastMessage(
    Guid Id,
    string Message,
    ToastLevel Level,
    int DurationMs,
    string? LinkText = null,
    string? LinkUrl  = null);

public enum ToastLevel { Info, Success, Warning, Error }
