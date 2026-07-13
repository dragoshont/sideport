using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Operations;
using Sideport.Orchestrator;

namespace Sideport.Api.WorkspaceAccess;

internal sealed record WorkspaceImpactSnapshot(
    string TargetMemberId,
    long TargetMemberVersion,
    int MemberCount,
    int OwnedDeviceCount,
    int UnassignedDeviceCount,
    int RegistrationCount,
    int QueuedOperationCount,
    int RunningOperationCount,
    int SchedulerEffectCount,
    string ImpactVersion,
    DateTimeOffset ExpiresAt);

internal sealed class WorkspaceImpactService
{
    private static readonly TimeSpan ImpactLifetime = TimeSpan.FromMinutes(10);

    private readonly WorkspaceAccessStore _workspace;
    private readonly KnownDeviceStore _devices;
    private readonly IAppRegistry _registrations;
    private readonly OperationStore _operations;
    private readonly OperationService _operationService;
    private readonly TimeProvider _time;

    internal WorkspaceImpactService(
        WorkspaceAccessStore workspace,
        KnownDeviceStore devices,
        IAppRegistry registrations,
        OperationStore operations,
        OperationService operationService,
        TimeProvider? timeProvider = null)
    {
        _workspace = workspace;
        _devices = devices;
        _registrations = registrations;
        _operations = operations;
        _operationService = operationService;
        _time = timeProvider ?? TimeProvider.System;
    }

    internal async Task<WorkspaceImpactSnapshot> CreateOwnerReplacementAsync(
        CancellationToken ct = default)
    {
        WorkspaceAccessDocument document = await ReadActiveWorkspaceAsync(ct).ConfigureAwait(false);
        string ownerMemberId = document.Workspace.OwnerMemberId
            ?? throw new WorkspaceAccessException("workspace-store-unavailable", "The active Owner is unavailable.");
        WorkspaceMemberRecord owner = document.Members.First(item => item.MemberId == ownerMemberId);
        return await CalculateAsync(
            "owner-replacement",
            document,
            owner,
            _time.GetUtcNow().Add(ImpactLifetime),
            ct).ConfigureAwait(false);
    }

    internal async Task<WorkspaceImpactSnapshot> VerifyOwnerReplacementAsync(
        string expectedOwnerMemberId,
        string impactVersion,
        CancellationToken ct = default)
    {
        WorkspaceAccessDocument document = await ReadActiveWorkspaceAsync(ct).ConfigureAwait(false);
        return await VerifyOwnerReplacementAsync(
            document,
            expectedOwnerMemberId,
            impactVersion,
            ct).ConfigureAwait(false);
    }

    internal async Task<WorkspaceImpactSnapshot> VerifyOwnerReplacementAsync(
        WorkspaceAccessDocument document,
        string expectedOwnerMemberId,
        string impactVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        DateTimeOffset expiresAt = ParseExpiry(impactVersion, "owner-replacement-preflight-stale");
        if (document.Workspace.State != WorkspaceLifecycleState.Active)
            throw Stale("owner-replacement-preflight-stale");
        if (!string.Equals(document.Workspace.OwnerMemberId, expectedOwnerMemberId, StringComparison.Ordinal))
            throw Stale("owner-replacement-preflight-stale");
        WorkspaceMemberRecord owner = document.Members.FirstOrDefault(item => item.MemberId == expectedOwnerMemberId)
            ?? throw new WorkspaceAccessException(
                "workspace-store-unavailable",
                "The active Owner is unavailable.");
        WorkspaceImpactSnapshot current = await CalculateAsync(
            "owner-replacement",
            document,
            owner,
            expiresAt,
            ct).ConfigureAwait(false);
        EnsureVersion(impactVersion, current.ImpactVersion, "owner-replacement-preflight-stale");
        return current;
    }

    internal async Task<WorkspaceImpactSnapshot> CreateOffboardingAsync(
        string memberId,
        CancellationToken ct = default)
    {
        WorkspaceAccessDocument document = await ReadActiveWorkspaceAsync(ct).ConfigureAwait(false);
        WorkspaceMemberRecord member = FindFamilyMember(document, memberId);
        return await CalculateAsync(
            "member-offboarding",
            document,
            member,
            _time.GetUtcNow().Add(ImpactLifetime),
            ct).ConfigureAwait(false);
    }

