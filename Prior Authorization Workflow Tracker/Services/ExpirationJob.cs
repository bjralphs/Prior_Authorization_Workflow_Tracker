using Microsoft.EntityFrameworkCore;
using Prior_Authorization_Workflow_Tracker.Data;
using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;
using Prior_Authorization_Workflow_Tracker.Services.Abstractions;

namespace Prior_Authorization_Workflow_Tracker.Services;

/// <summary>
/// Hosted background service that expires approved PA requests whose authorization
/// period has ended (§8.6, FR-006, BUG-008 mitigation).
///
/// Runs every 24 hours using PeriodicTimer for drift-stable scheduling.
/// Also executes once immediately at application startup to catch any requests
/// that expired while the service was offline.
///
/// Guards against double-expiration: WorkflowService.ExpireAsync throws
/// WorkflowTransitionException when a request is not in Approved status.
///
/// Uses DbSeeder.SystemUserId for all PaStatusHistory entries — never accesses
/// ICurrentUserService, which is HTTP-scoped and unavailable in a background thread.
/// </summary>
public sealed class ExpirationJob : BackgroundService, IExpirationJobTrigger
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ExpirationJob> _logger;

    public ExpirationJob(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider clock,
        ILogger<ExpirationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _clock        = clock;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExpirationJob started.");

        using var timer = new PeriodicTimer(CheckInterval);

        // Fire immediately on startup, then once per interval (BUG-008: catches missed runs)
        do
        {
            try
            {
                await RunCheckAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExpirationJob: unhandled error during check. Will retry next cycle.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        _logger.LogInformation("ExpirationJob stopped.");
    }

    // ── IExpirationJobTrigger — for Admin manual trigger (PRD §8.8) ───────────

    /// <summary>
    /// Runs the expiration check on demand and returns the number of requests expired.
    /// Called from the Admin UI (Users.razor). Uses the same logic as the scheduled run.
    /// </summary>
    public Task<int> TriggerAsync(CancellationToken ct = default)
        => RunCheckAsync(ct);

    // ── Core check loop ───────────────────────────────────────────────────────

    private async Task<int> RunCheckAsync(CancellationToken ct)
    {
        var today = _clock.UtcNow.Date;
        _logger.LogDebug("ExpirationJob: running check for {Date:yyyy-MM-dd}.", today);

        // Resolve candidate IDs in a short-lived scope to avoid holding an open connection
        IReadOnlyList<int> candidateIds = await GetExpiredRequestIdsAsync(today, ct);

        if (candidateIds.Count == 0)
        {
            _logger.LogDebug("ExpirationJob: no expired authorizations to process.");
            return 0;
        }

        _logger.LogInformation("ExpirationJob: found {Count} request(s) to expire.", candidateIds.Count);

        var expired = 0;
        var skipped = 0;
        var failed  = 0;

        foreach (var id in candidateIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ExpireSingleAsync(id, ct);
                expired++;
                _logger.LogInformation("ExpirationJob: expired request {RequestId}.", id);
            }
            catch (WorkflowTransitionException ex)
            {
                // Already expired or in a terminal state — harmless race condition
                _logger.LogDebug(
                    "ExpirationJob: skipped request {RequestId} (already transitioned): {Message}",
                    id, ex.Message);
                skipped++;
            }
            catch (Exception ex)
            {
                // One failure must not stop others
                _logger.LogError(ex, "ExpirationJob: failed to expire request {RequestId}.", id);
                failed++;
            }
        }

        _logger.LogInformation(
            "ExpirationJob: complete — {Expired} expired, {Skipped} skipped, {Failed} failed.",
            expired, skipped, failed);
        return expired;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<int>> GetExpiredRequestIdsAsync(DateTime today, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync(ct);

        return await db.PaRequests
            .AsNoTracking()
            .Where(r => r.Status == PaStatus.Approved
                     && r.AuthorizationEndDate.HasValue
                     && r.AuthorizationEndDate.Value.Date < today)
            .Select(r => r.ID)
            .ToListAsync(ct);
    }

    private async Task ExpireSingleAsync(int requestId, CancellationToken ct)
    {
        // Each request gets its own scope so failures are isolated per DI graph
        await using var scope = _scopeFactory.CreateAsyncScope();
        var workflow = scope.ServiceProvider.GetRequiredService<IWorkflowService>();
        await workflow.ExpireAsync(requestId, ct);
    }
}
