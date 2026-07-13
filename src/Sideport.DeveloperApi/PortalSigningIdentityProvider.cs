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
/// couple of active certificates. Sideport mints once and <em>persists</em> the
/// matching private key, then reuses that identity while it is valid. If Apple
/// already has a certificate whose key Sideport does not own, this provider
/// fails closed; replacement belongs to an explicit cutover flow.
/// </summary>
public sealed class PortalSigningIdentityProvider : ISigningIdentityProvider
{
    private const string DeviceName = "Sideport Device";

    private readonly IAppleDeveloperPortal _portal;
    private readonly PortalSigningOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PortalSigningIdentityProvider> _logger;
    private readonly SemaphoreSlim _identityLock = new(1, 1);

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

    public Task<PreparedSigningInputs> PrepareAsync(
        AppleSession session, string teamId, string bundleId, string deviceUdid,
        CancellationToken ct = default) =>
        PrepareAsync(
            session,
            teamId,
            bundleId,
            deviceUdid,
            allowCertificateCreation: true,
            ct);

    public async Task<PreparedSigningInputs> PrepareAsync(
        AppleSession session,
        string teamId,
        string bundleId,
        string deviceUdid,
        bool allowCertificateCreation,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamId);
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        ArgumentException.ThrowIfNullOrEmpty(deviceUdid);

        // 1. Ensure a signing identity — reuse the persisted, still-valid one;
        //    request and persist a new certificate only when Apple's inventory
        //    is empty; existing certificates are never revoked here. This
        //    intentionally happens before device registration so a certificate
        //    cutover requirement cannot partially mutate the Apple team.
        byte[] identityPkcs12 = await EnsureIdentityAsync(
            session,
            teamId,
            allowCertificateCreation,
            ct);

        try
        {
            // 2. Register the device with the team (idempotent).
            await _portal.RegisterDeviceAsync(session, teamId, deviceUdid, DeviceName, ct);

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
        finally
        {
            CryptographicOperations.ZeroMemory(identityPkcs12);
        }
    }

