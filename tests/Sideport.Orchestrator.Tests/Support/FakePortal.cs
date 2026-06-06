using Sideport.Core;

namespace Sideport.Orchestrator.Tests.Support;

/// <summary>
/// A configurable fake <see cref="IAppleDeveloperPortal"/>: scripts the auth
/// result and counts calls, so the session manager + orchestrator can be tested
/// without any real GrandSlam traffic.
/// </summary>
internal sealed class FakePortal : IAppleDeveloperPortal
{
    public Func<string, string, AppleLoginResult> OnAuthenticate { get; set; } =
        (appleId, _) => new AppleLoginResult.Success(
            new AppleSession(appleId, "adsid-" + appleId, "Account", new byte[32]) { IdmsToken = "idms" });

    public int AuthenticateCalls { get; private set; }
    public int SubmitCodeCalls { get; private set; }
    public string? LastSubmittedCode { get; private set; }

    public Task<AppleLoginResult> AuthenticateAsync(string appleId, string password, CancellationToken ct = default)
    {
        AuthenticateCalls++;
        return Task.FromResult(OnAuthenticate(appleId, password));
    }

    public Task SubmitTwoFactorCodeAsync(AppleLoginChallenge challenge, string code, CancellationToken ct = default)
    {
        SubmitCodeCalls++;
        LastSubmittedCode = code;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AppleTeam>> ListTeamsAsync(AppleSession session, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AppleTeam>>([new AppleTeam("TEAM", "Team", "free")]);

    public Task RegisterDeviceAsync(AppleSession session, string teamId, string udid, string name, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<SigningCertificate> EnsureCertificateAsync(AppleSession session, string teamId, byte[] csrDer, CancellationToken ct = default) =>
        Task.FromResult(new SigningCertificate("serial", new byte[1], DateTimeOffset.UtcNow.AddDays(7)));

    public Task<ProvisioningProfile> EnsureProfileAsync(AppleSession session, string teamId, string bundleId, CancellationToken ct = default) =>
        Task.FromResult(new ProvisioningProfile("pid", bundleId, new byte[1], DateTimeOffset.UtcNow.AddDays(7)));
}
