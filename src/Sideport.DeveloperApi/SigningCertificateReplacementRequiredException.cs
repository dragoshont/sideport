namespace Sideport.DeveloperApi;

/// <summary>
/// A development certificate exists but Sideport does not hold its private key.
/// The caller must use an explicit, impact-confirmed cutover instead of silently
/// revoking another signer's certificate.
/// </summary>
public sealed class SigningCertificateReplacementRequiredException(int certificateCount)
    : Exception(
        $"Sideport found {certificateCount} existing Apple development certificate(s). " +
        "No Apple certificate or device registration was changed; explicit certificate replacement is required."),
      Sideport.Core.IStructuredRefreshFailure
{
    public int CertificateCount { get; } = certificateCount;
    public string ErrorCode => "signing-cutover-required";
    public string SafeMessage =>
        "Review the existing Apple development certificates before Sideport creates its signing identity.";
}