    public async Task<SigningIdentityInspection> InspectAsync(
        string appleId,
        string teamId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(appleId);
        ArgumentException.ThrowIfNullOrEmpty(teamId);
        ct.ThrowIfCancellationRequested();

        string directory = _options.IdentityDirectory ?? Path.Combine(_options.WorkDirectory, "identities");
        string path = Path.Combine(directory, $"{IdentityKey(appleId, teamId)}.p12");
        if (!File.Exists(path))
            return new SigningIdentityInspection("missing", null, null);

        byte[] identity = await File.ReadAllBytesAsync(path, ct);
        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(identity, string.Empty);
            if (!certificate.HasPrivateKey)
                return new SigningIdentityInspection("corrupt", null, null);

            DateTimeOffset expiresAt = new(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            DateTimeOffset now = _time.GetUtcNow();
            string state = expiresAt <= now
                ? "expired"
                : expiresAt - now <= _options.CertificateRenewLeadTime
                    ? "expiring"
                    : "reusable";
            string serial = certificate.SerialNumber ?? string.Empty;
            string? suffix = serial.Length == 0 ? null : serial[^Math.Min(4, serial.Length)..];
            return new SigningIdentityInspection(state, expiresAt, suffix);
        }
        catch (CryptographicException)
        {
            return new SigningIdentityInspection("corrupt", null, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(identity);
        }
    }

    public async Task<SigningIdentityInspection> ReplaceAsync(
        AppleSession session,
        string teamId,
        IReadOnlyList<string> acknowledgedCertificateIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        string[] acknowledged = acknowledgedCertificateIds
            .Select(id => id?.Trim() ?? string.Empty)
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        await _identityLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            IReadOnlyList<AppleDevelopmentCertificate> inventory =
                await _portal.ListDevelopmentCertificatesAsync(session, teamId, ct).ConfigureAwait(false);
            string[] current = inventory.Select(certificate => certificate.Id)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (current.Any(id => !acknowledged.Contains(id, StringComparer.Ordinal)))
                throw new SigningReplacementInventoryChangedException();

            foreach (string certificateId in current)
                await _portal.RevokeDevelopmentCertificateAsync(session, teamId, certificateId, ct).ConfigureAwait(false);

            string dir = _options.IdentityDirectory ?? Path.Combine(_options.WorkDirectory, "identities");
            Directory.CreateDirectory(dir);
            RestrictToOwner(dir, directory: true);
            string path = Path.Combine(dir, $"{IdentityKey(session.AppleId, teamId)}.p12");
            string pendingKeyPath = path + ".pending.pkcs8";
            TryDelete(path);
            using DevelopmentKeyPair keyPair = await LoadOrCreatePendingKeyAsync(pendingKeyPath, ct).ConfigureAwait(false);
            SigningCertificate certificate = await _portal.EnsureCertificateAsync(
                session, teamId, keyPair.CreateCsrDer(), ct).ConfigureAwait(false);
            byte[] pkcs12 = keyPair.ExportPkcs12(certificate.CertificateDer, string.Empty);
            try
            {
                await WritePrivateFileAtomicallyAsync(path, pkcs12, ct).ConfigureAwait(false);
                TryDelete(pendingKeyPath);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkcs12);
            }
            string serial = certificate.SerialNumber ?? string.Empty;
            return new SigningIdentityInspection(
                "reusable",
                certificate.ExpiresAt,
                serial.Length == 0 ? null : serial[^Math.Min(4, serial.Length)..]);
        }
        finally
        {
            _identityLock.Release();
        }
    }

    public async Task<SigningIdentityInspection> ReplaceAndFinalizeAsync(
        AppleSession session,
        string teamId,
        IReadOnlyList<string> acknowledgedCertificateIds,
        bool allowPersistedIdentityRecovery,
        Func<SigningIdentityInspection, CancellationToken, Task> finalizeAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(finalizeAsync);
        await _identityLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SigningIdentityInspection current = await InspectUnlockedAsync(session.AppleId, teamId, ct).ConfigureAwait(false);
            SigningIdentityInspection result;
            if (allowPersistedIdentityRecovery && string.Equals(current.State, "reusable", StringComparison.Ordinal))
            {
                result = current;
            }
            else
            {
                result = await ReplaceLockedAsync(session, teamId, acknowledgedCertificateIds, ct).ConfigureAwait(false);
            }
            await finalizeAsync(result, ct).ConfigureAwait(false);
            return result;
        }
        finally { _identityLock.Release(); }
    }

    private async Task<SigningIdentityInspection> InspectUnlockedAsync(string appleId, string teamId, CancellationToken ct)
    {
        string directory = _options.IdentityDirectory ?? Path.Combine(_options.WorkDirectory, "identities");
        string path = Path.Combine(directory, $"{IdentityKey(appleId, teamId)}.p12");
        if (!File.Exists(path)) return new SigningIdentityInspection("missing", null, null);
        byte[] identity = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(identity, string.Empty);
            if (!certificate.HasPrivateKey) return new SigningIdentityInspection("corrupt", null, null);
            DateTimeOffset expiresAt = new(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            string state = expiresAt <= _time.GetUtcNow() ? "expired" : expiresAt - _time.GetUtcNow() <= _options.CertificateRenewLeadTime ? "expiring" : "reusable";
            string serial = certificate.SerialNumber ?? string.Empty;
            return new SigningIdentityInspection(state, expiresAt, serial.Length == 0 ? null : serial[^Math.Min(4, serial.Length)..]);
        }
        catch (CryptographicException) { return new SigningIdentityInspection("corrupt", null, null); }
        finally { CryptographicOperations.ZeroMemory(identity); }
    }

    private async Task<SigningIdentityInspection> ReplaceLockedAsync(
        AppleSession session,
        string teamId,
        IReadOnlyList<string> acknowledgedCertificateIds,
        CancellationToken ct)
    {
        string[] acknowledged = acknowledgedCertificateIds.Select(id => id?.Trim() ?? string.Empty).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        IReadOnlyList<AppleDevelopmentCertificate> inventory = await _portal.ListDevelopmentCertificatesAsync(session, teamId, ct).ConfigureAwait(false);
        string[] current = inventory.Select(certificate => certificate.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (current.Any(id => !acknowledged.Contains(id, StringComparer.Ordinal))) throw new SigningReplacementInventoryChangedException();
        foreach (string certificateId in current) await _portal.RevokeDevelopmentCertificateAsync(session, teamId, certificateId, ct).ConfigureAwait(false);
        string dir = _options.IdentityDirectory ?? Path.Combine(_options.WorkDirectory, "identities");
        Directory.CreateDirectory(dir);
        RestrictToOwner(dir, directory: true);
        string path = Path.Combine(dir, $"{IdentityKey(session.AppleId, teamId)}.p12");
        string pendingKeyPath = path + ".pending.pkcs8";
        TryDelete(path);
        using DevelopmentKeyPair keyPair = await LoadOrCreatePendingKeyAsync(pendingKeyPath, ct).ConfigureAwait(false);
        SigningCertificate certificate = await _portal.EnsureCertificateAsync(session, teamId, keyPair.CreateCsrDer(), ct).ConfigureAwait(false);
        byte[] pkcs12 = keyPair.ExportPkcs12(certificate.CertificateDer, string.Empty);
        try { await WritePrivateFileAtomicallyAsync(path, pkcs12, ct).ConfigureAwait(false); TryDelete(pendingKeyPath); }
        finally { CryptographicOperations.ZeroMemory(pkcs12); }
        string serial = certificate.SerialNumber ?? string.Empty;
        return new SigningIdentityInspection("reusable", certificate.ExpiresAt, serial.Length == 0 ? null : serial[^Math.Min(4, serial.Length)..]);
    }

    /// <summary>
    /// Return the persisted signing PKCS#12, requesting and persisting a new
    /// certificate only when there is no valid persisted one and Apple has no
    /// existing certificate that would require an explicit cutover.
    /// </summary>
    private async Task<byte[]> EnsureIdentityAsync(
        AppleSession session,
        string teamId,
        bool allowCertificateCreation,
        CancellationToken ct)
    {
        await _identityLock.WaitAsync(ct);
        try
        {
            return await EnsureIdentityLockedAsync(
                session,
                teamId,
                allowCertificateCreation,
                ct);
        }
        finally
        {
            _identityLock.Release();
        }
    }

    private async Task<byte[]> EnsureIdentityLockedAsync(
        AppleSession session,
        string teamId,
        bool allowCertificateCreation,
        CancellationToken ct)
    {
        string dir = _options.IdentityDirectory ?? Path.Combine(_options.WorkDirectory, "identities");
        string path = Path.Combine(dir, $"{IdentityKey(session.AppleId, teamId)}.p12");
        string pendingKeyPath = path + ".pending.pkcs8";
        if (allowCertificateCreation)
        {
            Directory.CreateDirectory(dir);
            RestrictToOwner(dir, directory: true);
            CleanupStaleTemporaryFiles(dir, Path.GetFileName(path));
        }

        if (File.Exists(path))
        {
            byte[] existing = await File.ReadAllBytesAsync(path, ct);
            if (TryReadCertificateExpiry(existing, out DateTimeOffset notAfter) &&
                notAfter - _time.GetUtcNow() > _options.CertificateRenewLeadTime)
            {
                TryDelete(pendingKeyPath);
                _logger.LogDebug("reusing persisted signing identity (expires {Expiry:o})", notAfter);
                return existing;
            }

            CryptographicOperations.ZeroMemory(existing);

            _logger.LogInformation(
                "persisted signing identity missing or near expiry; checking whether a safe mint is possible");
        }

        if (!allowCertificateCreation)
            throw new OwnerManagedAppleActionRequiredException();

        using DevelopmentKeyPair keyPair = await LoadOrCreatePendingKeyAsync(pendingKeyPath, ct);
        byte[] csrDer = keyPair.CreateCsrDer();
        SigningCertificate certificate = await _portal.EnsureCertificateAsync(session, teamId, csrDer, ct);
        byte[] pkcs12 = keyPair.ExportPkcs12(certificate.CertificateDer, string.Empty);

        try
        {
            await WritePrivateFileAtomicallyAsync(path, pkcs12, ct);
            TryDelete(pendingKeyPath);
            _logger.LogInformation(
                "minted signing certificate serial {Serial} (expires {Expiry:o})",
                certificate.SerialNumber, certificate.ExpiresAt);
            return pkcs12;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(pkcs12);
            throw;
        }
    }

    private static async Task<DevelopmentKeyPair> LoadOrCreatePendingKeyAsync(
        string pendingKeyPath,
        CancellationToken ct)
    {
        if (File.Exists(pendingKeyPath))
        {
            byte[] staged = await File.ReadAllBytesAsync(pendingKeyPath, ct);
            try
            {
                return new DevelopmentKeyPair(staged);
            }
            catch (CryptographicException)
            {
                TryDelete(pendingKeyPath);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(staged);
            }
        }

        var generated = new DevelopmentKeyPair();
        byte[] privateKey = generated.ExportPkcs8PrivateKey();
        try
        {
            // The recovery key must be durable before the request reaches Apple.
            // If the response is lost after Apple issues the certificate, the
            // next attempt reloads this key and reclaims the matching certificate.
            await WritePrivateFileAtomicallyAsync(pendingKeyPath, privateKey, ct);
            return generated;
        }
        catch
        {
            generated.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private static async Task WritePrivateFileAtomicallyAsync(
        string path,
        byte[] contents,
        CancellationToken ct)
    {
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 4096,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            await using (FileStream stream = new(temporaryPath, options))
            {
                await stream.WriteAsync(contents, ct);
                stream.Flush(flushToDisk: true);
            }

            RestrictToOwner(temporaryPath, directory: false);
            File.Move(temporaryPath, path, overwrite: true);
            RestrictToOwner(path, directory: false);
            SyncParentDirectory(path);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static bool TryReadCertificateExpiry(byte[] pkcs12, out DateTimeOffset notAfter)
    {
        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(pkcs12, string.Empty);
            if (!certificate.HasPrivateKey)
            {
                notAfter = default;
                return false;
            }
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // A stale recovery file is harmless once the final identity exists.
        }
        catch (UnauthorizedAccessException)
        {
            // The final owner-only identity remains authoritative.
        }
    }

    private static void CleanupStaleTemporaryFiles(string directory, string identityFileName)
    {
        foreach (string temporaryPath in Directory.EnumerateFiles(
                     directory,
                     $"{identityFileName}.*.tmp",
                     SearchOption.TopDirectoryOnly))
        {
            TryDelete(temporaryPath);
        }
    }

    private static void SyncParentDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        string directory = Path.GetDirectoryName(path)
            ?? throw new IOException("The signing identity has no parent directory.");
        int descriptor = open(directory, flags: 0); // O_RDONLY is portable across Linux and macOS.
        if (descriptor < 0)
            throw new IOException($"Could not open the signing-identity directory for sync (errno {Marshal.GetLastPInvokeError()}).");
        try
        {
            if (fsync(descriptor) != 0)
                throw new IOException($"Could not sync the signing-identity directory (errno {Marshal.GetLastPInvokeError()}).");
        }
        finally
        {
            _ = close(descriptor);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int fsync(int fileDescriptor);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fileDescriptor);
}
