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

    public Task<int> RebindAppleAuthorityAsync(string currentAppleId, string currentTeamId, string replacementAppleId, string replacementTeamId, CancellationToken ct = default)
    {
        AppRegistration[] affected = _apps.Values.Where(app => string.Equals(app.AppleId, currentAppleId, StringComparison.OrdinalIgnoreCase) && string.Equals(app.TeamId, currentTeamId, StringComparison.Ordinal)).ToArray();
        foreach (AppRegistration app in affected) _apps[app.Key] = app with { AppleId = replacementAppleId, TeamId = replacementTeamId };
        return Task.FromResult(affected.Length);
    }

    public Task<int> RebindAppleAuthorityByProfileAsync(string currentAccountProfileId, string currentTeamId, string replacementAppleId, string replacementTeamId, CancellationToken ct = default)
    {
        AppRegistration[] affected = _apps.Values.Where(app => string.Equals(ProfileId(app.AppleId), currentAccountProfileId, StringComparison.Ordinal) && string.Equals(app.TeamId, currentTeamId, StringComparison.Ordinal)).ToArray();
        foreach (AppRegistration app in affected) _apps[app.Key] = app with { AppleId = replacementAppleId, TeamId = replacementTeamId };
        return Task.FromResult(affected.Length);
    }
    private static string ProfileId(string appleId) => $"acct_{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(appleId.Trim().ToLowerInvariant())))[..20]}";

    private static string KeyOf(string udid, string bundleId) => $"{udid}:{bundleId}";
}
