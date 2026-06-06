using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sideport.DeveloperApi.Tests.Support;

/// <summary>
/// Creates a throwaway password-protected PKCS#12 signing identity on disk, so
/// the signer's password-handling (the re-export-password-less path) runs
/// against a real encrypted p12 rather than a placeholder file.
/// </summary>
internal static class TestPkcs12Builder
{
    public static string WriteP12(string directory, string password, string fileName = "id.p12")
    {
        Directory.CreateDirectory(directory);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Sideport Test Signing", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        byte[] p12 = cert.Export(X509ContentType.Pkcs12, password);
        string path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, p12);
        return path;
    }
}
