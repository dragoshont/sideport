using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;
using Sideport.Core;
using Sideport.DeveloperApi.DeveloperServices;

namespace Sideport.DeveloperApi;

/// <summary>
/// <see cref="ISigningIdentityProvider"/> over the developer portal: registers
/// the device, ensures a certificate (CSR → cert → persisted PKCS#12) and an
/// App ID + provisioning profile, and materializes them as files for the signer.
///
/// Free-tier discipline (design invariant #4): a free Apple ID allows only a
/// couple of active certificates, and minting a new one revokes the old (which
/// would also revoke AltStore's). So the certificate is minted once and
/// <em>persisted</em>, then reused across refreshes while it is valid — only the
/// provisioning profile (which expires weekly) is re-downloaded each time.
/// </summary>
public sealed class PortalSigningIdentityProvider : ISigningIdentityProvider
{
    private const string DeviceName = "Sideport Device";

    private readonly IAppleDeveloperPortal _portal;
    private readonly PortalSigningOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PortalSigningIdentityProvider> _logger;

    public PortalSigningIdentityProvider(
        IAppleDeveloperPortal portal,
        PortalSigningOptions options,
        ILogger<PortalSigningIdentityProvider> logger,
        TimeProvider? timeProvider = null)
    {
        _portal = portal;
        _options = options;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<PreparedSigningInputs> PrepareAsync(
        AppleSession session, string teamId, string bundleId, string deviceUdid,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamId);
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        ArgumentException.ThrowIfNullOrEmpty(deviceUdid);

        // 1. Register the device with the team (idempotent).
        await _portal.RegisterDeviceAsync(session, teamId, deviceUdid, DeviceName, ct);

        // 2. Ensure a signing identity — reuse the persisted, still-valid one;
        //    mint (and persist) a new certificate only when necessary.
        byte[] identityPkcs12 = await EnsureIdentityAsync(session, teamId, ct);

        // 3. Ensure the App ID + download a fresh provisioning profile.
        ProvisioningProfile profile = await _portal.EnsureProfileAsync(session, teamId, bundleId, ct);

        // 4. Materialize the inputs the signer consumes in an owner-private,
        //    per-call directory. The PKCS#12 is password-less (kept in a 0600
        //    file under a 0700 dir); the downstream PreparedSigningIdentity step
        //    re-exports it without ever putting a password on the signer argv.
        string callDir = Path.Combine(_options.WorkDirectory, "prepared", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(callDir);
        RestrictToOwner(callDir, directory: true);

        string pkcs12Path = Path.Combine(callDir, "identity.p12");
        string profilePath = Path.Combine(callDir, "profile.mobileprovision");
        await File.WriteAllBytesAsync(pkcs12Path, identityPkcs12, ct);
        await File.WriteAllBytesAsync(profilePath, profile.MobileProvision, ct);
        RestrictToOwner(pkcs12Path, directory: false);
        RestrictToOwner(profilePath, directory: false);

        return new PreparedSigningInputs(pkcs12Path, string.Empty, profilePath, profile.ExpiresAt);
    }

    /// <summary>
    /// Return the persisted signing PKCS#12, minting + persisting a new
    /// certificate only when there is no valid persisted one.
    /// </summary>
    private async Task<byte[]> EnsureIdentityAsync(AppleSession session, string teamId, CancellationToken ct)
    {
        string dir = _options.IdentityDirectory ?? Path.Combine(_options.WorkDirectory, "identities");
        Directory.CreateDirectory(dir);
        RestrictToOwner(dir, directory: true);
        string path = Path.Combine(dir, $"{IdentityKey(session.AppleId, teamId)}.p12");

        if (File.Exists(path))
        {
            byte[] existing = await File.ReadAllBytesAsync(path, ct);
            if (TryReadCertificateExpiry(existing, out DateTimeOffset notAfter) &&
                notAfter - _time.GetUtcNow() > _options.CertificateRenewLeadTime)
            {
                _logger.LogDebug("reusing persisted signing identity (expires {Expiry:o})", notAfter);
                return existing;
            }

            _logger.LogInformation(
                "persisted signing identity missing or near expiry; minting a new certificate");
        }

        using var keyPair = new DevelopmentKeyPair();
        byte[] csrDer = keyPair.CreateCsrDer();
        SigningCertificate certificate = await _portal.EnsureCertificateAsync(session, teamId, csrDer, ct);
        byte[] pkcs12 = keyPair.ExportPkcs12(certificate.CertificateDer, string.Empty);

        await File.WriteAllBytesAsync(path, pkcs12, ct);
        RestrictToOwner(path, directory: false);
        _logger.LogInformation(
            "minted signing certificate serial {Serial} (expires {Expiry:o})",
            certificate.SerialNumber, certificate.ExpiresAt);
        return pkcs12;
    }

    private static bool TryReadCertificateExpiry(byte[] pkcs12, out DateTimeOffset notAfter)
    {
        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(pkcs12, string.Empty);
            notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            return true;
        }
        catch
        {
            notAfter = default;
            return false;
        }
    }

    /// <summary>
    /// A stable, filesystem-safe key per (Apple ID, team) — a short SHA-256 of
    /// the lowercased Apple ID (no raw e-mail on disk) plus the team id.
    /// </summary>
    private static string IdentityKey(string appleId, string teamId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(appleId.ToLowerInvariant()));
        string shortHash = Convert.ToHexStringLower(hash)[..16];
        string safeTeam = new string(teamId.Where(char.IsLetterOrDigit).ToArray());
        return $"{shortHash}-{safeTeam}";
    }

    private static void RestrictToOwner(string path, bool directory)
    {
        if (OperatingSystem.IsWindows())
            return; // NTFS ACLs differ; the Linux/macOS host is the deployment target.

        UnixFileMode mode = directory
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite;
        File.SetUnixFileMode(path, mode);
    }
}