    internal async Task<WorkspaceImpactSnapshot> VerifyOffboardingAsync(
        string memberId,
        long expectedMemberVersion,
        string impactVersion,
        CancellationToken ct = default)
    {
        WorkspaceAccessDocument document = await ReadActiveWorkspaceAsync(ct).ConfigureAwait(false);
        return await VerifyOffboardingAsync(
            document,
            memberId,
            expectedMemberVersion,
            impactVersion,
            ct).ConfigureAwait(false);
    }

    internal async Task<WorkspaceImpactSnapshot> VerifyOffboardingAsync(
        WorkspaceAccessDocument document,
        string memberId,
        long expectedMemberVersion,
        string impactVersion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        DateTimeOffset expiresAt = ParseExpiry(impactVersion, "offboarding-preflight-stale");
        if (document.Workspace.State != WorkspaceLifecycleState.Active)
            throw Stale("offboarding-preflight-stale");
        WorkspaceMemberRecord member = FindFamilyMember(document, memberId);
        if (member.Version != expectedMemberVersion || member.Status != WorkspaceMemberStatus.Suspended)
            throw Stale("offboarding-preflight-stale");
        WorkspaceImpactSnapshot current = await CalculateAsync(
            "member-offboarding",
            document,
            member,
            expiresAt,
            ct).ConfigureAwait(false);
        EnsureVersion(impactVersion, current.ImpactVersion, "offboarding-preflight-stale");
        return current;
    }

    internal static WorkspaceAuditImpact ToAuditImpact(WorkspaceImpactSnapshot impact)
    {
        ArgumentNullException.ThrowIfNull(impact);
        return new WorkspaceAuditImpact(
            impact.MemberCount,
            impact.OwnedDeviceCount,
            impact.RegistrationCount,
            impact.QueuedOperationCount,
            impact.RunningOperationCount,
            impact.SchedulerEffectCount,
            impact.ImpactVersion);
    }

