using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.DeviceInventory;

public sealed class KnownDeviceService(
    KnownDeviceStore store,
    IDeviceController devices,
    IAppRegistry registry)
{
    private const int AppSlotLimit = 3;

    public async Task<IReadOnlyList<KnownDeviceDto>> ListAsync(bool includeReachable = true, CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<KnownDeviceRecord> stored = await store.ListAsync(ct).ConfigureAwait(false);
        Dictionary<string, DeviceInfo> reachable = includeReachable
            ? await ReachableByUdidAsync(ct).ConfigureAwait(false)
            : new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct).ConfigureAwait(false);

        var byUdid = new Dictionary<string, KnownDeviceRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (KnownDeviceRecord record in stored)
            byUdid[record.Udid] = record;

        foreach (DeviceInfo device in reachable.Values)
        {
            KnownDeviceRecord? previous = byUdid.GetValueOrDefault(device.Udid);
            byUdid[device.Udid] = OverlayReachable(previous, device, now);
        }

        return byUdid.Values
            .Select(record => ToDto(record, reachable.GetValueOrDefault(record.Udid), registrations, now))
            .OrderBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Udid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<(KnownDeviceDto Device, bool Created)> UpsertAsync(KnownDeviceUpsertRequest request, CancellationToken ct = default)
    {
        ValidateUdid(request.Udid);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Dictionary<string, DeviceInfo> reachable = await ReachableByUdidAsync(ct).ConfigureAwait(false);
        KnownDeviceRecord? previous = await store.FindAsync(request.Udid, ct).ConfigureAwait(false);
        DeviceInfo? current = reachable.GetValueOrDefault(request.Udid);
        KnownDeviceRecord merged = current is null
            ? MergeManual(previous, request, now)
            : PersistReachable(previous, current, now) with
            {
                DisplayName = FirstNonBlank(request.DisplayName, previous?.DisplayName, current.Name, current.Udid),
                Owner = NormalizeOptional(request.Owner) ?? previous?.Owner,
                Notes = NormalizeOptional(request.Notes) ?? previous?.Notes,
                UpdatedAt = now,
            };

        (KnownDeviceRecord saved, bool created) = await store.UpsertAsync(merged, ct).ConfigureAwait(false);
        IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct).ConfigureAwait(false);
        return (ToDto(saved, current, registrations, now), created);
    }

    public async Task<KnownDeviceDto?> PatchAsync(string udid, KnownDevicePatchRequest request, CancellationToken ct = default)
    {
        ValidateUdid(udid);
        KnownDeviceRecord? existing = await store.FindAsync(udid, ct).ConfigureAwait(false);
        if (existing is null)
            return null;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        KnownDeviceRecord patched = existing with
        {
            DisplayName = FirstNonBlank(request.DisplayName, existing.DisplayName, existing.Udid),
            Owner = request.Owner is null ? existing.Owner : NormalizeOptional(request.Owner),
            Notes = request.Notes is null ? existing.Notes : NormalizeOptional(request.Notes),
            UpdatedAt = now,
        };
        (KnownDeviceRecord saved, _) = await store.UpsertAsync(patched, ct).ConfigureAwait(false);
        Dictionary<string, DeviceInfo> reachable = await ReachableByUdidAsync(ct).ConfigureAwait(false);
        IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct).ConfigureAwait(false);
        return ToDto(saved, reachable.GetValueOrDefault(saved.Udid), registrations, now);
    }

    public async Task<(bool Removed, int RegistrationCount)> RemoveAsync(string udid, CancellationToken ct = default)
    {
        ValidateUdid(udid);
        IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct).ConfigureAwait(false);
        int registrationCount = registrations.Count(app => string.Equals(app.DeviceUdid, udid, StringComparison.OrdinalIgnoreCase));
        if (registrationCount > 0)
            return (false, registrationCount);

        bool removed = await store.RemoveAsync(udid, ct).ConfigureAwait(false);
        return (removed, 0);
    }

    private async Task<Dictionary<string, DeviceInfo>> ReachableByUdidAsync(CancellationToken ct)
    {
        IReadOnlyList<DeviceInfo> reachable = await devices.ListDevicesAsync(ct).ConfigureAwait(false);
        return reachable
            .GroupBy(device => device.Udid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(device => device.Connection == DeviceConnection.Usb ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);
    }

    private static KnownDeviceRecord OverlayReachable(KnownDeviceRecord? previous, DeviceInfo current, DateTimeOffset now) =>
        new(
            current.Udid,
            FirstNonBlank(previous?.DisplayName, current.Name, current.Udid),
            current.ProductType,
            current.OsVersion,
            ConnectionValue(current.Connection),
            previous?.FirstSeenAt ?? now,
            previous?.LastSeenAt,
            previous?.LastSeenSource ?? "current-poll",
            now,
            "trusted",
            previous?.Owner,
            previous?.Notes,
            previous?.UpdatedAt ?? now);

    private static KnownDeviceRecord PersistReachable(KnownDeviceRecord? previous, DeviceInfo current, DateTimeOffset now) =>
        new(
            current.Udid,
            FirstNonBlank(previous?.DisplayName, current.Name, current.Udid),
            current.ProductType,
            current.OsVersion,
            ConnectionValue(current.Connection),
            previous?.FirstSeenAt ?? now,
            now,
            "live-poll",
            now,
            "trusted",
            previous?.Owner,
            previous?.Notes,
            now);

    private static KnownDeviceRecord MergeManual(KnownDeviceRecord? previous, KnownDeviceUpsertRequest request, DateTimeOffset now) =>
        new(
            request.Udid.Trim(),
            FirstNonBlank(request.DisplayName, previous?.DisplayName, request.Udid),
            previous?.ProductType,
            previous?.OsVersion,
            previous?.Connection ?? "unknown",
            previous?.FirstSeenAt ?? now,
            previous?.LastSeenAt,
            previous?.LastSeenSource ?? "manual",
            previous?.CurrentPollAt,
            previous?.TrustState ?? "unknown",
            NormalizeOptional(request.Owner) ?? previous?.Owner,
            NormalizeOptional(request.Notes) ?? previous?.Notes,
            now);

    private static KnownDeviceDto ToDto(KnownDeviceRecord record, DeviceInfo? reachable, IReadOnlyList<AppRegistration> registrations, DateTimeOffset now)
    {
        bool isReachable = reachable is not null;
        int slotsUsed = registrations.Count(app => string.Equals(app.DeviceUdid, record.Udid, StringComparison.OrdinalIgnoreCase));
        string healthState = isReachable ? "healthy" : record.LastSeenAt is null ? "warning" : "offline";
        string healthReason = isReachable
            ? "Reachable in current poll."
            : record.LastSeenAt is null
                ? "Known manually, but Sideport has not seen this device in a live poll yet."
                : "Known device is not reachable in the current poll.";
        string? nextAction = isReachable ? null : "Connect the trusted iPhone over USB or Wi-Fi, then refresh device discovery.";

        return new KnownDeviceDto(
            record.Udid,
            record.DisplayName,
            reachable?.ProductType ?? record.ProductType,
            reachable?.OsVersion ?? record.OsVersion,
            isReachable ? ConnectionValue(reachable!.Connection) : record.Connection == "unknown" ? "offline" : record.Connection,
            record.FirstSeenAt,
            record.LastSeenAt,
            record.LastSeenSource,
            isReachable ? now : record.CurrentPollAt,
            record.TrustState,
            new KnownDeviceHealthDto(healthState, healthReason, "derived", isReachable ? now : record.UpdatedAt, nextAction),
            new KnownDeviceAppSlotsDto(slotsUsed, AppSlotLimit),
            record.Owner,
            record.Notes);
    }

    private static void ValidateUdid(string? udid)
    {
        if (string.IsNullOrWhiteSpace(udid))
            throw new ArgumentException("Device UDID is required.", nameof(udid));
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Unknown device";

    private static string ConnectionValue(DeviceConnection connection) => connection == DeviceConnection.Wifi ? "wifi" : "usb";
}
