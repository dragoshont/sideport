namespace Sideport.Core;

/// <summary>
/// The Apple-facing surface Sideport actually owns and clean-room implements
/// (design §6): GrandSlam auth + the developer portal
/// (<c>developerservices2.apple.com</c>). Reimplemented from Apple's documented
/// endpoints and the pypush spec — never translated from AGPL AltSign source.
/// </summary>
public interface IAppleDeveloperPortal
{
    /// <summary>
    /// Authenticate an Apple ID via GrandSlam (SRP-6a + anisette). The result is
    /// either <see cref="AppleLoginResult.Success"/> (the device is already
    /// trusted) or <see cref="AppleLoginResult.TwoFactorRequired"/>. In the 2FA
    /// case, deliver the code via <see cref="SubmitTwoFactorCodeAsync"/> and then
    /// call this method again with the same credentials.
    /// </summary>
    Task<AppleLoginResult> AuthenticateAsync(
        string appleId, string password, CancellationToken ct = default);

    /// <summary>
    /// Submit a trusted-device / SMS 2FA code for a pending
    /// <see cref="AppleLoginChallenge"/>. On success, re-call
    /// <see cref="AuthenticateAsync"/> to obtain the session (matching Apple's
    /// re-authenticate-after-validation flow). The password is intentionally not
    /// stored on the challenge, so the caller re-supplies it on the retry.
    /// </summary>
    Task SubmitTwoFactorCodeAsync(
        AppleLoginChallenge challenge, string code, CancellationToken ct = default);

    /// <summary>List development teams visible to the session.</summary>
    Task<IReadOnlyList<AppleTeam>> ListTeamsAsync(
        AppleSession session, CancellationToken ct = default);

    /// <summary>Register a device UDID with a team (idempotent).</summary>
    Task RegisterDeviceAsync(
        AppleSession session, string teamId, string udid, string name,
        CancellationToken ct = default);

    /// <summary>Mint (or fetch) a signing certificate from a CSR.</summary>
    Task<SigningCertificate> EnsureCertificateAsync(
        AppleSession session, string teamId, byte[] csrDer,
        CancellationToken ct = default);

    /// <summary>Ensure an App ID + capabilities and return a provisioning profile.</summary>
    Task<ProvisioningProfile> EnsureProfileAsync(
        AppleSession session, string teamId, string bundleId,
        CancellationToken ct = default);
}

/// <summary>The outcome of a GrandSlam authentication attempt.</summary>
public abstract record AppleLoginResult
{
    private AppleLoginResult() { }

    /// <summary>Authentication completed; <see cref="Session"/> is usable.</summary>
    public sealed record Success(AppleSession Session) : AppleLoginResult;

    /// <summary>
    /// Apple requires a second factor. Deliver a code for
    /// <see cref="Challenge"/> via
    /// <see cref="IAppleDeveloperPortal.SubmitTwoFactorCodeAsync"/>, then retry.
    /// </summary>
    public sealed record TwoFactorRequired(AppleLoginChallenge Challenge) : AppleLoginResult;
}

/// <summary>
/// A resumable 2FA challenge. Carries only the identifiers needed to request and
/// validate the code (never the password), so it is safe to hold and pass around.
/// </summary>
public sealed record AppleLoginChallenge(string Adsid, string IdmsToken, TwoFactorKind Kind);

/// <summary>The second-factor delivery channel Apple asked for.</summary>
public enum TwoFactorKind
{
    /// <summary>Code pushed to a trusted Apple device.</summary>
    TrustedDevice,

    /// <summary>Code sent over SMS.</summary>
    Sms,
}

/// <summary>An authenticated GrandSlam session (ADSID + session key material).</summary>
public sealed record AppleSession(
    string AppleId,
    string Adsid,
    string AccountName,
    byte[] SessionKey)
{
    /// <summary>
    /// The GrandSlam token used as <c>X-Apple-GS-Token</c> for the
    /// developer-services endpoints. This is the APP-SPECIFIC token fetched after
    /// login (the GSA app-tokens flow), not the raw login <c>GsIdmsToken</c> —
    /// developer-services rejects the latter ("session expired").
    /// </summary>
    public string IdmsToken { get; init; } = "";
}

/// <summary>An Apple Developer team.</summary>
public sealed record AppleTeam(string TeamId, string Name, string Type);

/// <summary>A minted signing certificate.</summary>
public sealed record SigningCertificate(
    string SerialNumber,
    byte[] CertificateDer,
    DateTimeOffset ExpiresAt);

/// <summary>A provisioning profile for a bundle ID.</summary>
public sealed record ProvisioningProfile(
    string ProfileId,
    string BundleId,
    byte[] MobileProvision,
    DateTimeOffset ExpiresAt);
