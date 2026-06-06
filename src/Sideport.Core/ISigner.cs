namespace Sideport.Core;

/// <summary>
/// IPA code-signing seam (design §6). v1 shells out to a cross-platform,
/// license-clean signer binary sidecar — <c>zsign</c> (MIT, proven on the
/// homelab host) by default, migrating to <c>rcodesign</c> (Apache-2.0) later.
/// A pure-managed Mach-O signer is explicitly out of v1 scope.
/// </summary>
public interface ISigner
{
    /// <summary>
    /// Re-sign <paramref name="request"/>'s IPA with the supplied signing
    /// identity and provisioning profile, producing a new signed IPA.
    /// </summary>
    Task<SignResult> SignAsync(SignRequest request, CancellationToken ct = default);
}

/// <summary>Inputs for a single re-sign operation.</summary>
public sealed record SignRequest(
    string InputIpaPath,
    string OutputIpaPath,
    string SigningCertificatePkcs12Path,
    string ProvisioningProfilePath);

/// <summary>Outcome of a re-sign operation.</summary>
public sealed record SignResult(
    bool Success,
    string OutputIpaPath,
    string? BundleId,
    string? Error);
