using System.Text.Json;
using Sideport.Api.Catalog;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Operations;
using Sideport.Api.WorkspaceAccess;
using Sideport.Orchestrator;

namespace Sideport.Api.Tests;

public sealed class FamilyResourceAccessTests
{
    private const string FamilyMemberId = "member_family";
    private const string OtherMemberId = "member_other";

    [Fact]
    public async Task OwnedRegistrationRead_KeepsSafeLegacyFallback_ButMutationRequiresApprovedV2()
    {
        Fixture fixture = await CreateFixtureAsync();
        await fixture.Registrations.UpsertAsync(new AppRegistration(
            "com.example.approved",
            "owner@apple.example",
            "TEAM-SECRET",
            "FAMILY-UDID",
            "/private/approved.ipa",
            CatalogAppId: "approved-app"));
        await fixture.Registrations.UpsertAsync(new AppRegistration(
            "com.example.legacy",
            "owner@apple.example",
            "TEAM-SECRET",
            "FAMILY-UDID",
            "/private/legacy.ipa"));
        await fixture.Registrations.UpsertAsync(new AppRegistration(
            "com.example.other",
            "owner@apple.example",
            "TEAM-SECRET",
            "OTHER-UDID",
            "/private/other.ipa",
            CatalogAppId: "approved-app"));

        IReadOnlyList<OwnedFamilyRegistration> visible =
            await fixture.Access.ListOwnedRegistrationsAsync(FamilyMemberId);

        Assert.Equal(2, visible.Count);
        Assert.Contains(visible, item =>
            item.Registration.BundleId == "com.example.approved" && item.CatalogApp is not null);
        Assert.Contains(visible, item =>
            item.Registration.BundleId == "com.example.legacy" && item.CatalogApp is null);
        Assert.NotNull(await fixture.Access.FindOwnedRegistrationAsync(
            FamilyMemberId,
            "FAMILY-UDID",
            "com.example.legacy"));
        Assert.Null(await fixture.Access.FindOwnedApprovedRegistrationAsync(
            FamilyMemberId,
            "FAMILY-UDID",
            "com.example.legacy"));
        Assert.Null(await fixture.Access.FindOwnedRegistrationAsync(
            FamilyMemberId,
            "OTHER-UDID",
            "com.example.other"));
    }

    [Fact]
    public async Task OperationList_FiltersOwnershipBeforeApplyingLimit_AndLegacyNullOwnerStaysHidden()
    {
        Fixture fixture = await CreateFixtureAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int index = 0; index < 5; index++)
        {
            await fixture.Operations.AddIfIdempotentMissingAsync(Operation(
                $"op_other_{index}",
                now.AddMinutes(index + 1),
                OtherMemberId,
                OtherMemberId));
        }
        await fixture.Operations.AddIfIdempotentMissingAsync(Operation(
            "op_family_old",
            now,
            FamilyMemberId,
            FamilyMemberId));
        await fixture.Operations.AddIfIdempotentMissingAsync(Operation(
            "op_legacy",
            now.AddMinutes(10),
            actorMemberId: null,
            ownerMemberId: null));

        IReadOnlyList<OperationRecordDto> visible = await fixture.Access.ListOwnedOperationsAsync(
            FamilyMemberId,
            deviceUdid: null,
            bundleId: null,
            limit: 1);

