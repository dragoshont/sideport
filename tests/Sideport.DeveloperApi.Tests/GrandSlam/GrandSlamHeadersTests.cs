using Sideport.Core;
using Sideport.DeveloperApi.GrandSlam;

namespace Sideport.DeveloperApi.Tests.GrandSlam;

public class GrandSlamHeadersTests
{
    private static AnisetteHeaders Sample() => new(
        MachineId: "MACHINE",
        OneTimePassword: "OTP",
        RoutingInfo: "17106176",
        LocalUserId: "LOCALUSER",
        ClientTime: new DateTimeOffset(2026, 6, 7, 12, 30, 45, TimeSpan.Zero));

    [Fact]
    public void BuildHeaders_IncludesAnisetteAndDeviceFields()
    {
        var headers = GrandSlamHeaders.BuildHeaders(Sample(), "DEVICE-ID");

        Assert.Equal("OTP", headers["X-Apple-I-MD"]);
        Assert.Equal("MACHINE", headers["X-Apple-I-MD-M"]);
        Assert.Equal("LOCALUSER", headers["X-Apple-I-MD-LU"]);
        Assert.Equal("17106176", headers["X-Apple-I-MD-RINFO"]);
        Assert.Equal("DEVICE-ID", headers["X-Mme-Device-Id"]);
        Assert.Equal("2026-06-07T12:30:45Z", headers["X-Apple-I-Client-Time"]);
    }

    [Fact]
    public void BuildHeaders_WithoutClientInfo_OmitsClientInfoFields()
    {
        var headers = GrandSlamHeaders.BuildHeaders(Sample(), "DEVICE-ID");
        Assert.False(headers.ContainsKey("X-Mme-Client-Info"));
    }

    [Fact]
    public void BuildHeaders_WithClientInfo_AddsClientInfoFields()
    {
        var headers = GrandSlamHeaders.BuildHeaders(Sample(), "DEVICE-ID", includeClientInfo: true);

        Assert.True(headers.ContainsKey("X-Mme-Client-Info"));
        Assert.Equal("com.apple.gs.xcode.auth", headers["X-Apple-App-Info"]);
    }

    [Fact]
    public void BuildHeaders_EmptyRoutingInfo_FallsBackToDefault()
    {
        var anisette = Sample() with { RoutingInfo = "" };
        var headers = GrandSlamHeaders.BuildHeaders(anisette, "DEVICE-ID");
        Assert.Equal("17106176", headers["X-Apple-I-MD-RINFO"]);
    }

    [Fact]
    public void BuildHeaders_AnisetteDeviceId_OverridesConfiguredFallback()
    {
        // Trust inheritance: when anisette surfaces the device identity it is
        // provisioned for, Sideport must send THAT (not the configured fallback)
        // so Apple recognizes a trusted device and skips 2FA.
        var anisette = Sample() with { DeviceId = "BAC92E5F-TRUSTED" };
        var headers = GrandSlamHeaders.BuildHeaders(anisette, "config-fallback-id");
        Assert.Equal("BAC92E5F-TRUSTED", headers["X-Mme-Device-Id"]);
    }

    [Fact]
    public void BuildHeaders_NoAnisetteDeviceId_UsesConfiguredFallback()
    {
        var headers = GrandSlamHeaders.BuildHeaders(Sample(), "config-fallback-id");
        Assert.Equal("config-fallback-id", headers["X-Mme-Device-Id"]);
    }

    [Fact]
    public void BuildHeaders_AnisetteClientInfo_OverridesBuiltIn()
    {
        var anisette = Sample() with { ClientInfo = "<MacBookPro13,2> <macOS;13.1;22C65>" };
        var headers = GrandSlamHeaders.BuildHeaders(anisette, "DEVICE-ID", includeClientInfo: true);
        Assert.Equal("<MacBookPro13,2> <macOS;13.1;22C65>", headers["X-Mme-Client-Info"]);
    }

    [Fact]
    public void BuildCpd_IncludesBootstrapFlagsAndHeaders()
    {
        var cpd = GrandSlamHeaders.BuildCpd(Sample(), "DEVICE-ID");

        Assert.Equal(true, cpd["bootstrap"]);
        Assert.Equal(false, cpd["pbe"]);
        Assert.Equal("iCloud", cpd["svct"]);
        Assert.Equal("OTP", cpd["X-Apple-I-MD"]); // headers merged in
    }
}
