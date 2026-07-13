using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
    private const string Udid = "00008110-0011223344556677";

    private readonly string _workDir = Path.Combine(
        Path.GetTempPath(), "sideport-test-" + Guid.NewGuid().ToString("N"));

    private (PortalSigningIdentityProvider provider, FakeDeveloperServicesHandler handler) Build(
        FakeTimeProvider time,
        FakeDeveloperServicesHandler? existingHandler = null)
    {
        var handler = existingHandler ?? new FakeDeveloperServicesHandler(TeamId);
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

    private string IdentityPath()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("dev@example.com"));
        string shortHash = Convert.ToHexStringLower(hash)[..16];
        return Path.Combine(_workDir, "identities", $"{shortHash}-{TeamId}.p12");
    }

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
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            Assert.Equal(expected, File.GetUnixFileMode(IdentityPath()));
            Assert.Equal(expected, File.GetUnixFileMode(inputs.Pkcs12Path));
            Assert.Equal(expected, File.GetUnixFileMode(inputs.ProvisioningProfilePath));
        }
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
    public async Task Replace_RevokesOnlyAcknowledgedInventoryThenPersistsOneIdentity()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var handler = new FakeDeveloperServicesHandler(TeamId);
        handler.SeedDevelopmentCertificate("CERT-OLD");
        (PortalSigningIdentityProvider provider, _) = Build(time, handler);

        SigningIdentityInspection result = await provider.ReplaceAsync(
            Session(), TeamId, ["CERT-OLD"]);

        Assert.Equal("reusable", result.State);
        Assert.DoesNotContain("CERT-OLD", handler.CertificateIds);
        Assert.Single(handler.CertificateIds);
        Assert.True(File.Exists(IdentityPath()));
        Assert.Contains(("DELETE", "certificates/CERT-OLD"), handler.ServiceRequests);
    }

    [Fact]
    public async Task Replace_InventoryDriftRevokesNothingAndMintsNothing()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var handler = new FakeDeveloperServicesHandler(TeamId);
        handler.SeedDevelopmentCertificate("CERT-OLD");
        handler.SeedDevelopmentCertificate("CERT-NEW");
        (PortalSigningIdentityProvider provider, _) = Build(time, handler);

        await Assert.ThrowsAsync<SigningReplacementInventoryChangedException>(() =>
            provider.ReplaceAsync(Session(), TeamId, ["CERT-OLD"]));

        Assert.Equal(["CERT-OLD", "CERT-NEW"], handler.CertificateIds);
        Assert.DoesNotContain(handler.ServiceRequests, request => request.Method == "DELETE");
        Assert.DoesNotContain(handler.Requests, request => request.Action == "ios/submitDevelopmentCSR.action");
        Assert.False(File.Exists(IdentityPath()));
    }

    [Fact]
    public async Task Prepare_RestrictedPolicyNeverCreatesAKeyOrCallsAppleForMissingIdentity()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (PortalSigningIdentityProvider provider, FakeDeveloperServicesHandler handler) = Build(time);

        OwnerManagedAppleActionRequiredException error =
            await Assert.ThrowsAsync<OwnerManagedAppleActionRequiredException>(() =>
                provider.PrepareAsync(
                    Session(),
                    TeamId,
                    BundleId,
                    Udid,
                    allowCertificateCreation: false));

        Assert.Equal("owner-action-required", error.ErrorCode);
        Assert.False(Directory.Exists(Path.Combine(_workDir, "identities")));
        Assert.Empty(handler.Requests);
        Assert.Empty(handler.ServiceRequests);
    }

    [Fact]
    public async Task Prepare_RestrictedPolicyReusesPersistedIdentityWithoutMinting()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (PortalSigningIdentityProvider provider, FakeDeveloperServicesHandler handler) = Build(time);
        await provider.PrepareAsync(Session(), TeamId, BundleId, Udid);
        int mintsBeforeRestrictedPrepare = handler.Requests.Count(request =>
            request.Action == "ios/submitDevelopmentCSR.action");

        using PreparedSigningInputs inputs = await provider.PrepareAsync(
            Session(),
            TeamId,
            BundleId,
            Udid,
            allowCertificateCreation: false);

        Assert.True(File.Exists(inputs.Pkcs12Path));
        Assert.Equal(
            mintsBeforeRestrictedPrepare,
            handler.Requests.Count(request => request.Action == "ios/submitDevelopmentCSR.action"));
    }

    [Fact]
    public async Task Prepare_NearExpiryRequiresExplicitCutoverWithoutRevoking()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (PortalSigningIdentityProvider provider, FakeDeveloperServicesHandler handler) = Build(time);

        await provider.PrepareAsync(Session(), TeamId, BundleId, Udid);
        time.Advance(TimeSpan.FromDays(360));
        await Assert.ThrowsAsync<SigningCertificateReplacementRequiredException>(
            () => provider.PrepareAsync(Session(), TeamId, BundleId, Udid));

        int mints = handler.Requests.Count(r => r.Action == "ios/submitDevelopmentCSR.action");
        Assert.Equal(1, mints);
        Assert.DoesNotContain(handler.ServiceRequests, request => request.Item1 == "DELETE");
    }

    [Fact]
    public async Task Prepare_ExistingCertificateFailsClosedWithoutRevoking()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (PortalSigningIdentityProvider provider, FakeDeveloperServicesHandler handler) = Build(time);
        handler.SeedDevelopmentCertificate("EXISTING-CERT");

        SigningCertificateReplacementRequiredException error = await Assert.ThrowsAsync<SigningCertificateReplacementRequiredException>(
            () => provider.PrepareAsync(Session(), TeamId, BundleId, Udid));

        Assert.Equal(1, error.CertificateCount);
        Assert.DoesNotContain(handler.ServiceRequests, request => request.Item1 == "DELETE");
        Assert.Contains("EXISTING-CERT", handler.CertificateIds);
        Assert.Single(handler.CertificateIds);
        Assert.Equal(0, handler.Requests.Count(r => r.Action == "ios/submitDevelopmentCSR.action"));
        Assert.DoesNotContain(handler.Requests, r => r.Action == "ios/addDevice.action");
        Assert.Equal(("GET", "certificates"), handler.ServiceRequests[0]);
    }

    [Fact]
    public async Task Prepare_LostMintResponseRecoversWithDurablyStagedKey()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var handler = new FakeDeveloperServicesHandler(TeamId)
        {
            DropNextCertificateResponseAfterIssuing = true,
        };
        (PortalSigningIdentityProvider firstAttempt, _) = Build(time, handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => firstAttempt.PrepareAsync(Session(), TeamId, BundleId, Udid));

        Assert.Single(handler.CertificateIds);
        Assert.DoesNotContain(handler.Requests, r => r.Action == "ios/addDevice.action");
        Assert.Single(Directory.GetFiles(
            Path.Combine(_workDir, "identities"), "*.pending.pkcs8"));

        // A fresh provider models a process restart. It reloads the pre-mint
        // private key, finds Apple's matching certificate, and completes without
        // minting a second certificate or requiring a destructive replacement.
        (PortalSigningIdentityProvider recovered, _) = Build(time, handler);
        PreparedSigningInputs inputs =
            await recovered.PrepareAsync(Session(), TeamId, BundleId, Udid);

        Assert.True(File.Exists(inputs.Pkcs12Path));
        Assert.Single(handler.CertificateIds);
        Assert.Equal(1, handler.Requests.Count(
            r => r.Action == "ios/submitDevelopmentCSR.action"));
        Assert.Empty(Directory.GetFiles(
            Path.Combine(_workDir, "identities"), "*.pending.pkcs8"));
        Assert.Empty(Directory.GetFiles(
            Path.Combine(_workDir, "identities"), "*.tmp"));
    }

    [Fact]
    public async Task Prepare_DoesNotTrustPublicOnlyPersistedCertificate()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (PortalSigningIdentityProvider provider, FakeDeveloperServicesHandler handler) = Build(time);
        Directory.CreateDirectory(Path.GetDirectoryName(IdentityPath())!);

        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Public only",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 withKey = request.CreateSelfSigned(
            time.GetUtcNow().AddMinutes(-1),
            time.GetUtcNow().AddDays(30));
        using X509Certificate2 publicOnly =
            X509CertificateLoader.LoadCertificate(withKey.RawData);
        await File.WriteAllBytesAsync(
            IdentityPath(),
            publicOnly.Export(X509ContentType.Pkcs12, string.Empty));

        await provider.PrepareAsync(Session(), TeamId, BundleId, Udid);

        Assert.Equal(1, handler.Requests.Count(
            r => r.Action == "ios/submitDevelopmentCSR.action"));
        using X509Certificate2 persisted =
            X509CertificateLoader.LoadPkcs12FromFile(IdentityPath(), string.Empty);
        Assert.True(persisted.HasPrivateKey);
    }

    [Fact]
    public async Task Prepare_RemovesMalformedPendingKeyAndStaleSecretTemp()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        (PortalSigningIdentityProvider provider, FakeDeveloperServicesHandler handler) = Build(time);
        Directory.CreateDirectory(Path.GetDirectoryName(IdentityPath())!);
        string pendingPath = IdentityPath() + ".pending.pkcs8";
        string staleTempPath = IdentityPath() + ".abandoned.tmp";
        await File.WriteAllBytesAsync(pendingPath, [0x01, 0x02, 0x03]);
        await File.WriteAllBytesAsync(staleTempPath, [0x04, 0x05, 0x06]);

        await Assert.ThrowsAsync<CryptographicException>(
            () => provider.PrepareAsync(Session(), TeamId, BundleId, Udid));

        Assert.False(File.Exists(pendingPath));
        Assert.False(File.Exists(staleTempPath));
        Assert.Empty(handler.Requests);
        Assert.Empty(handler.ServiceRequests);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort */ }
    }
}
