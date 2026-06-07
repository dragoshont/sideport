using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sideport.Core;

namespace Sideport.Devices;

/// <summary>DI registration for the device-control plane.</summary>
public static class DevicesServiceCollectionExtensions
{
    /// <summary>
    /// Register the real Netimobiledevice-backed <see cref="IDeviceController"/>.
    /// </summary>
    public static IServiceCollection AddDeviceController(this IServiceCollection services)
    {
        services.AddSingleton<IDeviceBackend>(sp =>
            new NetimobiledeviceBackend(sp.GetService<ILogger<NetimobiledeviceBackend>>()));
        services.AddSingleton<IDeviceController>(sp =>
            new NetimobiledeviceController(
                sp.GetRequiredService<IDeviceBackend>(),
                sp.GetService<ILogger<NetimobiledeviceController>>()));
        return services;
    }
}