    internal async Task CancelQueuedWorkBestEffortAsync(
        string memberId,
        CancellationToken ct = default)
    {
        IReadOnlyList<OperationRecordDto> operations = await _operations.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        foreach (OperationRecordDto operation in operations.Where(item =>
                     string.Equals(item.OwnerMemberId, memberId, StringComparison.Ordinal) &&
                     string.Equals(item.Status, "queued", StringComparison.Ordinal) &&
                     item.Cancelable))
        {
            try
            {
                await _operationService.CancelAsync(
                    operation.OperationId,
                    "Membership access was disabled before work started.",
                    ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Authorization already fails closed from the committed member
                // state. Cancellation is recovery hygiene, never rollback.
            }
        }
    }

    private async Task<WorkspaceImpactSnapshot> CalculateAsync(
        string kind,
        WorkspaceAccessDocument document,
        WorkspaceMemberRecord target,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        if (expiresAt <= _time.GetUtcNow())
            throw Stale(kind == "owner-replacement" ? "owner-replacement-preflight-stale" : "offboarding-preflight-stale");

        IReadOnlyList<KnownDeviceRecord> devices;
        IReadOnlyList<AppRegistration> registrations;
        IReadOnlyList<OperationRecordDto> operations;
        try
        {
            devices = await _devices.ListAsync(ct).ConfigureAwait(false);
            registrations = await _registrations.ListAsync(ct).ConfigureAwait(false);
            operations = await _operations.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is KnownDeviceStoreException or OperationStoreException or
                                      IOException or UnauthorizedAccessException)
        {
            throw new WorkspaceAccessException(
                "workspace-store-unavailable",
                "Sideport could not safely calculate the current impact.",
                error);
        }
        HashSet<string> ownedDeviceIds = devices
            .Where(item => string.Equals(item.OwnerMemberId, target.MemberId, StringComparison.Ordinal))
            .Select(item => item.Udid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AppRegistration[] ownedRegistrations = registrations
            .Where(item => ownedDeviceIds.Contains(item.DeviceUdid))
            .ToArray();
        OperationRecordDto[] ownedOperations = operations
            .Where(item => string.Equals(item.OwnerMemberId, target.MemberId, StringComparison.Ordinal))
            .ToArray();

        string canonical = string.Join('\n',
        [
            kind,
            document.Workspace.WorkspaceId,
            target.MemberId,
            target.Version.ToString(CultureInfo.InvariantCulture),
            expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            .. document.Members.OrderBy(item => item.MemberId, StringComparer.Ordinal)
                .Select(item => $"m:{item.MemberId}:{item.Role}:{item.Status}:{item.Version}"),
            .. devices.OrderBy(item => item.Udid, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"d:{item.Udid}:{item.OwnerMemberId ?? "-"}:{item.InventoryState}:{item.AcceptedAt?.UtcTicks ?? 0}"),
            .. registrations.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"r:{item.Key}:{item.Lifecycle}:{item.CatalogVersion ?? 0}:{item.LastVerifiedOperationId ?? "-"}"),
            .. operations.OrderBy(item => item.OperationId, StringComparer.Ordinal)
                .Select(item => $"o:{item.OperationId}:{item.OwnerMemberId ?? "-"}:{item.ActorMemberId ?? "-"}:{item.Status}:{item.UpdatedAt.UtcTicks}"),
        ]);
        byte[] key = Encoding.UTF8.GetBytes(document.Workspace.SecurityEpoch);
        byte[] digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical));
        string impactVersion = $"impact_v1_{expiresAt.ToUnixTimeSeconds()}_{Base64Url(digest)}";
        return new WorkspaceImpactSnapshot(
            target.MemberId,
            target.Version,
            document.Members.Count,
            ownedDeviceIds.Count,
            devices.Count(item => string.IsNullOrWhiteSpace(item.OwnerMemberId)),
            ownedRegistrations.Length,
            ownedOperations.Count(item => string.Equals(item.Status, "queued", StringComparison.Ordinal)),
            ownedOperations.Count(item => string.Equals(item.Status, "running", StringComparison.Ordinal)),
            ownedRegistrations.Count(item => string.Equals(item.Lifecycle, "active", StringComparison.Ordinal)),
            impactVersion,
            expiresAt);
    }

    private async Task<WorkspaceAccessDocument> ReadActiveWorkspaceAsync(CancellationToken ct)
    {
        WorkspaceAccessDocument? document = await _workspace.ReadAsync(ct).ConfigureAwait(false);
        if (document?.Workspace.State != WorkspaceLifecycleState.Active)
            throw new WorkspaceAccessException("workspace-bootstrap-required", "The workspace is not active.");
        return document;
    }

    private static WorkspaceMemberRecord FindFamilyMember(
        WorkspaceAccessDocument document,
        string memberId)
    {
        WorkspaceMemberRecord? member = document.Members.FirstOrDefault(item =>
            string.Equals(item.MemberId, memberId, StringComparison.Ordinal));
        if (member?.Role != WorkspaceMemberRole.Family)
            throw new WorkspaceAccessException("resource-not-found", "The member was not found.");
        return member;
    }

    private DateTimeOffset ParseExpiry(string impactVersion, string error)
    {
        const string prefix = "impact_v1_";
        int digestSeparator = impactVersion.IndexOf('_', prefix.Length);
        if (!impactVersion.StartsWith(prefix, StringComparison.Ordinal) ||
            digestSeparator <= prefix.Length ||
            digestSeparator == impactVersion.Length - 1 ||
            !long.TryParse(
                impactVersion.AsSpan(prefix.Length, digestSeparator - prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long unixSeconds))
        {
            throw Stale(error);
        }

        DateTimeOffset expiresAt;
        try
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Stale(error);
        }
        if (expiresAt <= _time.GetUtcNow() || expiresAt - _time.GetUtcNow() > ImpactLifetime)
            throw Stale(error);
        return expiresAt;
    }

    private static void EnsureVersion(string presented, string current, string error)
    {
        byte[] left = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        byte[] right = SHA256.HashData(Encoding.UTF8.GetBytes(current));
        if (!CryptographicOperations.FixedTimeEquals(left, right))
            throw Stale(error);
    }

    private static WorkspaceAccessException Stale(string code) =>
        new(code, "The impact has changed. Review it again before continuing.");

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
