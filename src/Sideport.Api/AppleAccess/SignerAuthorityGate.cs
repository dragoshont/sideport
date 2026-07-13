namespace Sideport.Api.AppleAccess;

public sealed class SignerAuthorityGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await action(ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
