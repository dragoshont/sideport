using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Sideport.Core;
using Sideport.DeveloperApi.GrandSlam;

namespace Sideport.DeveloperApi;

/// <summary>
/// DI registration for the Apple developer-portal stack: the anisette provider,
/// the GrandSlam authentication client (with its own configured
/// <see cref="HttpClient"/>), and the <see cref="IAppleDeveloperPortal"/> facade.
/// </summary>
public static class DeveloperApiServiceCollectionExtensions
{
    /// <summary>
    /// Register the anisette provider + GrandSlam auth + developer portal.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="anisetteBaseUrl">Base URL of the anisette v3 sidecar.</param>
    /// <param name="deviceId">Stable <c>X-Mme-Device-Id</c> UUID for this instance.</param>
    /// <param name="allowInsecureTls">
    /// When <see langword="true"/>, disables TLS certificate validation on the
    /// GrandSlam client (for debugging through an intercepting proxy only).
    /// Defaults to <see langword="false"/> — production validates Apple's cert
    /// chain normally. The SRP M2 check is an additional server-authenticity
    /// proof, not a substitute for TLS.
    /// </param>
    public static IServiceCollection AddAppleDeveloperPortal(
        this IServiceCollection services, Uri anisetteBaseUrl, string deviceId,
        bool allowInsecureTls = false)
    {
        services.AddSingleton(new GrandSlamClientOptions { DeviceId = deviceId });

        services.AddHttpClient<IAnisetteProvider, ContainerAnisetteProvider>(
            c => c.BaseAddress = anisetteBaseUrl);

        // GrandSlam (gsa.apple.com) is served from Apple's PRIVATE PKI — its
        // intermediate CA isn't publicly trusted, so ordinary validation fails.
        // By default Sideport pins Apple's root CA and validates the chain
        // against it (real server authentication, defeats MITM), rather than
        // disabling verification like the rest of the ecosystem. The insecure
        // opt-out exists only for proxy-based protocol debugging/capture.
        services.AddHttpClient<GrandSlamClient>()
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();
                if (allowInsecureTls)
                {
                    handler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }
                else
                {
                    handler.ServerCertificateCustomValidationCallback =
                        (_, cert, chain, _) => AppleCaPinning.Validate(cert, chain);
                }
                return handler;
            });

        services.AddSingleton<IAppleDeveloperPortal>(sp =>
            new AppleDeveloperPortal(sp.GetRequiredService<GrandSlamClient>()));
        services.AddSingleton<ISigningIdentityProvider, PortalSigningIdentityProvider>();
        return services;
    }
}
