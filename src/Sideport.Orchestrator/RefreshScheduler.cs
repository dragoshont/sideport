using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sideport.Orchestrator;

/// <summary>
/// Background scheduler that proactively re-signs registered apps before their
/// signatures lapse. On each tick it refreshes the apps that are due (never
/// signed, or expiring within the lead time), soonest-expiring first, one at a
/// time. Serialization is also guaranteed by the orchestrator's single-flight
/// lock; doing it sequentially here just avoids queueing redundant work.
/// </summary>
public sealed class RefreshScheduler : BackgroundService
{
    private readonly IAppRegistry _registry;
    private readonly RefreshOrchestrator _orchestrator;
    private readonly OrchestratorOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<RefreshScheduler> _logger;

    public RefreshScheduler(
        IAppRegistry registry,
        RefreshOrchestrator orchestrator,
        OrchestratorOptions options,
        TimeProvider? timeProvider = null,
        ILogger<RefreshScheduler>? logger = null)
    {
        _registry = registry;
        _orchestrator = orchestrator;
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<RefreshScheduler>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(_options.ScheduleInterval, _time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A scheduler tick must never crash the service.
                _logger.LogError(ex, "refresh scheduler tick failed");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    /// <summary>Evaluate the catalog once and refresh every due app.</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        DateTimeOffset now = _time.GetUtcNow();
        IReadOnlyList<AppRegistration> apps = await _registry.ListAsync(ct);

        var due = apps
            .Select(app => (app, state: _orchestrator.GetState(app.DeviceUdid, app.BundleId)))
            .Where(x => x.state is null || x.state.IsDue(now, _options.RefreshLeadTime))
            .OrderBy(x => x.state?.ExpiresAt ?? DateTimeOffset.MinValue)
            .ToList();

        if (due.Count == 0)
            return;

        _logger.LogInformation("scheduler: {Count} app(s) due for refresh", due.Count);
        foreach ((AppRegistration app, _) in due)
        {
            ct.ThrowIfCancellationRequested();
            await _orchestrator.RefreshAsync(app.DeviceUdid, app.BundleId, ct);
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
