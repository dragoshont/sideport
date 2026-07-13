using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sideport.Api.Operations;

namespace Sideport.Api.DeviceInventory;

public sealed class DeviceEnrollmentWorker(
    DeviceEnrollmentQueue queue,
    DeviceEnrollmentService enrollments,
    ILogger<DeviceEnrollmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await enrollments.RequeuePendingAsync(stoppingToken).ConfigureAwait(false);
                break;
            }
            catch (OperationStoreException ex)
            {
                logger.LogError(ex, "device-enrollment rehydrate failed; enrollment APIs will report store errors");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }

        await foreach (string operationId in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await enrollments.ProcessAsync(operationId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "device-enrollment operation {OperationId} failed outside its durable record", operationId);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                // The durable record remains authoritative. Requeueing lets a
                // recovered store/transport resume it; post-pair records enter
                // passive recovery and can never request Trust again blindly.
                queue.Enqueue(operationId);
            }
        }
    }
}
