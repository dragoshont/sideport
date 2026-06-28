using Sideport.DeveloperApi.Packaging;
using Sideport.DeveloperApi.Tests.Support;

namespace Sideport.DeveloperApi.Tests.Packaging;

public class MobileProvisionTests
{
    [Fact]
    public void Parse_CmsWrappedProfile_ReadsFields()
    {
        DateTimeOffset expiry = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
        byte[] profile = TestMobileProvisionBuilder.Build(
            name: "My Profile",
            expiration: expiry,
            devices: ["00008110-0011223344556677"],
            teamName: "Acme",
            teamId: "TEAM123456");

        ProvisioningProfileInfo info = MobileProvision.Parse(profile);

        Assert.Equal("My Profile", info.Name);
        Assert.Equal("Acme", info.TeamName);
        Assert.Equal(expiry, info.ExpirationDate);
        Assert.Equal(["TEAM123456"], info.TeamIdentifiers);
        Assert.Equal(["00008110-0011223344556677"], info.ProvisionedDeviceIds);
    }

    [Fact]
    public void Parse_BareplistProfile_StillReadsFields()
    {
        // Some tooling hands us the inner plist without the CMS envelope; the
        // span-extraction fallback must still parse it.
        byte[] bare = TestMobileProvisionBuilder.Build(name: "Bare", wrapInCms: false);

        ProvisioningProfileInfo info = MobileProvision.Parse(bare);
        Assert.Equal("Bare", info.Name);
    }

    [Fact]
    public void IsExpired_RespectsExpirationDate()
    {
        byte[] profile = TestMobileProvisionBuilder.Build(
            expiration: DateTimeOffset.UtcNow.AddDays(7));
        ProvisioningProfileInfo info = MobileProvision.Parse(profile);

        Assert.False(info.IsExpired(DateTimeOffset.UtcNow));
        Assert.True(info.IsExpired(DateTimeOffset.UtcNow.AddDays(8)));
        Assert.True(info.TimeUntilExpiry(DateTimeOffset.UtcNow) > TimeSpan.FromDays(6));
    }

    [Fact]
    public void Parse_NoDevices_YieldsEmptyDeviceList()
    {
        byte[] profile = TestMobileProvisionBuilder.Build(devices: null);
        ProvisioningProfileInfo info = MobileProvision.Parse(profile);
        Assert.Empty(info.ProvisionedDeviceIds);
    }

    [Fact]
    public void ExtractPlist_Garbage_Throws()
    {
        Assert.Throws<FormatException>(() => MobileProvision.Parse("not a profile"u8.ToArray()));
    }

    [Theory]
    [InlineData("TEAM123456.com.example.app", "com.example.app", true)]
    [InlineData("TEAM123456.com.example.app", "com.example.other", false)]
    [InlineData("TEAM123456.com.example.*", "com.example.app", true)]
    [InlineData("TEAM123456.com.example.*", "com.other.app", false)]
    [InlineData("TEAM123456.*", "literally.anything", true)]
    public void CoversBundle_HonorsExplicitPrefixAndFullWildcard(string appId, string bundleId, bool expected)
    {
        byte[] profile = TestMobileProvisionBuilder.Build(applicationIdentifier: appId);
        ProvisioningProfileInfo info = MobileProvision.Parse(profile);
        Assert.Equal(expected, info.CoversBundle(bundleId));
    }

    [Fact]
    public void ProfileBundlePattern_StripsTeamPrefix()
    {
        byte[] profile = TestMobileProvisionBuilder.Build(applicationIdentifier: "TEAM123456.com.example.app");
        ProvisioningProfileInfo info = MobileProvision.Parse(profile);
        Assert.Equal("com.example.app", info.ProfileBundlePattern);
    }
}
