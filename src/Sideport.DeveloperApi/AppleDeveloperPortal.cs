using Sideport.Core;

namespace Sideport.DeveloperApi;

/// <summary>
/// Clean-room <see cref="IAppleDeveloperPortal"/> over GrandSlam +
/// <c>developerservices2.apple.com</c> (design §6/§8 phases 2-3). Reimplemented
/// from Apple's documented endpoints + the pypush spec — never translated from
/// AGPL AltSign source. Uses <see cref="IAnisetteProvider"/> for the required
/// <c>X-Apple-I-MD*</c> headers.
/// </summary>
public sealed class AppleDeveloperPortal(IAnisetteProvider anisette) : IAppleDeveloperPortal
{
    private readonly IAnisetteProvider _anisette = anisette;

    public Task<AppleSession> AuthenticateAsync(string appleId, string password, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 2: managed SRP-6a + s2k + anisette headers → ADSID/SPD/session.");

    public Task<IReadOnlyList<AppleTeam>> ListTeamsAsync(AppleSession session, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 3: listTeams.");

    public Task RegisterDeviceAsync(AppleSession session, string teamId, string udid, string name, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 3: addDevice.");

    public Task<SigningCertificate> EnsureCertificateAsync(AppleSession session, string teamId, byte[] csrDer, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 3: CSR → submitDevelopmentCSR.");

    public Task<ProvisioningProfile> EnsureProfileAsync(AppleSession session, string teamId, string bundleId, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 3: ensure App ID + capabilities, fetch profile.");
}
