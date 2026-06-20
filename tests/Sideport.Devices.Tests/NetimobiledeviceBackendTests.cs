using System.Net;

namespace Sideport.Devices.Tests;

/// <summary>
/// Unit coverage for <see cref="NetimobiledeviceBackend"/> helpers that have no
/// device dependency — chiefly decoding the muxer's raw <c>NetworkAddress</c>
/// sockaddr, which selects the IP the Wi-Fi direct-TCP lockdown path connects to.
/// (The device-touching paths are covered by the host integration gate.)
/// </summary>
public class NetimobiledeviceBackendTests
{
    [Fact]
    public void DecodeNetworkAddress_Ipv4_ReturnsDottedQuad()
    {
        // BSD sockaddr_in: [0]=len, [1]=AF_INET(2), [2..4]=port, [4..8]=addr.
        byte[] sa = [16, 2, 0xF2, 0x7E, 192, 168, 1, 153, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal("192.168.1.153", NetimobiledeviceBackend.DecodeNetworkAddress(sa));
    }

    [Fact]
    public void DecodeNetworkAddress_Ipv6Global_ReturnsCompressedAddress()
    {
        // BSD sockaddr_in6: [0]=len, [1]=AF_INET6(30), [2..4]=port,
        // [4..8]=flowinfo, [8..24]=addr, [24..28]=scope.
        byte[] sa = new byte[28];
        sa[0] = 28;
        sa[1] = 30;
        IPAddress.Parse("2001:db8::1").GetAddressBytes().CopyTo(sa, 8);
        Assert.Equal("2001:db8::1", NetimobiledeviceBackend.DecodeNetworkAddress(sa));
    }

    [Fact]
    public void DecodeNetworkAddress_Ipv6LinkLocal_IsUnusable()
    {
        // fe80::/10 needs a scope id the pod cannot supply.
        byte[] sa = new byte[28];
        sa[0] = 28;
        sa[1] = 30;
        IPAddress.Parse("fe80::1").GetAddressBytes().CopyTo(sa, 8);
        Assert.Null(NetimobiledeviceBackend.DecodeNetworkAddress(sa));
    }

    [Fact]
    public void DecodeNetworkAddress_Null_ReturnsNull() =>
        Assert.Null(NetimobiledeviceBackend.DecodeNetworkAddress(null));

    [Fact]
    public void DecodeNetworkAddress_TooShort_ReturnsNull() =>
        Assert.Null(NetimobiledeviceBackend.DecodeNetworkAddress([16, 2, 0, 0]));

    [Fact]
    public void DecodeNetworkAddress_UnknownFamily_ReturnsNull()
    {
        byte[] sa = [16, 99, 0, 0, 1, 2, 3, 4, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Null(NetimobiledeviceBackend.DecodeNetworkAddress(sa));
    }
}
