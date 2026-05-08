using Prior_Authorization_Workflow_Tracker.Models;

namespace Prior_Authorization_Workflow_Tracker.Services.Demo;

/// <summary>
/// Runs the expiration check inline (no background timer in WASM).
/// Matches expired approved authorizations and transitions them to Expired.
/// </summary>
public sealed class DemoExpirationJobTrigger : IExpirationJobTrigger
{
    private readonly DemoDataStore       _store;
    private readonly IWorkflowService    _workflow;

    public DemoExpirationJobTrigger(DemoDataStore store, IWorkflowService workflow)
    {
        _store    = store;
        _workflow = workflow;
    }

    public async Task<int> TriggerAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expired = _store.Requests
            .Where(r => r.Status == PaStatus.Approved &&
                        r.AuthorizationEndDate.HasValue &&
                        r.AuthorizationEndDate.Value < now)
            .ToList();

        int count = 0;
        foreach (var r in expired)
        {
            try
            {
                await _workflow.ExpireAsync(r.ID, ct);
                count++;
            }
            catch { /* skip individual failures */ }
        }
        return count;
    }
}
