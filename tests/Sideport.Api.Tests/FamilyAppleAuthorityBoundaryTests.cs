using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sideport.Api.AppleAccess;
using Sideport.Api.Catalog;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Operations;
using Sideport.Api.WorkspaceAccess;
using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.Tests;

public sealed class FamilyAppleAuthorityBoundaryTests : IDisposable
{
    private const string AppleId = "owner@example.test";
    private const string TeamId = "TEAMID1234";
    private const string DeviceUdid = "family-device-udid";
    private const string BundleId = "com.example.family";
    private const string CatalogAppId = "family-approved-app";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "sideport-family-apple-boundary-tests",
        Guid.NewGuid().ToString("N"));

    public FamilyAppleAuthorityBoundaryTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task FamilyRefresh_WhenCatalogLineageChanges_StopsBeforeAppleOrDeviceMutation()
    {
        using BoundaryFixture fixture = await CreateFixtureAsync("refresh");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        const string verificationId = "op_family_refresh_verified";
        var verified = new OperationRecordDto(
            verificationId,
            "install",
            "succeeded",
            now.AddDays(-1),
            now.AddDays(-1),
            now.AddDays(-1),
            now.AddDays(-1),
            fixture.FamilyActor,
            "family-refresh-verified",
            Attempt: 1,
            fixture.Target,
            [new OperationStageDto("verify", "Verify", "succeeded", now.AddDays(-1), now.AddDays(-1), "Verified.")],
            new OperationResultDto(true, BundleId, now.AddDays(6), null, Version: "1.0"),
            Error: null,
            Cancelable: false,
            Retryable: false,
            Rerunnable: true,
            CorrelationId: verificationId,
            ActorMemberId: fixture.Family.MemberId,
            OwnerMemberId: fixture.Family.MemberId);
        await fixture.Operations.AddIfIdempotentMissingAsync(verified);
        await fixture.Registry.UpsertAsync(fixture.Registration with
        {
            Lifecycle = "active",
            ActivatedAt = now.AddDays(-1),
            LastVerifiedOperationId = verificationId,
        });

        const string operationId = "op_family_refresh_stale_catalog";
        var queued = new OperationRecordDto(
            operationId,
            "refresh",
            "queued",
            now,
            null,
            now,
            null,
            fixture.FamilyActor,
            "family-refresh-stale-catalog",
            Attempt: 1,
            fixture.Target,
            [new OperationStageDto("refresh", "Refresh", "pending", null, null, "Waiting.")],
            Result: null,
            Error: null,
            Cancelable: true,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: operationId,
            ActorMemberId: fixture.Family.MemberId,
            OwnerMemberId: fixture.Family.MemberId);
        await fixture.Operations.AddIfIdempotentMissingAsync(queued);
        await fixture.AdvanceCatalogAsync();

        await fixture.OperationsService.ProcessQueuedRefreshAsync(operationId);

        OperationRecordDto terminal = (await fixture.Operations.FindAsync(operationId))!;
        Assert.Equal("blocked", terminal.Status);
        Assert.Equal("owner-action-required", terminal.Error?.Code);
        Assert.Equal(0, fixture.Sessions.AuthenticationCalls);
        Assert.Equal(0, fixture.SigningIdentity.PrepareCalls);
        Assert.Equal(0, fixture.Signer.SignCalls);
        Assert.Equal(0, fixture.Devices.InstallCalls);
    }

    [Fact]
    public async Task FamilyInstall_WhenCatalogLineageChanges_StopsBeforeAppleOrDeviceMutation()
    {
        using BoundaryFixture fixture = await CreateFixtureAsync("install");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await fixture.Registry.UpsertAsync(fixture.Registration);

        const string operationId = "op_family_install_stale_catalog";
        var intent = new InstallOperationIntentDto(
            DeviceUdid,
            CatalogAppId,
            fixture.AccountProfileId,
            BundleId,
            FinishOnboarding: false,
            fixture.Registration.Key,
            CatalogVersion: fixture.CatalogVersion,
            CatalogSha256: fixture.CatalogSha256);
        var queued = new OperationRecordDto(
            operationId,
            "install",
            "queued",
            now,
            null,
            now,
            null,
            fixture.FamilyActor,
            "family-install-stale-catalog",
            Attempt: 1,
            fixture.Target,
            [
                new OperationStageDto("preflight", "Preflight", "succeeded", now, now, "Ready."),
                new OperationStageDto("install", "Sign and install", "pending", null, null, "Waiting."),
                new OperationStageDto("verify", "Verify", "pending", null, null, "Waiting."),
            ],
            Result: null,
            Error: null,
            Cancelable: true,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: operationId,
            InstallIntent: intent,
            ActorMemberId: fixture.Family.MemberId,
            OwnerMemberId: fixture.Family.MemberId);
        await fixture.Operations.AddIfIdempotentMissingAsync(queued);
        await fixture.AdvanceCatalogAsync();

        await fixture.OperationsService.ProcessQueuedInstallAsync(operationId);

        OperationRecordDto terminal = (await fixture.Operations.FindAsync(operationId))!;
        Assert.Equal("failed", terminal.Status);
        Assert.Equal("owner-action-required", terminal.Error?.Code);
        Assert.Equal(0, fixture.Sessions.AuthenticationCalls);
        Assert.Equal(0, fixture.SigningIdentity.PrepareCalls);
        Assert.Equal(0, fixture.Signer.SignCalls);
        Assert.Equal(0, fixture.Devices.InstallCalls);
    }

    private async Task<BoundaryFixture> CreateFixtureAsync(string suffix)
    {
        string stateDirectory = Path.Combine(_directory, suffix, "state");
        string sourceDirectory = Path.Combine(_directory, suffix, "source");
        string initialIpa = WriteIpa(sourceDirectory, BundleId, "1.0");
        var sessions = new TrackingSessionManager(AppleId);
        var signingIdentity = new TrackingSigningIdentityProvider();
        var signer = new TrackingSigner();
        var devices = new TrackingDeviceController();
        string accountProfileId = AppleAccountIdentity.ProfileIdFor(AppleId);
        var personalApple = new TrackingPersonalAppleAccess(accountProfileId);
        WebApplicationFactory<Program> factory = CreateFactory(
            stateDirectory,
            sessions,
            signingIdentity,
            signer,
            devices,
            personalApple);

        try
        {
            IServiceProvider services = factory.Services;
            WorkspaceAccessStore workspace = services.GetRequiredService<WorkspaceAccessStore>();
            (WorkspaceMemberRecord _, WorkspaceMemberRecord family) = await BootstrapFamilyAsync(workspace, suffix);
            KnownDeviceStore knownDevices = services.GetRequiredService<KnownDeviceStore>();
            await knownDevices.UpsertAsync(AcceptedDevice(family.MemberId));

            IAppCatalog catalog = services.GetRequiredService<IAppCatalog>();
            CatalogV2MutationResult first = await catalog.ImportUploadedIpaV2Async(
                new CatalogUploadV2Request(
                    initialIpa,
                    Id: CatalogAppId,
                    Name: "Family App",
                    Purpose: "Approved by the Owner",
                    IdempotencyKey: $"family-catalog-v1-{suffix}"),
                "member:owner");
            CatalogAppDto firstEntry = (await catalog.ListAsync()).Single(app =>
                string.Equals(app.Id, CatalogAppId, StringComparison.OrdinalIgnoreCase));
            string durableIpa = await services.GetRequiredService<IpaStore>()
                .StoreAsync(DeviceUdid, BundleId, firstEntry.IpaPath);
            var registration = new AppRegistration(
                BundleId,
                AppleId,
                TeamId,
                DeviceUdid,
                durableIpa,
                Lifecycle: "pending-install",
                CatalogAppId: CatalogAppId,
                CreatedAt: DateTimeOffset.UtcNow,
                CatalogVersion: first.Entry.CatalogVersion,
                CatalogSha256: first.Entry.Sha256);
            var target = new OperationTargetDto(
                DeviceUdid,
                BundleId,
                TeamId: TeamId,
                Kind: "catalog-app",
                CatalogAppId: CatalogAppId,
                AccountProfileId: accountProfileId,
                CatalogVersion: first.Entry.CatalogVersion,
                Version: "1.0",
                CatalogSha256: first.Entry.Sha256);
            return new BoundaryFixture(
                factory,
                sourceDirectory,
                catalog,
                services.GetRequiredService<IAppRegistry>(),
                services.GetRequiredService<OperationStore>(),
                services.GetRequiredService<OperationService>(),
                sessions,
                signingIdentity,
                signer,
                devices,
                family,
                new OperationActorDto("member", family.DisplayName, family.MemberId),
                registration,
                target,
                accountProfileId,
                first.Entry.CatalogVersion,
                first.Entry.Sha256!);
        }
        catch
        {
            factory.Dispose();
            throw;
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string stateDirectory,
        TrackingSessionManager sessions,
        TrackingSigningIdentityProvider signingIdentity,
        TrackingSigner signer,
        TrackingDeviceController devices,
        TrackingPersonalAppleAccess personalApple) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sideport:Api:AuthToken", "test-recovery-token");
            builder.UseSetting("Sideport:Apple:DeviceId", "TEST-DEVICE-UUID");
            builder.UseSetting("Sideport:Signer:BinaryPath", File.Exists("/usr/bin/true")
                ? "/usr/bin/true"
                : Environment.ProcessPath ?? typeof(FamilyAppleAuthorityBoundaryTests).Assembly.Location);
            builder.UseSetting("Sideport:State:Directory", stateDirectory);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IAnisetteProvider>();
                services.RemoveAll<ISessionManager>();
                services.RemoveAll<ISigningIdentityProvider>();
                services.RemoveAll<ISigner>();
                services.RemoveAll<IDeviceController>();
                services.RemoveAll<IPersonalAppleAccess>();
                services.AddSingleton<IAnisetteProvider>(new HealthyAnisette());
                services.AddSingleton<ISessionManager>(sessions);
                services.AddSingleton<ISigningIdentityProvider>(signingIdentity);
                services.AddSingleton<ISigner>(signer);
                services.AddSingleton<IDeviceController>(devices);
                services.AddSingleton<IPersonalAppleAccess>(personalApple);
            });
        });

    private static async Task<(WorkspaceMemberRecord Owner, WorkspaceMemberRecord Family)> BootstrapFamilyAsync(
        WorkspaceAccessStore store,
        string suffix)
    {
        WorkspaceOwnerClaimCreateResult claim = await store.CreateOwnerClaimAsync(new(
            ExpectedOwnerMemberId: null,
            ImpactVersion: null,
            Lifetime: TimeSpan.FromMinutes(15),
            IdempotencyKey: $"family-owner-bootstrap-{suffix}",
            RequestId: $"req-family-owner-bootstrap-{suffix}"));
        WorkspaceHandoffCreateResult ownerHandoff = await store.ExchangeOwnerClaimAsync(
            claim.Token!,
            $"req-family-owner-handoff-{suffix}");
        WorkspaceAcceptanceResult owner = await store.AcceptOwnerClaimAsync(
            ownerHandoff.Token,
            new WorkspaceAcceptanceRequest(
                new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", $"owner-{suffix}"),
                "Owner",
                "owner@example.test",
                $"family-owner-accept-{suffix}",
                $"req-family-owner-accept-{suffix}"));

        WorkspaceInvitationCreateResult invitation = await store.CreateInvitationAsync(new(
            WorkspaceActorRecord.ForMember(owner.Member.MemberId),
            "Family",
            "family@example.test",
            TimeSpan.FromDays(7),
            $"family-invite-{suffix}",
            $"req-family-invite-{suffix}"));
        WorkspaceHandoffCreateResult familyHandoff = await store.ExchangeInvitationAsync(
            invitation.Token!,
            $"req-family-handoff-{suffix}");
        WorkspaceAcceptanceResult family = await store.AcceptInvitationAsync(
            familyHandoff.Token,
            new WorkspaceAcceptanceRequest(
                new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", $"family-{suffix}"),
                "Family",
                "family@example.test",
                $"family-accept-{suffix}",
                $"req-family-accept-{suffix}"));
        return (owner.Member, family.Member);
    }

    private static KnownDeviceRecord AcceptedDevice(string ownerMemberId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new KnownDeviceRecord(
            DeviceUdid,
            "Family iPhone",
            "iPhone15,2",
            "18.5",
            "usb",
            now,
            now,
            "test",
            now,
            "trusted",
            Owner: null,
            Notes: null,
            UpdatedAt: now,
            InventoryState: "accepted",
            AcceptedAt: now,
            AcceptedBy: "test",
            EnrollmentOperationId: "op-family-enrollment",
            TrustReason: "Lockdown verified.",
            LockdownCheckedAt: now,
            UsableForInstall: true,
            OwnerMemberId: ownerMemberId);
    }

    private static string WriteIpa(string directory, string bundleId, string shortVersion)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{Guid.NewGuid():N}.ipa");
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("Payload/Family.app/Info.plist");
        using Stream entryStream = entry.Open();
        byte[] plist = Encoding.UTF8.GetBytes($$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>CFBundleIdentifier</key><string>{{bundleId}}</string>
                <key>CFBundleDisplayName</key><string>Family App</string>
                <key>CFBundleExecutable</key><string>Family</string>
                <key>CFBundleVersion</key><string>{{shortVersion}}</string>
                <key>CFBundleShortVersionString</key><string>{{shortVersion}}</string>
            </dict>
            </plist>
            """);
        entryStream.Write(plist);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class BoundaryFixture(
        WebApplicationFactory<Program> factory,
        string sourceDirectory,
        IAppCatalog catalog,
        IAppRegistry registry,
        OperationStore operations,
        OperationService operationsService,
        TrackingSessionManager sessions,
        TrackingSigningIdentityProvider signingIdentity,
        TrackingSigner signer,
        TrackingDeviceController devices,
        WorkspaceMemberRecord family,
        OperationActorDto familyActor,
        AppRegistration registration,
        OperationTargetDto target,
        string accountProfileId,
        int catalogVersion,
        string catalogSha256) : IDisposable
    {
        public IAppRegistry Registry { get; } = registry;
        public OperationStore Operations { get; } = operations;
        public OperationService OperationsService { get; } = operationsService;
        public TrackingSessionManager Sessions { get; } = sessions;
        public TrackingSigningIdentityProvider SigningIdentity { get; } = signingIdentity;
        public TrackingSigner Signer { get; } = signer;
        public TrackingDeviceController Devices { get; } = devices;
        public WorkspaceMemberRecord Family { get; } = family;
        public OperationActorDto FamilyActor { get; } = familyActor;
        public AppRegistration Registration { get; } = registration;
        public OperationTargetDto Target { get; } = target;
        public string AccountProfileId { get; } = accountProfileId;
        public int CatalogVersion { get; } = catalogVersion;
        public string CatalogSha256 { get; } = catalogSha256;

        public async Task AdvanceCatalogAsync()
        {
            string replacement = WriteIpa(sourceDirectory, BundleId, "2.0");
            CatalogV2MutationResult changed = await catalog.ImportUploadedIpaV2Async(
                new CatalogUploadV2Request(
                    replacement,
                    Id: CatalogAppId,
                    Name: "Family App",
                    Purpose: "Approved by the Owner",
                    IdempotencyKey: $"family-catalog-v2-{Guid.NewGuid():N}",
                    ExpectedCatalogVersion: CatalogVersion),
                "member:owner");
            Assert.Equal(CatalogVersion + 1, changed.Entry.CatalogVersion);
            Assert.NotEqual(CatalogSha256, changed.Entry.Sha256);
        }

        public void Dispose() => factory.Dispose();
    }

    private sealed class TrackingSessionManager : ISessionManager
    {
        private readonly AppleSession _session;
        private int _authenticationCalls;

        public TrackingSessionManager(string appleId) =>
            _session = new AppleSession(appleId, "cached-adsid", "Owner", [0x01])
            {
                IdmsToken = "cached-idms-token",
            };

        public int AuthenticationCalls => Volatile.Read(ref _authenticationCalls);

        public AppleSession? TryGetCachedSession(string appleId) =>
            string.Equals(appleId, _session.AppleId, StringComparison.OrdinalIgnoreCase)
                ? _session
                : null;

        public Task<AppleSession> GetSessionAsync(string appleId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _authenticationCalls);
            return Task.FromResult(_session);
        }

        public Task<AppleLoginResult> SignInAsync(string appleId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _authenticationCalls);
            return Task.FromResult<AppleLoginResult>(new AppleLoginResult.Success(_session));
        }

        public Task<AppleSession> CompleteTwoFactorAsync(
            string appleId,
            AppleLoginChallenge challenge,
            string code,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _authenticationCalls);
            return Task.FromResult(_session);
        }

        public void RememberSession(AppleSession session) { }

        public void Invalidate(string appleId) { }
    }

    private sealed class TrackingSigningIdentityProvider : ISigningIdentityProvider
    {
        private int _prepareCalls;
        public int PrepareCalls => Volatile.Read(ref _prepareCalls);

        public Task<SigningIdentityInspection> InspectAsync(
            string appleId,
            string teamId,
            CancellationToken ct = default) =>
            Task.FromResult(new SigningIdentityInspection(
                "reusable",
                DateTimeOffset.UtcNow.AddMonths(6),
                "TEST"));

        public Task<PreparedSigningInputs> PrepareAsync(
            AppleSession session,
            string teamId,
            string bundleId,
            string deviceUdid,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _prepareCalls);
            return Task.FromException<PreparedSigningInputs>(new InvalidOperationException(
                "Stale Family catalog lineage must stop before signing preparation."));
        }

        public Task<PreparedSigningInputs> PrepareAsync(
            AppleSession session,
            string teamId,
            string bundleId,
            string deviceUdid,
            bool allowCertificateCreation,
            CancellationToken ct = default) =>
            PrepareAsync(session, teamId, bundleId, deviceUdid, ct);
    }

    private sealed class TrackingSigner : ISigner
    {
        private int _signCalls;
        public int SignCalls => Volatile.Read(ref _signCalls);

        public Task<SignResult> SignAsync(SignRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _signCalls);
            return Task.FromResult(new SignResult(false, request.OutputIpaPath, null, "unexpected sign"));
        }
    }

    private sealed class TrackingDeviceController : IDeviceController
    {
        private int _installCalls;
        public int InstallCalls => Volatile.Read(ref _installCalls);

        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>([new DeviceInfo(
                DeviceUdid,
                "Family iPhone",
                "iPhone15,2",
                "18.5",
                DeviceConnection.Usb,
                "trusted",
                "Lockdown session verified over USB.",
                DateTimeOffset.UtcNow,
                UsableForInstall: true)]);

        public Task<DeviceTrustProbe> ProbeTrustAsync(string udid, CancellationToken ct = default) =>
            Task.FromResult(new DeviceTrustProbe(
                udid,
                DeviceConnection.Usb,
                "trusted",
                "Lockdown session verified over USB.",
                DateTimeOffset.UtcNow,
                UsableForInstall: true));

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(
            string udid,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InstalledApp>>([]);

        public Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _installCalls);
            return Task.CompletedTask;
        }

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceDiagnostics("ok", []));
    }

    private sealed class TrackingPersonalAppleAccess(string accountProfileId) : IPersonalAppleAccess
    {
        public Task<PersonalAppleStatusDto> StatusAsync(CancellationToken ct = default) =>
            Task.FromResult(Status());

        public Task<PersonalAppleInstallContext> ResolveFreshInstallContextAsync(
            string requestedAccountProfileId,
            CancellationToken ct = default) =>
            Task.FromResult(Context(requestedAccountProfileId));

        public Task<PersonalAppleInstallPreflightContext> ResolveFreshInstallPreflightContextAsync(
            string requestedAccountProfileId,
            CancellationToken ct = default) =>
            Task.FromResult(new PersonalAppleInstallPreflightContext(
                Context(requestedAccountProfileId),
                []));

        public Task<PersonalAppleConnectResult> ConnectAsync(
            PersonalAppleConnectRequest request,
            string actor,
            CancellationToken ct = default) =>
            Task.FromException<PersonalAppleConnectResult>(UnexpectedMutation());

        public Task<PersonalAppleStatusDto> SignInAsync(
            PersonalAppleSignInRequest request,
            string? actor = null,
            CancellationToken ct = default) =>
            Task.FromException<PersonalAppleStatusDto>(UnexpectedMutation());

        public Task<PersonalAppleTwoFactorResult> CompleteTwoFactorAsync(
            PersonalAppleCompleteTwoFactorRequest request,
            string? actor = null,
            CancellationToken ct = default) =>
            Task.FromException<PersonalAppleTwoFactorResult>(UnexpectedMutation());

        public string? PendingChallengeAccountProfileId(string challengeId, string actor) => null;

        public Task<PersonalAppleStatusDto> SelectTeamAsync(
            PersonalAppleTeamSelectionRequest request,
            string actor,
            CancellationToken ct = default) =>
            Task.FromException<PersonalAppleStatusDto>(UnexpectedMutation());

        private PersonalAppleInstallContext Context(string requestedAccountProfileId)
        {
            Assert.Equal(accountProfileId, requestedAccountProfileId);
            return new PersonalAppleInstallContext(
                AppleId,
                accountProfileId,
                TeamId,
                DateTimeOffset.UtcNow);
        }

        private PersonalAppleStatusDto Status() => new(
            "personal-apple-id",
            "validated-recently",
            "server",
            "o***@example.test",
            "Validated.",
            PendingChallengeId: null,
            PendingChallengeKind: null,
            [new PersonalAppleTeamDto(TeamId, "Personal Team", "Individual")],
            AccountProfileId: accountProfileId,
            SelectedTeamId: TeamId,
            TeamValidatedAt: DateTimeOffset.UtcNow,
            LastAuthenticatedAt: DateTimeOffset.UtcNow,
            AuthValidatedAt: DateTimeOffset.UtcNow);

        private static Exception UnexpectedMutation() => new InvalidOperationException(
            "Stale Family catalog lineage must stop before Apple mutation.");
    }

    private sealed class HealthyAnisette : IAnisetteProvider
    {
        public Task<AnisetteClientInfo> GetClientInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new AnisetteClientInfo("<TestClient>", "akd/1.0"));

        public Task<AnisetteHeaders> GetHeadersAsync(CancellationToken ct = default) =>
            Task.FromResult(new AnisetteHeaders("M", "O", "R", "L", DateTimeOffset.UnixEpoch));
    }
}
