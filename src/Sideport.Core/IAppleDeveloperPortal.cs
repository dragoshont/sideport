namespace Sideport.Core;

/// <summary>
/// The Apple-facing surface Sideport actually owns and clean-room implements
/// (design §6): GrandSlam auth + the developer portal
/// (<c>developerservices2.apple.com</c>). Reimplemented from Apple's documented
/// endpoints and the pypush spec — never translated from AGPL AltSign source.
/// </summary>
public interface IAppleDeveloperPortal
{
    /// <summary>Authenticate an Apple ID via GrandSlam (SRP-6a + anisette).</summary>
    Task<AppleSession> AuthenticateAsync(
        string appleId, string password, CancellationToken ct = default);

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

/// <summary>An authenticated GrandSlam session (ADSID + session key material).</summary>
public sealed record AppleSession(
    string AppleId,
    string Adsid,
    string AccountName,
    byte[] SessionKey);

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
