using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Sideport.Api.AppleAccess;
using Sideport.Api.Catalog;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Operations;
using Sideport.Orchestrator;

namespace Sideport.Api.Tests;

public sealed class PendingRegistrationServiceTests : IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);
    private const string AppleId = "owner.secret@example.com";
    private const string AccountProfileId = "acct_owner";
    private const string TeamId = "TEAMID1234";
    private const string DeviceUdid = "00008101TESTDEVICE";
    private const string CatalogAppId = "sample-app";
    private const string BundleId = "com.example.sample";
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"sideport-pending-registration-{Guid.NewGuid():N}");

    public PendingRegistrationServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task CreateAsync_PersistsV2Selection_Idempotently_AndReturnsOnlyAppleIdHint()
    {
        string sourceIpa = WriteIpa(Path.Combine(_directory, "source.ipa"));
        var catalog = new FileAppCatalog(new AppCatalogOptions(
            Path.Combine(_directory, "catalog.json"),
            Path.Combine(_directory, "catalog-imports"),
            MaxUploadBytes: 64 * 1024 * 1024,
            Seeds: [new AppCatalogSeed(
                CatalogAppId,
                "Sample App",
                sourceIpa,
                BundleId,
                "Test app")]));
        var registry = new InMemoryAppRegistry();
        var knownDevices = new KnownDeviceStore(Path.Combine(_directory, "known-devices.json"));
        await knownDevices.UpsertAsync(AcceptedDevice());
        var personalApple = new FakePersonalAppleAccess(new PersonalAppleInstallContext(
            AppleId,
            AccountProfileId,
            TeamId,
            Now));
        var request = new CatalogAppRegistrationRequest(
            CatalogAppId,
            DeviceUdid,
            AccountProfileId);

        var firstService = new PendingRegistrationService(
            registry,
            catalog,
            new Lazy<IPersonalAppleAccess>(() => personalApple),
            knownDevices,
            new IpaStore(Path.Combine(_directory, "registration-ipas")));
        CatalogAppRegistrationResult created = await firstService.CreateAsync(request);

        var reloadedService = new PendingRegistrationService(
            registry,
            catalog,
            new Lazy<IPersonalAppleAccess>(() => personalApple),
            knownDevices,
            new IpaStore(Path.Combine(_directory, "registration-ipas")));
        CatalogAppRegistrationResult replay = await reloadedService.CreateAsync(request);

        Assert.True(created.Created);
        Assert.False(replay.Created);
        Assert.NotNull(created.Registration);
        Assert.Equal(created.Registration, replay.Registration);
        Assert.Equal("o***@example.com", created.Registration.AppleIdHint);
        Assert.Equal("pending-install", created.Registration.Lifecycle);
        Assert.Equal(CatalogAppId, created.Registration.CatalogAppId);

        AppRegistration stored = Assert.Single(await registry.ListAsync());
        Assert.Equal(AppleId, stored.AppleId);
        Assert.Equal(AccountProfileId, personalApple.LastResolvedAccountProfileId);
        Assert.Equal(TeamId, stored.TeamId);
        Assert.Equal(DeviceUdid, stored.DeviceUdid);
        Assert.Equal(BundleId, stored.BundleId);
        Assert.Equal(CatalogAppId, stored.CatalogAppId);
        Assert.True(stored.IsPendingInstall);
        Assert.True(File.Exists(stored.InputIpaPath));
        Assert.NotEqual(Path.GetFullPath(sourceIpa), Path.GetFullPath(stored.InputIpaPath));

        using JsonDocument response = JsonDocument.Parse(JsonSerializer.Serialize(created.Registration, WebJson));
        Assert.False(response.RootElement.TryGetProperty("appleId", out _));
        Assert.Equal("o***@example.com", response.RootElement.GetProperty("appleIdHint").GetString());
        Assert.DoesNotContain(AppleId, response.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_directory, response.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static KnownDeviceRecord AcceptedDevice() =>
        new(
            DeviceUdid,
            "iPhone",
            "iPhone17,1",
            "26.0",
            Connection: "usb",
            FirstSeenAt: Now.AddMinutes(-10),
            LastSeenAt: Now,
            LastSeenSource: "device",
            CurrentPollAt: Now,
            TrustState: "trusted",
            Owner: null,
            Notes: null,
            UpdatedAt: Now,
            InventoryState: "accepted",
            AcceptedAt: Now,
            AcceptedBy: "api-token-client",
            EnrollmentOperationId: "op_enroll_verified",
            TrustReason: null,
            LockdownCheckedAt: Now,
            UsableForInstall: true);

    private static string WriteIpa(string path)
    {
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("Payload/Sample.app/Info.plist");
        using Stream target = entry.Open();
        byte[] plist = Encoding.UTF8.GetBytes($$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>CFBundleIdentifier</key><string>{{BundleId}}</string>
              <key>CFBundleDisplayName</key><string>Sample App</string>
              <key>CFBundleExecutable</key><string>Sample</string>
              <key>CFBundleVersion</key><string>1</string>
              <key>CFBundleShortVersionString</key><string>1.0</string>
            </dict></plist>
            """);
        target.Write(plist);
        return path;
    }

    private sealed class FakePersonalAppleAccess(PersonalAppleInstallContext context) : IPersonalAppleAccess
    {
        public string? LastResolvedAccountProfileId { get; private set; }

        public Task<PersonalAppleInstallContext> ResolveFreshInstallContextAsync(
            string accountProfileId,
            CancellationToken ct = default)
        {
            LastResolvedAccountProfileId = accountProfileId;
            return Task.FromResult(context);
        }

        public Task<PersonalAppleStatusDto> StatusAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PersonalAppleConnectResult> ConnectAsync(
            PersonalAppleConnectRequest request,
            string actor,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PersonalAppleStatusDto> SignInAsync(
            PersonalAppleSignInRequest request,
            string? actor = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PersonalAppleTwoFactorResult> CompleteTwoFactorAsync(
            PersonalAppleCompleteTwoFactorRequest request,
            string? actor = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public string? PendingChallengeAccountProfileId(string challengeId, string actor) => null;

        public Task<PersonalAppleInstallPreflightContext> ResolveFreshInstallPreflightContextAsync(
            string accountProfileId,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PersonalAppleStatusDto> SelectTeamAsync(
            PersonalAppleTeamSelectionRequest request,
            string actor,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
