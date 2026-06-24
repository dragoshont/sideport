using Microsoft.Extensions.Configuration;
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
        services.AddSingleton<DeviceMetrics>();
        services.AddSingleton<IDeviceBackend>(sp =>
            new NetimobiledeviceBackend(
                sp.GetService<ILogger<NetimobiledeviceBackend>>(),
                sp.GetRequiredService<DeviceMetrics>(),
                // Where the host keeps the trusted lockdown pairing records, so the
                // Wi-Fi direct-TCP path can reuse the existing trust. Defaults to
                // /var/lib/lockdown when unset.
                sp.GetService<IConfiguration>()?["Sideport:Devices:PairingRecordsDir"]));
        services.AddSingleton<IDeviceController>(sp =>
            new NetimobiledeviceController(
                sp.GetRequiredService<IDeviceBackend>(),
                sp.GetService<ILogger<NetimobiledeviceController>>(),
                sp.GetRequiredService<DeviceMetrics>()));
        return services;
    }
}
