using System.Net;

namespace Sideport.Devices.Tests;

/// <summary>
/// Unit coverage for <see cref="NetimobiledeviceBackend"/> helpers that have no
/// device dependency — chiefly turning the muxer's already-decoded
/// <c>NetworkAddress</c> bytes into the IP the Wi-Fi direct-TCP lockdown path
/// connects to. (The device-touching paths are covered by the host integration
/// gate.)
/// </summary>
public class NetimobiledeviceBackendTests
{
    [Fact]
    public void DecodeNetworkAddress_Ipv4_ReturnsDottedQuad() =>
        // Netimobiledevice already decodes the sockaddr to the 4 IPv4 octets.
        Assert.Equal("192.168.1.153", NetimobiledeviceBackend.DecodeNetworkAddress([192, 168, 1, 153]));

    [Fact]
    public void DecodeNetworkAddress_Ipv6Global_ReturnsCompressedAddress() =>
        // …and to the 16 IPv6 address bytes for AF_INET6.
        Assert.Equal("2001:db8::1",
            NetimobiledeviceBackend.DecodeNetworkAddress(IPAddress.Parse("2001:db8::1").GetAddressBytes()));

    [Fact]
    public void DecodeNetworkAddress_Ipv6LinkLocal_IsUnusable() =>
        // fe80::/10 needs a scope id the pod cannot supply.
        Assert.Null(NetimobiledeviceBackend.DecodeNetworkAddress(IPAddress.Parse("fe80::1").GetAddressBytes()));

    [Fact]
    public void DecodeNetworkAddress_Null_ReturnsNull() =>
        Assert.Null(NetimobiledeviceBackend.DecodeNetworkAddress(null));

    [Fact]
    public void DecodeNetworkAddress_EmptyOrOddLength_ReturnsNull()
    {
        Assert.Null(NetimobiledeviceBackend.DecodeNetworkAddress([]));        // USB device / unset
        Assert.Null(NetimobiledeviceBackend.DecodeNetworkAddress([1, 2, 3])); // not 4 or 16
    }
}
