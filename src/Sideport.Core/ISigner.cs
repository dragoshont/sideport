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

/// <summary>
/// Inputs for a single re-sign operation. The PKCS#12 password is carried here
/// but excluded from <see cref="ToString"/> so it never leaks into logs.
/// </summary>
public sealed record SignRequest
{
    public SignRequest(
        string inputIpaPath,
        string outputIpaPath,
        string signingCertificatePkcs12Path,
        string provisioningProfilePath,
        string signingCertificatePassword = "")
    {
        InputIpaPath = inputIpaPath;
        OutputIpaPath = outputIpaPath;
        SigningCertificatePkcs12Path = signingCertificatePkcs12Path;
        ProvisioningProfilePath = provisioningProfilePath;
        SigningCertificatePassword = signingCertificatePassword;
    }

    /// <summary>Path to the input (unsigned or stale) IPA.</summary>
    public string InputIpaPath { get; init; }

    /// <summary>Path the signed IPA is written to.</summary>
    public string OutputIpaPath { get; init; }

    /// <summary>Path to the signing identity as a PKCS#12 (.p12) file.</summary>
    public string SigningCertificatePkcs12Path { get; init; }

    /// <summary>Path to the provisioning profile (.mobileprovision).</summary>
    public string ProvisioningProfilePath { get; init; }

    /// <summary>Password protecting the PKCS#12 file (may be empty).</summary>
    public string SigningCertificatePassword { get; init; }

    // Redact the password from the record's generated ToString.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"{nameof(InputIpaPath)} = {InputIpaPath}, ");
        builder.Append($"{nameof(OutputIpaPath)} = {OutputIpaPath}, ");
        builder.Append($"{nameof(SigningCertificatePkcs12Path)} = {SigningCertificatePkcs12Path}, ");
        builder.Append($"{nameof(ProvisioningProfilePath)} = {ProvisioningProfilePath}, ");
        builder.Append($"{nameof(SigningCertificatePassword)} = ***");
        return true;
    }
}

/// <summary>Outcome of a re-sign operation.</summary>
public sealed record SignResult(
    bool Success,
    string OutputIpaPath,
    string? BundleId,
    string? Error);
