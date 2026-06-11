namespace Sideport.Orchestrator;

/// <summary>
/// The set of apps Sideport keeps signed. The API uses a durable file-backed
/// registry by default, while tests can still substitute an in-memory registry
/// without changing callers.
/// </summary>
public interface IAppRegistry
{
    /// <summary>All registered apps.</summary>
    Task<IReadOnlyList<AppRegistration>> ListAsync(CancellationToken ct = default);

    /// <summary>Find one registration by device UDID + bundle id, or null.</summary>
    Task<AppRegistration?> FindAsync(string udid, string bundleId, CancellationToken ct = default);

    /// <summary>Add or replace a registration.</summary>
    Task UpsertAsync(AppRegistration registration, CancellationToken ct = default);

    /// <summary>Remove a registration; returns whether it existed.</summary>
    Task<bool> RemoveAsync(string udid, string bundleId, CancellationToken ct = default);
}
