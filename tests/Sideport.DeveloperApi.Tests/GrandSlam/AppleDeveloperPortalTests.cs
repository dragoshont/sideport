using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;
using Sideport.DeveloperApi.DeveloperServices;
using Sideport.DeveloperApi.GrandSlam;
using Sideport.DeveloperApi.Tests.Support;
using Sideport.GrandSlam;

namespace Sideport.DeveloperApi.Tests.GrandSlam;

/// <summary>
/// Tests the <see cref="IAppleDeveloperPortal"/> facade end-to-end through the
/// fake transport, validating the seam-level result types and the 2FA + retry
/// contract the orchestrator (P6) depends on.
/// </summary>
public class AppleDeveloperPortalTests
{
    private const string Username = "dev@example.com";
    private const string Password = "swordfish-1234";
    private static readonly byte[] Salt = Enumerable.Range(10, 32).Select(i => (byte)i).ToArray();
    private const int Iterations = 1500;

    private static (IAppleDeveloperPortal portal, FakeGrandSlamHandler handler) Build(
        FakeGrandSlamHandler.TwoFactorMode twoFactor = FakeGrandSlamHandler.TwoFactorMode.None)
    {
        byte[] passwordKey = GrandSlamCrypto.DerivePasswordKey(Password, Salt, Iterations);
        var handler = new FakeGrandSlamHandler(Username, passwordKey, Salt, Iterations, twoFactor);
        var anisette = new StubAnisetteProvider();
        var options = new GrandSlamClientOptions { DeviceId = "portal-device-uuid" };
        var grandSlam = new GrandSlamClient(
            new HttpClient(handler),
            anisette,
            options,
            NullLogger<GrandSlamClient>.Instance);
        var dev = new DeveloperServicesClient(
            new HttpClient(new FakeDeveloperServicesHandler()),
            anisette,
            options,
            NullLogger<DeveloperServicesClient>.Instance);
        var portal = new AppleDeveloperPortal(grandSlam, dev);
        return (portal, handler);
    }

    [Fact]
    public async Task Authenticate_HappyPath_YieldsSession()
    {
        (IAppleDeveloperPortal portal, _) = Build();

        AppleLoginResult result = await portal.AuthenticateAsync(Username, Password);

        var success = Assert.IsType<AppleLoginResult.Success>(result);
        Assert.Equal(Username, success.Session.AppleId);
        Assert.NotEmpty(success.Session.Adsid);
    }

    [Fact]
    public async Task Authenticate_TwoFactorThenSubmitThenRetry_FullFlow()
    {
        (IAppleDeveloperPortal portal, FakeGrandSlamHandler handler) =
            Build(FakeGrandSlamHandler.TwoFactorMode.TrustedDevice);

        var challenge = Assert.IsType<AppleLoginResult.TwoFactorRequired>(
            await portal.AuthenticateAsync(Username, Password)).Challenge;

        await portal.SubmitTwoFactorCodeAsync(challenge, "123456");
        Assert.True(handler.CodeValidated);

        AppleLoginResult retry = await portal.AuthenticateAsync(Username, Password);
        Assert.IsType<AppleLoginResult.Success>(retry);
    }

    [Fact]
    public async Task ResourceMethods_RunOverDeveloperServices()
    {
        (IAppleDeveloperPortal portal, _) = Build();
        var session = new AppleSession(Username, "adsid", "name", new byte[32]) { IdmsToken = "idms" };

        IReadOnlyList<AppleTeam> teams = await portal.ListTeamsAsync(session);

        Assert.NotEmpty(teams);
    }

    [Fact]
    public async Task RevokeDevelopmentCertificate_DeletesOnlyExactResource()
    {
        byte[] passwordKey = GrandSlamCrypto.DerivePasswordKey(Password, Salt, Iterations);
        var login = new FakeGrandSlamHandler(Username, passwordKey, Salt, Iterations, FakeGrandSlamHandler.TwoFactorMode.None);
        var developer = new FakeDeveloperServicesHandler("ABCDE12345");
        developer.SeedDevelopmentCertificate("CERT-ONE");
        developer.SeedDevelopmentCertificate("CERT-TWO");
        var anisette = new StubAnisetteProvider();
        var options = new GrandSlamClientOptions { DeviceId = "portal-device-uuid" };
        var portal = new AppleDeveloperPortal(
            new GrandSlamClient(new HttpClient(login), anisette, options, NullLogger<GrandSlamClient>.Instance),
            new DeveloperServicesClient(new HttpClient(developer), anisette, options, NullLogger<DeveloperServicesClient>.Instance));
        var session = new AppleSession(Username, "adsid", "name", new byte[32]) { IdmsToken = "idms" };

        await portal.RevokeDevelopmentCertificateAsync(session, "ABCDE12345", "CERT-ONE");

        Assert.Equal(["CERT-TWO"], developer.CertificateIds);
        Assert.Contains(("DELETE", "certificates/CERT-ONE"), developer.ServiceRequests);
    }
}
