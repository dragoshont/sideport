using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sideport.DeveloperApi.DeveloperServices;

/// <summary>
/// A development RSA key pair plus the PKCS#10 CSR Apple's
/// <c>submitDevelopmentCSR</c> endpoint signs into a certificate. The private
/// key never leaves the process: this type generates the CSR, and once Apple
/// returns the matching certificate it assembles the two into a PKCS#12 the
/// signer can use.
///
/// Equivalent to AltSign's <c>CertificateRequest</c>, reimplemented with the
/// .NET BCL (<see cref="RSA"/> + <see cref="CertificateRequest"/>) — no Apple or
/// AGPL code involved.
/// </summary>
internal sealed class DevelopmentKeyPair : IDisposable
{
    private readonly RSA _rsa;

    public DevelopmentKeyPair()
    {
        _rsa = RSA.Create(2048);
    }

    /// <summary>The PKCS#10 certificate-signing request, DER-encoded.</summary>
    public byte[] CreateCsrDer()
    {
        var request = new CertificateRequest(
            "CN=Sideport Development", _rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSigningRequest();
    }

    /// <summary>
    /// Combine Apple's returned certificate (DER) with this key pair's private
    /// key into a password-protected PKCS#12.
    /// </summary>
    public byte[] ExportPkcs12(byte[] certificateDer, string password)
    {
        using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(certificateDer);
        using X509Certificate2 withKey = certificate.CopyWithPrivateKey(_rsa);
        return withKey.Export(X509ContentType.Pkcs12, password);
    }

    public void Dispose() => _rsa.Dispose();
}
