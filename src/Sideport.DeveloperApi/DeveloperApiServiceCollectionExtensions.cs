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

        // GrandSlam talks to gsa.apple.com directly over normal validated TLS.
        // The insecure opt-out exists solely for proxy-based protocol debugging.
        IHttpClientBuilder grandSlam = services.AddHttpClient<GrandSlamClient>();
        if (allowInsecureTls)
        {
            grandSlam.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            });
        }

        services.AddSingleton<IAppleDeveloperPortal>(sp =>
            new AppleDeveloperPortal(sp.GetRequiredService<GrandSlamClient>()));
        return services;
    }
}
