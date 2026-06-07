using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;
using Sideport.DeveloperApi.DeveloperServices;
using Sideport.DeveloperApi.GrandSlam;
using Sideport.DeveloperApi.Tests.Support;

namespace Sideport.DeveloperApi.Tests.GrandSlam;

/// <summary>
/// Replay-style tests for the P4 developer-services protocol, driving the real
/// <see cref="AppleDeveloperPortal"/> + <see cref="PortalSigningIdentityProvider"/>
/// through a fake <c>developerservices2.apple.com</c> transport. The fake parses
/// the actual request plists and returns the actual response plists, so the
/// request envelope, identity + anisette headers, plist codec, resultCode error
/// envelope, and the CSR → cert → profile flow are all exercised end to end.
/// </summary>
public class DeveloperServicesTests
{
    private const string TeamId = "ABCDE12345";
    private const string BundleId = "com.example.diceroll";
    private const string Udid = "00008140-001A41390242801C";

    private static (AppleDeveloperPortal portal, FakeDeveloperServicesHandler handler, StubAnisetteProvider anisette) Build()
    {
        var handler = new FakeDeveloperServicesHandler(TeamId);
        var anisette = new StubAnisetteProvider();
        var options = new GrandSlamClientOptions { DeviceId = "dev-portal-uuid" };

        // The GrandSlam client is unused by the dev-API methods but the portal
        // requires one; give it a transport that never gets called.
        var grandSlam = new GrandSlamClient(
            new HttpClient(new FailingHandler()), anisette, options,
            NullLogger<GrandSlamClient>.Instance);
        var dev = new DeveloperServicesClient(
            new HttpClient(handler), anisette, options,
            NullLogger<DeveloperServicesClient>.Instance);

        return (new AppleDeveloperPortal(grandSlam, dev), handler, anisette);
    }

    private static AppleSession Session() =>
        new("dev@example.com", "000123-04-deadbeef", "Test Person", new byte[32])
        {
            IdmsToken = "test-idms-token",
        };

    [Fact]
    public async Task ListTeams_ParsesTeams()
    {
        (AppleDeveloperPortal portal, _, _) = Build();

        IReadOnlyList<AppleTeam> teams = await portal.ListTeamsAsync(Session());

        AppleTeam team = Assert.Single(teams);
        Assert.Equal(TeamId, team.TeamId);
        Assert.Equal("Test Team", team.Name);
    }

    [Fact]
    public async Task Request_CarriesIdentityAndEnvelope()
    {
        (AppleDeveloperPortal portal, FakeDeveloperServicesHandler handler, _) = Build();

        await portal.ListTeamsAsync(Session());

        FakeDeveloperServicesHandler.CapturedRequest req = Assert.Single(handler.Requests);
        // Identity headers: raw adsid + IDMS token (NOT the base64 GSA token).
        Assert.Equal("000123-04-deadbeef", req.Headers["X-Apple-I-Identity-Id"]);
        Assert.Equal("test-idms-token", req.Headers["X-Apple-GS-Token"]);
        Assert.Equal("Xcode", req.Headers["User-Agent"]);
        // Request envelope.
        Assert.Equal("XABBG36SBA", req.Body["clientId"].ToString());
        Assert.Equal("QH65B2", req.Body["protocolVersion"].ToString());
        Assert.False(string.IsNullOrEmpty(req.Body["requestId"].ToString()));
    }

    [Fact]
    public async Task RegisterDevice_IsIdempotent()
    {
        (AppleDeveloperPortal portal, FakeDeveloperServicesHandler handler, _) = Build();

        await portal.RegisterDeviceAsync(Session(), TeamId, Udid, "Phone");
        await portal.RegisterDeviceAsync(Session(), TeamId, Udid, "Phone");

        // First call: listDevices (empty) + addDevice. Second call: listDevices
        // sees it already there and does NOT add again.
        int adds = handler.Requests.Count(r => r.Action == "ios/addDevice.action");
        Assert.Equal(1, adds);
    }

    [Fact]
    public async Task EnsureCertificate_SignsCsrAndReturnsCert()
    {
        (AppleDeveloperPortal portal, _, _) = Build();
        using var keyPair = new DevelopmentKeyPair();
        byte[] csr = keyPair.CreateCsrDer();

        SigningCertificate cert = await portal.EnsureCertificateAsync(Session(), TeamId, csr);

        Assert.NotEmpty(cert.CertificateDer);
        Assert.True(cert.ExpiresAt > DateTimeOffset.UtcNow.AddDays(300));
        // The returned cert + the CSR key assemble into a usable PKCS#12.
        byte[] p12 = keyPair.ExportPkcs12(cert.CertificateDer, "pw");
        Assert.NotEmpty(p12);
    }

    [Fact]
    public async Task EnsureProfile_CreatesAppIdThenDownloadsProfile()
    {
        (AppleDeveloperPortal portal, FakeDeveloperServicesHandler handler, _) = Build();

        ProvisioningProfile profile = await portal.EnsureProfileAsync(Session(), TeamId, BundleId);

        Assert.Equal(BundleId, profile.BundleId);
        Assert.NotEmpty(profile.MobileProvision);
        Assert.True(profile.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(1, handler.Requests.Count(r => r.Action == "ios/addAppId.action"));
    }

    [Fact]
    public async Task EnsureProfile_ReusesExistingAppId()
    {
        (AppleDeveloperPortal portal, FakeDeveloperServicesHandler handler, _) = Build();

        await portal.EnsureProfileAsync(Session(), TeamId, BundleId);
        await portal.EnsureProfileAsync(Session(), TeamId, BundleId);

        // The App ID is created once and reused (free-tier quota discipline).
        Assert.Equal(1, handler.Requests.Count(r => r.Action == "ios/addAppId.action"));
        Assert.Equal(2, handler.Requests.Count(
            r => r.Action == "ios/downloadTeamProvisioningProfile.action"));
    }

    [Fact]
    public async Task AppleError_SurfacesResultCode()
    {
        (AppleDeveloperPortal portal, FakeDeveloperServicesHandler handler, _) = Build();
        handler.NextError = ("ios/addAppId.action", 9401, "bundle identifier unavailable");

        DeveloperServicesException ex = await Assert.ThrowsAsync<DeveloperServicesException>(
            () => portal.EnsureProfileAsync(Session(), TeamId, BundleId));

        Assert.Equal(9401, ex.ResultCode);
    }

    /// <summary>A handler that fails if hit — proves the dev-API path never calls GSA.</summary>
    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("GrandSlam transport must not be used by dev-API tests");
    }
}
