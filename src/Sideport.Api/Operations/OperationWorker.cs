using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sideport.Api.Operations;

public sealed class OperationWorker(
    OperationQueue queue,
    OperationService operations,
    OperationStore store,
    ILogger<OperationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await store.ReconcileStaleRunningOperationsAsync(stoppingToken).ConfigureAwait(false);
            await operations.RequeuePendingAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationStoreException ex)
        {
            logger.LogError(ex, "queued operation rehydrate failed; operation APIs will report store errors");
        }

        await foreach (string operationId in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await operations.ProcessQueuedOperationAsync(operationId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "queued operation {OperationId} failed outside the operation record", operationId);
            }
        }
    }
}
