using Sideport.Api.Catalog;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Operations;
using Sideport.Orchestrator;

namespace Sideport.Api.WorkspaceAccess;

/// <summary>
/// Resolves the durable ownership and approved-catalog predicates used by the
/// Family HTTP surface. Callers still own the HTTP status code so an unknown
/// resource and another member's resource can share the same 404 response.
/// </summary>
internal sealed class FamilyResourceAccess(
    KnownDeviceStore devices,
    IAppRegistry registrations,
    IAppCatalog catalog,
    OperationStore operations)
{
    internal async Task<IReadOnlyList<KnownDeviceRecord>> ListOwnedAcceptedDevicesAsync(
        string memberId,
        CancellationToken ct = default) =>
        (await devices.ListAsync(ct).ConfigureAwait(false))
            .Where(device => IsOwnedAcceptedDevice(device, memberId))
            .ToArray();

    internal async Task<KnownDeviceRecord?> FindOwnedAcceptedDeviceAsync(
        string memberId,
        string? deviceUdid,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceUdid))
            return null;
        KnownDeviceRecord? device = await devices.FindAsync(deviceUdid, ct).ConfigureAwait(false);
        return device is not null && IsOwnedAcceptedDevice(device, memberId) ? device : null;
    }

    internal async Task<bool> HasAcceptedDeviceAsync(
        string memberId,
        CancellationToken ct = default) =>
        (await devices.ListAsync(ct).ConfigureAwait(false))
            .Any(device => IsOwnedAcceptedDevice(device, memberId));

    internal async Task<string?> FindDeviceOwnerMemberIdAsync(
        string? deviceUdid,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceUdid))
            return null;
        KnownDeviceRecord? device = await devices.FindAsync(deviceUdid, ct).ConfigureAwait(false);
        return device is not null &&
               string.Equals(device.InventoryState, "accepted", StringComparison.Ordinal)
            ? NormalizeOptional(device.OwnerMemberId)
            : null;
    }

    internal async Task<IReadOnlyList<CatalogAppV2Dto>> ListApprovedCatalogAsync(
        CancellationToken ct = default) =>
        (await catalog.ListV2Async(ct).ConfigureAwait(false))
            .Where(IsApprovedCatalogApp)
            .ToArray();

    internal async Task<CatalogAppV2Dto?> FindApprovedCatalogAppAsync(
        string? catalogAppId,
        string? expectedBundleId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(catalogAppId))
            return null;
        CatalogAppV2Dto? app = (await catalog.ListV2Async(ct).ConfigureAwait(false))
            .FirstOrDefault(item =>
                string.Equals(item.Id, catalogAppId, StringComparison.OrdinalIgnoreCase));
        if (app is null || !IsApprovedCatalogApp(app))
            return null;
        return string.IsNullOrWhiteSpace(expectedBundleId) ||
               string.Equals(app.BundleId, expectedBundleId, StringComparison.Ordinal)
            ? app
            : null;
    }

    internal async Task<IReadOnlyList<OwnedFamilyRegistration>> ListOwnedRegistrationsAsync(
        string memberId,
        CancellationToken ct = default)
    {
        IReadOnlyList<KnownDeviceRecord> allDevices = await devices.ListAsync(ct).ConfigureAwait(false);
        var ownedUdids = allDevices
            .Where(device => IsOwnedAcceptedDevice(device, memberId))
            .Select(device => device.Udid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<CatalogAppV2Dto> approved = await ListApprovedCatalogAsync(ct).ConfigureAwait(false);
        var approvedById = approved.ToDictionary(app => app.Id, StringComparer.OrdinalIgnoreCase);

        return (await registrations.ListAsync(ct).ConfigureAwait(false))
            .Where(registration => ownedUdids.Contains(registration.DeviceUdid))
            .Select(registration =>
            {
                CatalogAppV2Dto? app = string.IsNullOrWhiteSpace(registration.CatalogAppId)
                    ? null
                    : approvedById.GetValueOrDefault(registration.CatalogAppId);
                if (app is not null &&
                    !string.Equals(app.BundleId, registration.BundleId, StringComparison.Ordinal))
                {
                    app = null;
                }
                return new OwnedFamilyRegistration(registration, app);
            })
            .ToArray();
    }

    internal async Task<OwnedFamilyRegistration?> FindOwnedRegistrationAsync(
        string memberId,
        string? deviceUdid,
        string? bundleId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceUdid) || string.IsNullOrWhiteSpace(bundleId))
            return null;
        if (await FindOwnedAcceptedDeviceAsync(memberId, deviceUdid, ct).ConfigureAwait(false) is null)
            return null;
        AppRegistration? registration = await registrations.FindAsync(deviceUdid, bundleId, ct).ConfigureAwait(false);
        if (registration is null)
            return null;
        CatalogAppV2Dto? approved = await FindApprovedCatalogAppAsync(
            registration.CatalogAppId,
            registration.BundleId,
            ct).ConfigureAwait(false);
        return new OwnedFamilyRegistration(registration, approved);
    }

    internal async Task<OwnedFamilyRegistration?> FindOwnedApprovedRegistrationAsync(
        string memberId,
        string? deviceUdid,
        string? bundleId,
        CancellationToken ct = default)
    {
        OwnedFamilyRegistration? owned = await FindOwnedRegistrationAsync(
            memberId,
            deviceUdid,
            bundleId,
            ct).ConfigureAwait(false);
        return owned?.CatalogApp is null ? null : owned;
    }

    internal async Task<IReadOnlyList<OperationRecordDto>> ListOwnedOperationsAsync(
        string memberId,
        string? deviceUdid,
        string? bundleId,
        int? limit,
        CancellationToken ct = default)
    {
        // The durable store must not apply the requested limit before resource
        // scope is evaluated or another member's records become a count oracle.
        IEnumerable<OperationRecordDto> query = await operations.ListAsync(
            deviceUdid,
            bundleId,
            limit: null,
            ct).ConfigureAwait(false);
        query = query.Where(operation => IsOwnedOperation(operation, memberId));
        int take = Math.Clamp(limit ?? 25, 1, 100);
        return query.Take(take).ToArray();
    }

    internal async Task<OperationRecordDto?> FindOwnedOperationAsync(
        string memberId,
        string? operationId,
        bool requireOwnActor,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            return null;
        OperationRecordDto? operation = await operations.FindAsync(operationId, ct).ConfigureAwait(false);
        if (operation is null || !IsOwnedOperation(operation, memberId))
            return null;
        if (requireOwnActor &&
            !string.Equals(operation.ActorMemberId, memberId, StringComparison.Ordinal))
        {
            return null;
        }
        return operation;
    }

    internal async Task<bool> HasEnrollmentReplayAsync(
        string memberId,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return false;
        IReadOnlyList<OperationRecordDto> records = await operations.ListAsync(
            limit: null,
            ct: ct).ConfigureAwait(false);
        return records.Any(operation =>
            string.Equals(operation.Type, DeviceEnrollmentService.OperationType, StringComparison.Ordinal) &&
            string.Equals(operation.ActorMemberId, memberId, StringComparison.Ordinal) &&
            string.Equals(operation.OwnerMemberId, memberId, StringComparison.Ordinal) &&
            string.Equals(operation.IdempotencyKey, idempotencyKey.Trim(), StringComparison.Ordinal));
    }

    internal async Task<string?> FindOperationOwnerMemberIdAsync(
        string? operationId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            return null;
        OperationRecordDto? operation = await operations.FindAsync(operationId, ct).ConfigureAwait(false);
        return NormalizeOptional(operation?.OwnerMemberId);
    }

    internal async Task<bool> IsApprovedMutableOperationAsync(
        string memberId,
        OperationRecordDto operation,
        CancellationToken ct = default)
    {
        if (!IsOwnedOperation(operation, memberId) ||
            !string.Equals(operation.ActorMemberId, memberId, StringComparison.Ordinal))
        {
            return false;
        }

        // Enrollment is the one legitimate operation before an accepted device
        // or app registration exists. Every app mutation must resolve both.
        if (string.Equals(operation.Type, DeviceEnrollmentService.OperationType, StringComparison.Ordinal))
            return true;
        if (string.IsNullOrWhiteSpace(operation.Target.DeviceUdid) ||
            string.IsNullOrWhiteSpace(operation.Target.BundleId))
        {
            return false;
        }
        return await FindOwnedApprovedRegistrationAsync(
            memberId,
            operation.Target.DeviceUdid,
            operation.Target.BundleId,
            ct).ConfigureAwait(false) is not null;
    }

    internal async Task<FamilyOperationDto> ProjectOperationAsync(
        string memberId,
        OperationRecordDto operation,
        CancellationToken ct = default) =>
        FamilyResourceProjections.Operation(
            operation,
            await IsApprovedMutableOperationAsync(memberId, operation, ct).ConfigureAwait(false));

    internal static bool IsOwnedAcceptedDevice(KnownDeviceRecord device, string memberId) =>
        string.Equals(device.InventoryState, "accepted", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(device.OwnerMemberId) &&
        string.Equals(device.OwnerMemberId, memberId, StringComparison.Ordinal);

    internal static bool IsOwnedOperation(OperationRecordDto operation, string memberId) =>
        !string.IsNullOrWhiteSpace(operation.OwnerMemberId) &&
        string.Equals(operation.OwnerMemberId, memberId, StringComparison.Ordinal);

    internal static bool IsApprovedCatalogApp(CatalogAppV2Dto app) =>
        app.CatalogVersion > 0 &&
        string.Equals(app.Status, "ready", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(app.BundleId) &&
        !string.IsNullOrWhiteSpace(app.Sha256);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record OwnedFamilyRegistration(
    AppRegistration Registration,
    CatalogAppV2Dto? CatalogApp);
