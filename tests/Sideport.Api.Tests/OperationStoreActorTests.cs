using Sideport.Api.Operations;

namespace Sideport.Api.Tests;

public sealed class OperationStoreActorTests
{
    [Fact]
    public async Task Idempotency_UsesStableActorId_NotMutableOrSharedDisplayName()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "sideport-operation-actor-tests",
            Guid.NewGuid().ToString("N"),
            "operations.json");
        var store = new OperationStore(path);
        var firstActor = new OperationActorDto("oidc-user", "Family Admin", "sha256:issuer-a-subject-1");
        var otherActor = new OperationActorDto("oidc-user", "Family Admin", "sha256:issuer-b-subject-1");
        var renamedActor = new OperationActorDto("oidc-user", "Renamed Admin", firstActor.Id);

        (OperationRecordDto first, bool firstCreated) = await store.AddIfIdempotentMissingAsync(
            Record("op_first", firstActor));
        (OperationRecordDto other, bool otherCreated) = await store.AddIfIdempotentMissingAsync(
            Record("op_other", otherActor));
        (OperationRecordDto replay, bool replayCreated) = await store.AddIfIdempotentMissingAsync(
            Record("op_renamed", renamedActor));

        Assert.True(firstCreated);
        Assert.True(otherCreated);
        Assert.NotEqual(first.OperationId, other.OperationId);
        Assert.False(replayCreated);
        Assert.Equal(first.OperationId, replay.OperationId);
        Assert.Equal(2, (await store.ListAsync(limit: null)).Count);
    }

    [Fact]
    public async Task OwnershipSnapshots_RoundTripWhileLegacyJsonLoadsUnassigned()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "sideport-operation-ownership-tests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "operations.json");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, """
            [
              {
                "operationId": "op_legacy",
                "type": "refresh",
                "status": "succeeded",
                "createdAt": "2026-07-01T10:00:00Z",
                "updatedAt": "2026-07-01T10:00:00Z",
                "actor": { "kind": "api-token", "displayName": "api-token-client" },
                "attempt": 1,
                "target": { "deviceUdid": "LEGACY-UDID", "bundleId": "com.example.legacy" },
                "stages": [],
                "cancelable": false,
                "retryable": false,
                "rerunnable": false,
                "correlationId": "op_legacy"
              }
            ]
            """);

        var legacyStore = new OperationStore(path);
        OperationRecordDto legacy = Assert.Single(await legacyStore.ListAsync(limit: null));
        Assert.Null(legacy.ActorMemberId);
        Assert.Null(legacy.OwnerMemberId);

        (OperationRecordDto added, bool created) = await legacyStore.AddIfIdempotentMissingAsync(
            Record(
                "op_owned",
                new OperationActorDto("oidc-user", "Family member", "principal-hash"),
                actorMemberId: " member_actor ",
                ownerMemberId: " member_owner "));
        Assert.True(created);
        Assert.Equal("member_actor", added.ActorMemberId);
        Assert.Equal("member_owner", added.OwnerMemberId);

        var reloadedStore = new OperationStore(path);
        OperationRecordDto reloaded = (await reloadedStore.ListAsync(limit: null))
            .Single(operation => operation.OperationId == "op_owned");
        Assert.Equal("member_actor", reloaded.ActorMemberId);
        Assert.Equal("member_owner", reloaded.OwnerMemberId);
    }

    private static OperationRecordDto Record(
        string operationId,
        OperationActorDto actor,
        string? actorMemberId = null,
        string? ownerMemberId = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new OperationRecordDto(
            operationId,
            "refresh",
            "queued",
            now,
            StartedAt: null,
            now,
            CompletedAt: null,
            actor,
            IdempotencyKey: "same-browser-key",
            Attempt: 1,
            new OperationTargetDto("TEST-UDID", "com.example.app", Kind: "app"),
            Stages: [],
            Result: null,
            Error: null,
            Cancelable: true,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: operationId,
            ActorMemberId: actorMemberId,
            OwnerMemberId: ownerMemberId);
    }
}
