using System.Collections.Concurrent;

namespace Sideport.Orchestrator;

/// <summary>Thread-safe in-memory <see cref="IAppRegistry"/> for v1.</summary>
public sealed class InMemoryAppRegistry : IAppRegistry
{
    private readonly ConcurrentDictionary<string, AppRegistration> _apps = new();

    public Task<IReadOnlyList<AppRegistration>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AppRegistration>>([.. _apps.Values]);

    public Task<AppRegistration?> FindAsync(string udid, string bundleId, CancellationToken ct = default) =>
        Task.FromResult(_apps.GetValueOrDefault(KeyOf(udid, bundleId)));

    public Task UpsertAsync(AppRegistration registration, CancellationToken ct = default)
    {
        _apps[registration.Key] = registration;
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string udid, string bundleId, CancellationToken ct = default) =>
        Task.FromResult(_apps.TryRemove(KeyOf(udid, bundleId), out _));

    private static string KeyOf(string udid, string bundleId) => $"{udid}:{bundleId}";
}
