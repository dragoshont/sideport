namespace Sideport.Core;

/// <summary>
/// Prepares the concrete signing inputs (a usable PKCS#12 identity + a
/// provisioning profile on disk) for one app on one device, given an
/// authenticated session. The real implementation performs the developer-portal
/// work — register device, ensure certificate (CSR → cert → p12), ensure App ID
/// + profile — and materializes the results as files the signer can consume.
///
/// It is a seam so the refresh orchestrator can be built and tested against the
/// stable <see cref="PreparedSigningInputs"/> contract independently of the
/// developer-portal protocol implementation.
/// </summary>
public interface ISigningIdentityProvider
{
    /// <summary>
    /// Ensure a signing identity + provisioning profile for
    /// <paramref name="bundleId"/> on <paramref name="deviceUdid"/> and return
    /// paths the signer can use. Implementations reuse an existing certificate
    /// and App ID where possible (respecting Apple's free-tier limits) rather
    /// than minting new ones each call.
    /// </summary>
    Task<PreparedSigningInputs> PrepareAsync(
        AppleSession session,
        string teamId,
        string bundleId,
        string deviceUdid,
        CancellationToken ct = default);
}

/// <summary>
/// Ready-to-use signing inputs materialized on disk, plus the resulting signing
/// expiry. The owner is responsible for disposing transient files.
/// </summary>
public sealed record PreparedSigningInputs(
    string Pkcs12Path,
    string Pkcs12Password,
    string ProvisioningProfilePath,
    DateTimeOffset ExpiresAt);
