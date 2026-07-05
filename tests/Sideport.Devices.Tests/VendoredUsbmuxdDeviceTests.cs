using System.Runtime.InteropServices;
using Netimobiledevice.Plist;
using Netimobiledevice.Usbmuxd;

namespace Sideport.Devices.Tests;

/// <summary>
/// Regression coverage for the vendored Netimobiledevice patch
/// (<c>vendor/Netimobiledevice/VENDOR.md</c>, issue #4): the usbmux sockaddr
/// address-family byte is at index <b>[0]</b> on Linux/Windows (16-bit
/// <c>sin_family</c>, no <c>sin_len</c>) and <b>[1]</b> on macOS/BSD
/// (<c>{ sin_len; sin_family; }</c>). <c>usbmuxd</c>/<c>netmuxd</c> run on Linux,
/// so on Linux (CI + the deployed pod) this exercises the patched byte-[0] path
/// that previously threw <see cref="NotImplementedException"/> ("Network address
/// is not supported"). The sockaddr is built for the CURRENT OS so the parser is
/// validated wherever the suite runs.
/// </summary>
public class VendoredUsbmuxdDeviceTests
{
    private static byte[] Ipv4Sockaddr(byte a, byte b, byte c, byte d)
    {
        byte[] sa = new byte[128]; // sockaddr_storage, as the muxer actually sends
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            sa[0] = 0x10; // sin_len (BSD)
            sa[1] = 0x02; // sin_family = AF_INET
        }
        else
        {
            sa[0] = 0x02; // AF_INET, low byte of the 16-bit family (Linux/Windows)
            sa[1] = 0x00;
        }
        sa[4] = a; sa[5] = b; sa[6] = c; sa[7] = d; // sin_addr (same offset everywhere)
        return sa;
    }

    private static UsbmuxdDevice NetworkDevice(byte[] sockaddr) =>
        new(new IntegerNode(1), new DictionaryNode
        {
            { "SerialNumber", new StringNode("00008110-0011223344556677") },
            { "ConnectionType", new StringNode("Network") },
            { "NetworkAddress", new DataNode(sockaddr) },
            { "InterfaceIndex", new IntegerNode(5) },
        });

    [Fact]
    public void Ipv4NetworkDevice_ParsesToTheAddress()
    {
        // A network-device sockaddr the muxer delivers (synthetic address).
        UsbmuxdDevice device = NetworkDevice(Ipv4Sockaddr(10, 0, 0, 42));

        Assert.Equal(UsbmuxdConnectionType.Network, device.ConnectionType);
        // On Linux this is the patched byte-[0] path; pre-fix it threw
        // NotImplementedException("Network address is not supported").
        Assert.Equal(new byte[] { 10, 0, 0, 42 }, device.NetworkAddress);
    }

    [Fact]
    public void UsbDevice_HasNoNetworkAddress()
    {
        UsbmuxdDevice device = new(new IntegerNode(2), new DictionaryNode
        {
            { "SerialNumber", new StringNode("00008110-0011223344556677") },
            { "ConnectionType", new StringNode("USB") },
        });

        Assert.Equal(UsbmuxdConnectionType.Usb, device.ConnectionType);
        Assert.Empty(device.NetworkAddress);
    }
}
