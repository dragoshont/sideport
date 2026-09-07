namespace Sideport.Orchestrator;

/// <summary>
/// Extra safety hooks a superseding-renewal recovery attaches to a single
/// <see cref="RefreshExecutionPolicy.Recovery"/>.
/// The orchestrator copies and hashes a private, pinned artifact snapshot before
/// signing (so a mutable registration path cannot be swapped after the preflight
/// hash), and calls <see cref="PersistMutationStartAsync"/> AFTER signing but
/// BEFORE the device install so a store failure fails the attempt closed without
/// mutating the iPhone.
/// </summary>
public sealed record RefreshRecoveryPlan(
    string ExpectedArtifactSha256,
    Func<RefreshMutationCheckpoint, CancellationToken, Task> PersistMutationStartAsync);

/// <summary>
/// The durable checkpoint the orchestrator hands back right before it installs.
/// The prepared expiry and the exact hash of the pinned snapshot let the caller
/// prove which artifact/expiry a device mutation started with, without ever
/// fabricating a verified-operation link before the device is read back.
/// </summary>
public sealed record RefreshMutationCheckpoint(
    string DeviceUdid,
    string BundleId,
    DateTimeOffset PreparedExpiresAt,
    string PinnedArtifactSha256,
    string? ArtifactSnapshotId = null);