        OperationRecordDto only = Assert.Single(visible);
        Assert.Equal("op_family_old", only.OperationId);
        Assert.Null(await fixture.Access.FindOwnedOperationAsync(
            FamilyMemberId,
            "op_other_0",
            requireOwnActor: false));
        Assert.Null(await fixture.Access.FindOwnedOperationAsync(
            FamilyMemberId,
            "op_legacy",
            requireOwnActor: false));
    }

    [Fact]
    public async Task FamilyOperationRead_DoesNotReconcileOrPersistAnUnrelatedStaleOperation()
    {
        Fixture fixture = await CreateFixtureAsync();
        DateTimeOffset staleAt = DateTimeOffset.UtcNow.AddHours(-2);
        await fixture.Operations.AddIfIdempotentMissingAsync(Operation(
            "op_family_visible",
            DateTimeOffset.UtcNow,
            FamilyMemberId,
            FamilyMemberId));
        await fixture.Operations.AddIfIdempotentMissingAsync(Operation(
            "op_other_stale",
            staleAt,
            OtherMemberId,
            OtherMemberId) with
        {
            Status = "running",
            StartedAt = staleAt,
            UpdatedAt = staleAt,
            CompletedAt = null,
            Error = null,
            Stages = [new OperationStageDto(
                "refresh",
                "Refresh",
                "running",
                staleAt,
                CompletedAt: null,
                "Refreshing.")],
        });
        string beforeRead = await File.ReadAllTextAsync(fixture.OperationPath);

        IReadOnlyList<OperationRecordDto> visible = await fixture.Access.ListOwnedOperationsAsync(
            FamilyMemberId,
            deviceUdid: null,
            bundleId: null,
            limit: 25);

        Assert.Single(visible, operation => operation.OperationId == "op_family_visible");
        Assert.Equal(beforeRead, await File.ReadAllTextAsync(fixture.OperationPath));
        Assert.Equal("running", (await fixture.Operations.FindAsync("op_other_stale"))!.Status);

        await fixture.Operations.ReconcileStaleRunningOperationsAsync();

        OperationRecordDto reconciled = (await fixture.Operations.FindAsync("op_other_stale"))!;
        Assert.Equal("unknown", reconciled.Status);
        Assert.Equal("operation-terminal-state-unknown", reconciled.Error?.Code);
    }

    [Fact]
    public async Task OperationAction_RequiresBothOwnTargetAndOwnActor()
    {
        Fixture fixture = await CreateFixtureAsync();
        OperationRecordDto ownerStarted = Operation(
            "op_owner_started",
            DateTimeOffset.UtcNow,
            actorMemberId: "member_owner",
            ownerMemberId: FamilyMemberId);
        await fixture.Operations.AddIfIdempotentMissingAsync(ownerStarted);

        Assert.NotNull(await fixture.Access.FindOwnedOperationAsync(
            FamilyMemberId,
            ownerStarted.OperationId,
            requireOwnActor: false));
        Assert.Null(await fixture.Access.FindOwnedOperationAsync(
            FamilyMemberId,
            ownerStarted.OperationId,
            requireOwnActor: true));
    }

    [Fact]
    public async Task OperationProjection_AdvertisesOnlyActionsEffectiveForTheFamilyCaller()
    {
        Fixture fixture = await CreateFixtureAsync();
        await fixture.Registrations.UpsertAsync(new AppRegistration(
            "com.example.approved",
            "owner@apple.example",
            "TEAM-SECRET",
            "FAMILY-UDID",
            "/private/approved.ipa",
            CatalogAppId: "approved-app"));
        OperationRecordDto familyStarted = Operation(
            "op_family_actions",
            DateTimeOffset.UtcNow,
            FamilyMemberId,
            FamilyMemberId) with
        {
            Cancelable = true,
            Retryable = true,
            Rerunnable = true,
        };

        FamilyOperationDto familyProjection = await fixture.Access.ProjectOperationAsync(
            FamilyMemberId,
            familyStarted);
        Assert.True(familyProjection.Cancelable);
        Assert.True(familyProjection.Retryable);
        Assert.True(familyProjection.Rerunnable);

        FamilyOperationDto ownerStartedProjection = await fixture.Access.ProjectOperationAsync(
            FamilyMemberId,
            familyStarted with
            {
                OperationId = "op_owner_actions",
                ActorMemberId = "member_owner",
            });
        Assert.False(ownerStartedProjection.Cancelable);
        Assert.False(ownerStartedProjection.Retryable);
        Assert.False(ownerStartedProjection.Rerunnable);
    }

    [Fact]
    public async Task CompletedFirstEnrollment_AllowsOnlyTheExactFamilyIdempotencyReplay()
    {
        Fixture fixture = await CreateFixtureAsync();
        await fixture.Operations.AddIfIdempotentMissingAsync(Operation(
            "op_enrollment_replay",
            DateTimeOffset.UtcNow,
            FamilyMemberId,
            FamilyMemberId) with
        {
            Type = DeviceEnrollmentService.OperationType,
            IdempotencyKey = "family-first-phone",
        });

        Assert.True(await fixture.Access.HasEnrollmentReplayAsync(
            FamilyMemberId,
            "family-first-phone"));
        Assert.False(await fixture.Access.HasEnrollmentReplayAsync(
            FamilyMemberId,
            "different-key"));
        Assert.False(await fixture.Access.HasEnrollmentReplayAsync(
            OtherMemberId,
            "family-first-phone"));
    }

    [Fact]
    public void Projections_AreAllowlistsAndNeverCarryOwnerSecretsOrInternalLineage()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var raw = new OperationRecordDto(
            "op_family",
            "install",
            "failed",
            now,
            now,
            now,
            now,
            new OperationActorDto("member", "Owner Secret Name", "member_owner"),
            "idempotency-secret",
            1,
            new OperationTargetDto(
                "FAMILY-UDID",
                "com.example.approved",
                AppleId: "owner@apple.example",
                TeamId: "TEAM-SECRET",
                Kind: "catalog-app",
                CatalogAppId: "approved-app",
                AccountProfileId: "account-secret",
                CatalogVersion: 3,
                Version: "1.2.3",
                CatalogSha256: "sha256-secret"),
            [new OperationStageDto(
                "install",
                "Raw owner label",
                "failed",
                now,
                now,
                "raw /private/path owner@apple.example",
                new OperationIssueDto(
                    "refresh-failed",
                    "raw /private/path owner@apple.example",
                    Detail: "exception-secret"))],
            new OperationResultDto(
                false,
                "com.example.approved",
                now.AddDays(7),
                "raw-result-secret",
                Version: "1.2.3"),
            new OperationIssueDto(
                "refresh-failed",
                "raw /private/path owner@apple.example",
                Detail: "exception-secret"),
            false,
            true,
            false,
            "correlation-secret",
            ParentOperationId: "parent-secret",
            InstallIntent: new InstallOperationIntentDto(
                "FAMILY-UDID",
                "approved-app",
                "account-secret",
                "com.example.approved",
                false,
                "registration-secret",
                CatalogSha256: "sha256-secret"),
            ActorMemberId: FamilyMemberId,
            OwnerMemberId: FamilyMemberId);

        string json = JsonSerializer.Serialize(
            FamilyResourceProjections.Operation(raw, actionsAllowed: false),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        string[] forbidden =
        [
            "owner@apple.example",
            "TEAM-SECRET",
            "Owner Secret Name",
            "idempotency-secret",
            "account-secret",
            "sha256-secret",
            "raw /private/path",
            "exception-secret",
            "correlation-secret",
            "parent-secret",
            "registration-secret",
            "actorMemberId",
            "ownerMemberId",
            "installIntent",
            "correlationId",
        ];
        Assert.All(forbidden, value => Assert.DoesNotContain(value, json, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("FAMILY-UDID", json, StringComparison.Ordinal);
        Assert.Contains("Sideport could not update the app", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogAndDeviceProjections_OmitRepositoryHashNotesAndPrivateDeviceMetadata()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var catalog = new CatalogAppV2Dto(
            "approved-app",
            2,
            "Approved App",
            "For the family",
            "com.example.approved",
            "12",
            "1.2",
            "live",
            "ready",
            123,
            "sha256-secret",
            true,
            now.AddDays(2),
            [new CatalogArtifactSourceDto(
                "github-private",
                "Private repository",
                "family/private-repository",
                "v1.2",
                "private.ipa")],
            now,
            ["owner-private-note"]);
        var device = new KnownDeviceDto(
            "FAMILY-UDID",
            "Mara's iPhone",
            "iPhone17,1",
            "18.5",
            "wifi",
            now.AddDays(-2),
            now,
            "live-poll",
            now,
            "accepted",
            now.AddDays(-2),
            "owner-private-actor",
            "op-private-enrollment",
            "trusted",
            "raw trust reason",
            now,
            true,
            false,
            new KnownDeviceHealthDto("healthy", "Ready", "derived", now),
            new KnownDeviceAppSlotsDto(1, 3),
            "legacy owner text",
            "owner-private-note",
            OwnerMemberId: FamilyMemberId);

        string json = JsonSerializer.Serialize(
            new
            {
                app = FamilyResourceProjections.Catalog(catalog),
                phone = FamilyResourceProjections.Device(device),
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        string[] forbidden =
        [
            "sha256-secret",
            "family/private-repository",
            "private.ipa",
            "owner-private-note",
            "owner-private-actor",
            "op-private-enrollment",
            "legacy owner text",
            "ownerMemberId",
            "artifactSources",
            "notes",
            "sha256",
        ];
        Assert.All(forbidden, value => Assert.DoesNotContain(value, json, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "sideport-family-resource-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var devices = new KnownDeviceStore(Path.Combine(directory, "known-devices.json"));
        await devices.UpsertAsync(AcceptedDevice("FAMILY-UDID", FamilyMemberId));
        await devices.UpsertAsync(AcceptedDevice("OTHER-UDID", OtherMemberId));
        var registrations = new InMemoryAppRegistry();
        string operationPath = Path.Combine(directory, "operations.json");
        var operations = new OperationStore(operationPath);
        var catalog = new StubCatalog([
            new CatalogAppV2Dto(
                "approved-app",
                1,
                "Approved App",
                "For the family",
                "com.example.approved",
                "1",
                "1.0",
                "live",
                "ready",
                123,
                new string('a', 64),
                false,
                null,
                [],
                DateTimeOffset.UtcNow,
                []),
        ]);
        return new Fixture(
            new FamilyResourceAccess(devices, registrations, catalog, operations),
            registrations,
            operations,
            operationPath);
    }

    private static KnownDeviceRecord AcceptedDevice(string udid, string memberId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new KnownDeviceRecord(
            udid,
            udid,
            "iPhone17,1",
            "18.5",
            "usb",
            now,
            now,
            "live-poll",
            now,
            "trusted",
            null,
            null,
            now,
            InventoryState: "accepted",
            AcceptedAt: now,
            AcceptedBy: "member:owner",
            EnrollmentOperationId: $"op_enroll_{udid}",
            TrustReason: null,
            LockdownCheckedAt: now,
            UsableForInstall: true,
            OwnerMemberId: memberId);
    }

    private static OperationRecordDto Operation(
        string id,
        DateTimeOffset createdAt,
        string? actorMemberId,
        string? ownerMemberId) =>
        new(
            id,
            "refresh",
            "failed",
            createdAt,
            createdAt,
            createdAt,
            createdAt,
            new OperationActorDto("member", "Member", actorMemberId),
            null,
            1,
            new OperationTargetDto(
                ownerMemberId == OtherMemberId ? "OTHER-UDID" : "FAMILY-UDID",
                "com.example.approved",
                Kind: "app",
                CatalogAppId: "approved-app"),
            [],
            null,
            new OperationIssueDto("refresh-failed", "Failed"),
            false,
            true,
            false,
            id,
            ActorMemberId: actorMemberId,
            OwnerMemberId: ownerMemberId);

    private sealed record Fixture(
        FamilyResourceAccess Access,
        InMemoryAppRegistry Registrations,
        OperationStore Operations,
        string OperationPath);

    private sealed class StubCatalog(IReadOnlyList<CatalogAppV2Dto> apps) : IAppCatalog
    {
        public Task<IReadOnlyList<CatalogAppV2Dto>> ListV2Async(CancellationToken ct = default) =>
            Task.FromResult(apps);

        public Task<IReadOnlyList<CatalogAppDto>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogAppDto>>([]);

        public Task<IReadOnlyList<CatalogImportRootDto>> ListImportRootsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogImportRootDto>>([]);

        public Task<CatalogAppDto> InspectAndStoreAsync(
            CatalogInspectRequest request,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<(CatalogAppDto Entry, bool Created)> ImportUploadedIpaAsync(
            CatalogUploadRequest request,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<CatalogV2MutationResult> ImportFromRootV2Async(
            CatalogRootImportRequest request,
            string actor,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<CatalogV2MutationResult> ImportUploadedIpaV2Async(
            CatalogUploadV2Request request,
            string actor,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<CatalogV2MutationResult> ImportDownloadedGitHubIpaV2Async(
            CatalogGitHubImportRequest request,
            string actor,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<CatalogV2MutationResult?> TryReplayDownloadedGitHubIpaV2Async(
            CatalogGitHubImportReplayRequest request,
            string actor,
            CancellationToken ct = default) => throw new NotSupportedException();
    }
}
