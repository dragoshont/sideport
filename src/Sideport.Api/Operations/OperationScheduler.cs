using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Orchestrator;

namespace Sideport.Api.Operations;

public sealed class OperationScheduler(
    IAppRegistry registry,
    RefreshOrchestrator orchestrator,
    OperationService operations,
    OrchestratorOptions options,
    TimeProvider? timeProvider = null,
    ILogger<OperationScheduler>? logger = null) : BackgroundService
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ILogger<OperationScheduler> _logger = logger ?? NullLogger<OperationScheduler>.Instance;
    private static readonly OperationActorDto SchedulerActor = new("system", "system:scheduler");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(options.ScheduleInterval, _time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "operation scheduler tick failed");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        DateTimeOffset now = _time.GetUtcNow();
        IReadOnlyList<AppRegistration> apps = await registry.ListAsync(ct).ConfigureAwait(false);
        var due = apps
            .Select(app => (app, state: orchestrator.GetState(app.DeviceUdid, app.BundleId)))
            .Where(item => item.state is null || item.state.IsDue(now, options.RefreshLeadTime, options.ResignInterval))
            .OrderBy(item => item.state?.ExpiresAt ?? DateTimeOffset.MinValue)
            .ToList();

        if (due.Count == 0)
            return;

        _logger.LogInformation("scheduler: enqueueing {Count} app refresh operation(s)", due.Count);
        foreach ((AppRegistration app, _) in due)
        {
            ct.ThrowIfCancellationRequested();
            string idempotencyKey = $"scheduler:{app.DeviceUdid}:{app.BundleId}:{now:yyyyMMddHH}";
            await operations.RefreshAsync(app.DeviceUdid, app.BundleId, SchedulerActor, idempotencyKey, ct: ct).ConfigureAwait(false);
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
