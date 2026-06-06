using Sideport.Core;

namespace Sideport.DeveloperApi;

/// <summary>
/// <see cref="ISigningIdentityProvider"/> over the developer portal: registers
/// the device, ensures a certificate (CSR → cert → PKCS#12) and an App ID +
/// provisioning profile, and materializes them as files for the signer.
///
/// The developer-portal protocol itself (P4) is not yet implemented — that work
/// is deferred until it can be validated against the live service rather than
/// guessed — so this currently surfaces a clear <see cref="NotImplementedException"/>
/// instead of fabricating unverified <c>developerservices2.apple.com</c> calls.
/// The refresh orchestrator is built and tested against the
/// <see cref="ISigningIdentityProvider"/> seam, so it does not depend on this
/// being filled in.
/// </summary>
public sealed class PortalSigningIdentityProvider(IAppleDeveloperPortal portal) : ISigningIdentityProvider
{
    private readonly IAppleDeveloperPortal _portal = portal;

    public Task<PreparedSigningInputs> PrepareAsync(
        AppleSession session, string teamId, string bundleId, string deviceUdid,
        CancellationToken ct = default)
        => throw new NotImplementedException(
            "P4: register device + ensure certificate/App-ID/profile on " +
            "developerservices2.apple.com, then assemble the PKCS#12 + profile. " +
            "Deferred until validatable against the live service.");
}
