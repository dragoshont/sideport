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

    /// <summary>
    /// Reports whether this process still has an active mutation task for the
    /// device. Reconciliation uses this read-only seam to avoid overlapping an
    /// install whose final outcome is not yet known.
    /// </summary>
    bool IsDeviceMutationActive(string udid);
}

/// <summary>Outcome of a refresh attempt.</summary>
public sealed record RefreshResult(
    bool Success,
    string BundleId,
    DateTimeOffset? NewExpiry,
    string? Error,
    string? ErrorCode = null);

/// <summary>
/// A failure that can cross the refresh seam as a stable, non-secret code and
/// operator-safe message. Provider-specific exception text never becomes the
/// API contract.
/// </summary>
public interface IStructuredRefreshFailure
{
    string ErrorCode { get; }
    string SafeMessage { get; }
}
