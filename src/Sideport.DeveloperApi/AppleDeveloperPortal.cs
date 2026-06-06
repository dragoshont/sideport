using Sideport.Core;
using Sideport.DeveloperApi.GrandSlam;

namespace Sideport.DeveloperApi;

/// <summary>
/// Clean-room <see cref="IAppleDeveloperPortal"/> over GrandSlam +
/// <c>developerservices2.apple.com</c> (design §6/§8 phases 2-3). Reimplemented
/// from Apple's documented endpoints + the pypush spec — never translated from
/// AGPL AltSign source. Authentication (P3) is delegated to
/// <see cref="GrandSlamClient"/>; the developer-services resource methods land
/// in P4.
/// </summary>
public sealed class AppleDeveloperPortal : IAppleDeveloperPortal
{
    private readonly GrandSlamClient _grandSlam;

    internal AppleDeveloperPortal(GrandSlamClient grandSlam)
    {
        _grandSlam = grandSlam;
    }

    public Task<AppleLoginResult> AuthenticateAsync(string appleId, string password, CancellationToken ct = default)
        => _grandSlam.AuthenticateAsync(appleId, password, ct);

    public Task SubmitTwoFactorCodeAsync(AppleLoginChallenge challenge, string code, CancellationToken ct = default)
        => _grandSlam.SubmitTwoFactorCodeAsync(challenge, code, ct);

    public Task<IReadOnlyList<AppleTeam>> ListTeamsAsync(AppleSession session, CancellationToken ct = default)
        => throw new NotImplementedException("P4: listTeams.");

    public Task RegisterDeviceAsync(AppleSession session, string teamId, string udid, string name, CancellationToken ct = default)
        => throw new NotImplementedException("P4: addDevice.");

    public Task<SigningCertificate> EnsureCertificateAsync(AppleSession session, string teamId, byte[] csrDer, CancellationToken ct = default)
        => throw new NotImplementedException("P4: CSR → submitDevelopmentCSR.");

    public Task<ProvisioningProfile> EnsureProfileAsync(AppleSession session, string teamId, string bundleId, CancellationToken ct = default)
        => throw new NotImplementedException("P4: ensure App ID + capabilities, fetch profile.");
}

