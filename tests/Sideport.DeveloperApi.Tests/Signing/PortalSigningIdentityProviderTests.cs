using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sideport.Core;
using Sideport.DeveloperApi.DeveloperServices;
using Sideport.DeveloperApi.GrandSlam;
using Sideport.DeveloperApi.Tests.Support;

namespace Sideport.DeveloperApi.Tests.Signing;

/// <summary>
/// Tests <see cref="PortalSigningIdentityProvider"/> — the device-register →
/// ensure-cert → ensure-profile orchestration and, crucially, the free-tier
/// discipline of persisting the certificate and reusing it across refreshes
/// rather than minting a new one each time (which would revoke the old).
/// </summary>
public class PortalSigningIdentityProviderTests : IDisposable
{
    private const string TeamId = "ABCDE12345";
    private const string BundleId = "com.example.diceroll";
    private const string Udid = "00008140-001A41390242801C";

    private readonly string _workDir = Path.Combine(
        Path.GetTempPath(), "sideport-test-" + Guid.NewGuid().ToString("N"));

    private (PortalSigningIdentityProvider provider, FakeDeveloperServicesHandler handler) Build(
        FakeTimeProvider time)
    {
        var handler = new FakeDeveloperServicesHandler(TeamId);
        var anisette = new StubAnisetteProvider();
        var options = new GrandSlamClientOptions { DeviceId = "dev-portal-uuid" };
        var dev = new DeveloperServicesClient(
            new HttpClient(handler), anisette, options, NullLogger<DeveloperServicesClient>.Instance);
        var grandSlam = new GrandSlamClient(
            new HttpClient(handler), anisette, options, NullLogger<GrandSlamClient>.Instance);
        var portal = new AppleDeveloperPortal(grandSlam, dev);

        var provider = new PortalSigningIdentityProvider(
            portal,
            new PortalSigningOptions { WorkDirectory = _workDir },
            NullLogger<PortalSigningIdentityProvider>.Instance,
            time);
        return (provider, handler);
    }

    private static AppleSession Session() =>
        new("dev@example.com", "000123-04-deadbeef", "Test Person", new byte[32])
        {
            IdmsToken = "test-idms-token",
        };

    [Fact]
    public async Task Prepare_ProducesUsablePkcs12AndProfileOnDisk()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (PortalSigningIdentityProvider provider, _) = Build(time);

        PreparedSigningInputs inputs = await provider.PrepareAsync(Session(), TeamId, BundleId, Udid);

        Assert.True(File.Exists(inputs.Pkcs12Path));
        Assert.True(File.Exists(inputs.ProvisioningProfilePath));
        // The materialized identity is password-less (no secret on the signer argv).
        Assert.Equal(string.Empty, inputs.Pkcs12Password);
        Assert.True(inputs.ExpiresAt > time.GetUtcNow());
    }

    [Fact]
    public async Task Prepare_ReusesCertificateAcrossRefreshes()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (PortalSigningIdentityProvider provider, FakeDeveloperServicesHandler handler) = Build(time);

        await provider.PrepareAsync(Session(), TeamId, BundleId, Udid);
        await provider.PrepareAsync(Session(), TeamId, BundleId, Udid);

        // The CSR/cert mint happens ONCE; the second refresh reuses the persisted
        // identity (the cert is far from expiry). This is the cert-revocation guard.
        int mints = handler.Requests.Count(r => r.Action == "ios/submitDevelopmentCSR.action");
        Assert.Equal(1, mints);
        // But the profile (weekly expiry) is re-downloaded every refresh.
        Assert.Equal(2, handler.Requests.Count(
            r => r.Action == "ios/downloadTeamProvisioningProfile.action"));
    }

    [Fact]
    public async Task Prepare_ReMintsWhenCertificateNearExpiry()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (PortalSigningIdentityProvider provider, FakeDeveloperServicesHandler handler) = Build(time);

        await provider.PrepareAsync(Session(), TeamId, BundleId, Udid);
        // Jump beyond (cert lifetime − renew lead): the persisted cert is now
        // within the renewal window, so the next prepare mints a fresh one.
        time.Advance(TimeSpan.FromDays(360));
        await provider.PrepareAsync(Session(), TeamId, BundleId, Udid);

        int mints = handler.Requests.Count(r => r.Action == "ios/submitDevelopmentCSR.action");
        Assert.Equal(2, mints);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort */ }
    }
}
