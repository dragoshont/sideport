using Microsoft.Extensions.Hosting;

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
public sealed class OperationRecoveryBarrier(OperationStore store) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await store.RecoverPriorProcessOperationsAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
