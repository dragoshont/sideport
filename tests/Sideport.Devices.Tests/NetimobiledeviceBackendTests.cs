using System.Net;
using Netimobiledevice.Exceptions;
using Netimobiledevice.Lockdown;
using Netimobiledevice.Lockdown.Pairing;
using Netimobiledevice.Plist;
using Sideport.Core;
using Sideport.DeveloperApi.Packaging;

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
        Assert.Equal("10.0.0.42", NetimobiledeviceBackend.DecodeNetworkAddress([10, 0, 0, 42]));

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

    [Fact]
    public void ClassifyTrustFailure_NotPaired_IsUntrustedWithoutRawError()
    {
        (string state, string reason) =
            NetimobiledeviceBackend.ClassifyTrustFailure(new NotPairedException());

        Assert.Equal("untrusted", state);
        Assert.Contains("pairing record", reason);
    }

    [Fact]
    public void ClassifyTrustFailure_PasswordProtected_IsLocked()
    {
        (string state, string reason) = NetimobiledeviceBackend.ClassifyTrustFailure(
            new LockdownException(LockdownError.PasswordProtected));

        Assert.Equal("locked", state);
        Assert.Contains("Unlock", reason);
    }

    [Fact]
    public void ClassifyTrustFailure_UnknownFailure_IsErrorWithoutExceptionMessage()
    {
        const string sensitive = "00008110-0011223344556677";
        (string state, string reason) = NetimobiledeviceBackend.ClassifyTrustFailure(
            new InvalidOperationException($"failed for {sensitive}"));

        Assert.Equal("error", state);
        Assert.DoesNotContain(sensitive, reason);
    }

    [Theory]
    [InlineData(PairingState.PairingDialogResponsePending, "waiting-for-trust")]
    [InlineData(PairingState.Paired, "paired")]
    [InlineData(PairingState.UserDeniedPairing, "denied")]
    [InlineData(PairingState.PasswordProtected, "locked")]
    public void MapPairingProgress_UsesPublicStateVocabulary(PairingState input, string expected)
    {
        Assert.Equal(expected, NetimobiledeviceBackend.MapPairingProgress(input).State);
    }

    [Fact]
    public void WifiPairingNotSupported_IsErrorAndNeverClaimsInstallUsability()
    {
        const string udid = "00008110-0011223344556677";
        DevicePairingResult result = NetimobiledeviceBackend.WifiPairingNotSupported(
            udid,
            DateTimeOffset.Parse("2026-07-11T12:00:00Z"));

        Assert.Equal(DeviceConnection.Wifi, result.Connection);
        Assert.Equal("error", result.TrustState);
        Assert.False(result.UsableForInstall);
        Assert.DoesNotContain(udid, result.TrustReason!);
    }

    [Fact]
    public void ProvisioningProfileBytes_DictionaryNode_RoundTripsAsBareMobileProvision()
    {
        DateTime expiry = new(2027, 8, 10, 10, 41, 36, DateTimeKind.Utc);
        var profile = new DictionaryNode
        {
            { "Name", new StringNode("Aletheia Development") },
            { "ExpirationDate", new DateNode(expiry) },
            { "TeamIdentifier", new ArrayNode { new StringNode("TEAMID") } },
            { "Entitlements", new DictionaryNode {
                { "application-identifier", new StringNode("TEAMID.ro.hont.aletheia") },
            } },
        };

        byte[] bytes = NetimobiledeviceBackend.ProvisioningProfileBytes(profile);
        ProvisioningProfileInfo parsed = MobileProvision.Parse(bytes);

        Assert.Equal("Aletheia Development", parsed.Name);
        Assert.Equal(expiry, parsed.ExpirationDate.UtcDateTime);
        Assert.True(parsed.CoversBundle("ro.hont.aletheia"));
    }

    [Fact]
    public void ProvisioningProfileBytes_DataNode_PreservesExactBytes()
    {
        byte[] original = [0, 1, 2, 3, 255];

        byte[] converted = NetimobiledeviceBackend.ProvisioningProfileBytes(new DataNode(original));

        Assert.Same(original, converted);
    }

    [Fact]
    public void ProvisioningProfileBytes_UnsupportedNode_Throws()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
            NetimobiledeviceBackend.ProvisioningProfileBytes(new StringNode("unsupported")));

        Assert.Contains("String", error.Message);
    }

    [Fact]
    public void NormalizeProfileDate_UsesUtcForLocalAndUnspecifiedValues()
    {
        DateTime local = new(2027, 8, 10, 10, 41, 36, DateTimeKind.Local);
        DateTime unspecified = new(2027, 8, 10, 10, 41, 36, DateTimeKind.Unspecified);

        Assert.Equal(local.ToUniversalTime(), NetimobiledeviceBackend.NormalizeProfileDate(local));
        Assert.Equal(DateTimeKind.Utc, NetimobiledeviceBackend.NormalizeProfileDate(local).Kind);
        Assert.Equal(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc),
                     NetimobiledeviceBackend.NormalizeProfileDate(unspecified));
    }
}
