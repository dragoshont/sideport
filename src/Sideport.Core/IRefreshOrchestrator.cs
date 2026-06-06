namespace Sideport.Core;

/// <summary>
/// Refresh orchestrator seam (design §4/§8 phase 4). Owns the single-flight
/// lock that serializes every re-sign through one host so Sideport never
/// becomes a competing signer, and drives the
/// auth → ensure cert/app-ID/profile → re-sign → install loop.
/// </summary>
public interface IRefreshOrchestrator
{
    /// <summary>Refresh a single app's signature on a single device, now.</summary>
    Task<RefreshResult> RefreshAsync(
        string udid, string bundleId, CancellationToken ct = default);
}

/// <summary>Outcome of a refresh attempt.</summary>
public sealed record RefreshResult(
    bool Success,
    string BundleId,
    DateTimeOffset? NewExpiry,
    string? Error);
