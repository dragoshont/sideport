using System.Net;
using Netimobiledevice.Exceptions;
using Netimobiledevice.Lockdown;
using Netimobiledevice.Lockdown.Pairing;
using Sideport.Core;

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
        Assert.Equal(
            DevicePairingDisposition.Locked,
            NetimobiledeviceBackend.ClassifyTrustFailureDisposition(
                new LockdownException(LockdownError.PasswordProtected)));
    }

    [Fact]
    public void ClassifyTrustFailure_UserDenied_IsTypedDenied()
    {
        Assert.Equal(
            DevicePairingDisposition.Denied,
            NetimobiledeviceBackend.ClassifyTrustFailureDisposition(
                new LockdownException(LockdownError.UserDeniedPairing)));
    }

    [Theory]
    [InlineData(LockdownError.PairingFailed)]
    [InlineData(LockdownError.InvalidHostID)]
    [InlineData(LockdownError.InvalidPairRecord)]
    public void ClassifyTrustFailure_DamagedSavedTrust_RequiresRepair(LockdownError error)
    {
        Assert.Equal(
            DevicePairingDisposition.RepairRequired,
            NetimobiledeviceBackend.ClassifyTrustFailureDisposition(new LockdownException(error)));
    }

    [Fact]
    public void ClassifyTrustFailure_FatalPairing_RequiresRepair()
    {
        (string state, string reason) = NetimobiledeviceBackend.ClassifyTrustFailure(new FatalPairingException());

        Assert.Equal("error", state);
        Assert.Contains("repaired", reason);
        Assert.Equal(
            DevicePairingDisposition.RepairRequired,
            NetimobiledeviceBackend.ClassifyTrustFailureDisposition(new FatalPairingException()));
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
        Assert.Equal(DevicePairingDisposition.UsbRequired, result.Disposition);
        Assert.False(result.UsableForInstall);
        Assert.DoesNotContain(udid, result.TrustReason!);
    }
}
