using System.Security.Cryptography.X509Certificates;
using Sideport.DeveloperApi;
using Sideport.DeveloperApi.Tests.Support;

namespace Sideport.DeveloperApi.Tests.Signing;

public class PreparedSigningIdentityTests
{
    [Fact]
    public void Create_ReexportsPasswordlessP12_LoadableWithoutPassword()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sideport-pid-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = TestPkcs12Builder.WriteP12(dir, "the-password");

            using var identity = PreparedSigningIdentity.Create(source, "the-password");

            Assert.True(File.Exists(identity.Pkcs12Path));

            // The prepared p12 opens with an EMPTY password and still carries the key.
            X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(identity.Pkcs12Path, string.Empty);
            Assert.True(cert.HasPrivateKey);
            Assert.Contains("Sideport Test Signing", cert.Subject);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Create_WrongPassword_Throws()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sideport-pid-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = TestPkcs12Builder.WriteP12(dir, "right");
            Assert.ThrowsAny<Exception>(() => PreparedSigningIdentity.Create(source, "wrong"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Dispose_RemovesTransientKeyFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sideport-pid-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = TestPkcs12Builder.WriteP12(dir, "pw");
            string preparedPath;
            using (var identity = PreparedSigningIdentity.Create(source, "pw"))
            {
                preparedPath = identity.Pkcs12Path;
                Assert.True(File.Exists(preparedPath));
            }
            Assert.False(File.Exists(preparedPath));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
