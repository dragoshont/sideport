using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Claunia.PropertyList;

namespace Sideport.DeveloperApi.Tests.Support;

/// <summary>
/// Builds real <c>.mobileprovision</c> fixtures: an Apple-shaped property list
/// wrapped in a genuine CMS/PKCS#7 SignedData envelope (signed by a throwaway
/// self-signed cert), so the parser is exercised against the actual on-disk
/// format rather than a bare plist.
/// </summary>
internal static class TestMobileProvisionBuilder
{
    public static byte[] Build(
        string name = "Sideport Test Profile",
        DateTimeOffset? expiration = null,
        IReadOnlyList<string>? devices = null,
        string teamName = "Test Team",
        string teamId = "ABCDE12345",
        string? applicationIdentifier = null,
        bool wrapInCms = true)
    {
        DateTimeOffset exp = expiration ?? DateTimeOffset.UtcNow.AddDays(7);

        var dict = new NSDictionary();
        dict.Add("Name", name);
        dict.Add("AppIDName", name + " App");
        dict.Add("TeamName", teamName);
        dict.Add("ExpirationDate", new NSDate(exp.UtcDateTime));
        dict.Add("TeamIdentifier", new NSArray(new NSString(teamId)));

        if (applicationIdentifier is not null)
        {
            var entitlements = new NSDictionary();
            entitlements.Add("application-identifier", applicationIdentifier);
            dict.Add("Entitlements", entitlements);
        }

        if (devices is { Count: > 0 })
        {
            var deviceNodes = devices.Select(d => (NSObject)new NSString(d)).ToArray();
            dict.Add("ProvisionedDevices", new NSArray(deviceNodes));
        }

        byte[] plistBytes = Encoding.UTF8.GetBytes(dict.ToXmlPropertyList());
        if (!wrapInCms)
            return plistBytes;

        using X509Certificate2 cert = CreateSelfSigned();
        var signed = new SignedCms(new ContentInfo(plistBytes));
        signed.ComputeSignature(new CmsSigner(cert) { IncludeOption = X509IncludeOption.EndCertOnly });
        return signed.Encode();
    }

    private static X509Certificate2 CreateSelfSigned()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Sideport Test", rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }
}
