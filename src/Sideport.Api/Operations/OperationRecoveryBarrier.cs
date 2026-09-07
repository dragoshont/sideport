using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Orchestrator;

namespace Sideport.Api.Operations;

/// <summary>
/// Startup ownership barrier for operation recovery. Its <see cref="StartAsync"/>
/// runs to completion — reconciling every operation that belonged to a prior
/// process — before the operation worker or scheduler begin serving mutations.
/// Registering it ahead of those hosted services eliminates the 30-minute gap
/// without introducing a new coordination subsystem: it reuses the durable
/// single-writer <see cref="OperationStore"/> and only reconciles state, never
/// replaying an uncertain installation.
/// </summary>
public sealed class OperationRecoveryBarrier(
    OperationStore store,
    OrchestratorOptions? options = null,
    ILogger<OperationRecoveryBarrier>? logger = null) : IHostedService
{
    private readonly ILogger<OperationRecoveryBarrier> _logger =
        logger ?? NullLogger<OperationRecoveryBarrier>.Instance;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await store.RecoverPriorProcessOperationsAsync(cancellationToken).ConfigureAwait(false);
        if (options is not null)
            DeleteOrphanedRecoverySnapshots(options.WorkDirectory);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void DeleteOrphanedRecoverySnapshots(string workDirectory)
    {
        if (!Directory.Exists(workDirectory))
            return;
        foreach (string deviceDirectory in Directory.EnumerateDirectories(workDirectory))
        {
            string recoveryDirectory = Path.Combine(deviceDirectory, "recovery");
            if (!Directory.Exists(recoveryDirectory))
                continue;
            try
            {
                Directory.Delete(recoveryDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    "could not remove orphaned private recovery snapshots ({ErrorType})",
                    ex.GetType().Name);
            }
        }
    }
}
