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
    /// Inspect the persisted identity for one Apple account/team without
    /// preparing signing inputs or making an Apple mutation. Providers that do
    /// not persist identities use the conservative <c>missing</c> default.
    /// </summary>
    Task<SigningIdentityInspection> InspectAsync(
        string appleId,
        string teamId,
        CancellationToken ct = default) =>
        Task.FromResult(new SigningIdentityInspection("missing", null, null));

    /// <summary>
    /// Replace the persisted identity after an Owner confirmed the exact Apple
    /// certificate IDs. Implementations must re-read inventory while holding
    /// the same gate used by signing and revoke only that exact set.
    /// </summary>
    Task<SigningIdentityInspection> ReplaceAsync(
        AppleSession session,
        string teamId,
        IReadOnlyList<string> acknowledgedCertificateIds,
        CancellationToken ct = default) =>
        Task.FromException<SigningIdentityInspection>(new NotSupportedException("Signer replacement is unavailable."));

    /// <summary>
    /// Run exact replacement and the caller's durable lineage finalization
    /// under the same signer gate. Recovery may reuse an already-persisted
    /// replacement identity but must never expand the acknowledged revoke set.
    /// </summary>
    Task<SigningIdentityInspection> ReplaceAndFinalizeAsync(
        AppleSession session,
        string teamId,
        IReadOnlyList<string> acknowledgedCertificateIds,
        bool allowPersistedIdentityRecovery,
        Func<SigningIdentityInspection, CancellationToken, Task> finalizeAsync,
        CancellationToken ct = default) =>
        Task.FromException<SigningIdentityInspection>(new NotSupportedException("Signer cutover is unavailable."));

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

    /// <summary>
    /// Prepare signing inputs under an explicit certificate-creation policy.
    /// Implementations must not generate a key, request a certificate, or
    /// replace an identity when <paramref name="allowCertificateCreation"/> is
    /// false. The conservative default denies that restricted path.
    /// </summary>
    Task<PreparedSigningInputs> PrepareAsync(
        AppleSession session,
        string teamId,
        string bundleId,
        string deviceUdid,
        bool allowCertificateCreation,
        CancellationToken ct = default) =>
        allowCertificateCreation
            ? PrepareAsync(session, teamId, bundleId, deviceUdid, ct)
            : Task.FromException<PreparedSigningInputs>(
                new OwnerManagedAppleActionRequiredException());
}

/// <summary>
/// A Family-triggered operation reached an Apple authentication or signing
/// identity change that only the Sideport Owner may authorize.
/// </summary>
public sealed class OwnerManagedAppleActionRequiredException : Exception, IStructuredRefreshFailure
{
    public OwnerManagedAppleActionRequiredException()
        : base("An active Sideport Owner must prepare the Apple signing authority before this operation can continue.")
    {
    }

    public string ErrorCode => "owner-action-required";

    public string SafeMessage =>
        "Ask the home Owner to prepare Apple signing before trying this app again.";
}

/// <summary>Read-only persisted signer state used by install preflight.</summary>
public sealed record SigningIdentityInspection(
    string State,
    DateTimeOffset? ExpiresAt,
    string? SerialSuffix);

public sealed class SigningReplacementInventoryChangedException : Exception
{
    public SigningReplacementInventoryChangedException()
        : base("Apple's development-certificate inventory changed. Run signing preflight again.") { }
}

/// <summary>
/// Ready-to-use signing inputs materialized on disk, plus the resulting signing
/// expiry. The owner is responsible for disposing transient files.
/// </summary>
public sealed record PreparedSigningInputs(
    string Pkcs12Path,
    string Pkcs12Password,
    string ProvisioningProfilePath,
    DateTimeOffset ExpiresAt) : IDisposable
{
    public void Dispose()
    {
        TryDelete(Pkcs12Path);
        TryDelete(ProvisioningProfilePath);

        string? identityDirectory = Path.GetDirectoryName(Pkcs12Path);
        string? profileDirectory = Path.GetDirectoryName(ProvisioningProfilePath);
        try
        {
            if (!string.IsNullOrWhiteSpace(identityDirectory) &&
                string.Equals(identityDirectory, profileDirectory, StringComparison.Ordinal) &&
                Directory.Exists(identityDirectory) &&
                !Directory.EnumerateFileSystemEntries(identityDirectory).Any())
            {
                Directory.Delete(identityDirectory);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
