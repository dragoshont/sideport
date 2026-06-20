using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sideport.Core;

namespace Sideport.Orchestrator;

/// <summary>DI registration for the refresh orchestrator + scheduler.</summary>
public static class OrchestratorServiceCollectionExtensions
{
    /// <summary>
    /// Register the app registry, credential provider, session manager, refresh
    /// orchestrator, and (optionally) the background scheduler.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Orchestrator tuning (work dir, lead time, interval).</param>
    /// <param name="runScheduler">
    /// Whether to start the background <see cref="RefreshScheduler"/>. Disable in
    /// tests or when refreshes should be manual-only.
    /// </param>
    public static IServiceCollection AddRefreshOrchestrator(
        this IServiceCollection services,
        OrchestratorOptions? options = null,
        bool runScheduler = true)
    {
        services.AddSingleton(options ?? new OrchestratorOptions());
        services.AddSingleton<IAppRegistry>(sp => new FileAppRegistry(
            sp.GetRequiredService<OrchestratorOptions>().AppRegistryPath));
        // Durable, PVC-backed store for the input IPAs registrations point at, so
        // the scheduler can re-sign unattended after a restart wipes /tmp.
        services.AddSingleton(sp => new IpaStore(
            sp.GetRequiredService<OrchestratorOptions>().IpaStoreDirectory));
        // Default credential source. TryAdd so a host that pre-registers a
        // different IAppleCredentialProvider (e.g. AppleKeychainCredentialProvider
        // when Sideport:Apple:CredentialSource=keychain) wins; env stays the default.
        services.TryAddSingleton<IAppleCredentialProvider, EnvironmentCredentialProvider>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<RefreshOrchestrator>();
        services.AddSingleton<IRefreshOrchestrator>(sp => sp.GetRequiredService<RefreshOrchestrator>());

        if (runScheduler)
            services.AddHostedService(sp => new RefreshScheduler(
                sp.GetRequiredService<IAppRegistry>(),
                sp.GetRequiredService<RefreshOrchestrator>(),
                sp.GetRequiredService<OrchestratorOptions>(),
                sp.GetService<TimeProvider>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<RefreshScheduler>>()));

        return services;
    }
}
