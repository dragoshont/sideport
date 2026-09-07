using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Sideport.DeveloperApi.GrandSlam;

internal static class GrandSlamTransport
{
    public static SocketsHttpHandler CreateHandler(bool allowInsecureTls)
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.Zero,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            },
        };

        handler.SslOptions.RemoteCertificateValidationCallback = allowInsecureTls
            ? static (_, _, _, _) => true
            : static (_, certificate, chain, errors) =>
            {
                if (certificate is null)
                    return false;

                using X509Certificate2 leaf =
                    X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                return AppleCaPinning.Validate(leaf, chain, errors);
            };

        return handler;
    }
}
