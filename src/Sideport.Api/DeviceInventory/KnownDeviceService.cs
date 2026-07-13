using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.DeviceInventory;

public sealed class KnownDeviceService
{
    private const int AppSlotLimit = 3;
    private readonly KnownDeviceStore _store;
    private readonly IDeviceController _devices;
    private readonly IAppRegistry _registry;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _trustEvidenceMaxAge;

    public KnownDeviceService(
        KnownDeviceStore store,
        IDeviceController devices,
        IAppRegistry registry,
        TimeProvider? timeProvider = null,
        TimeSpan? trustEvidenceMaxAge = null)
    {
        _store = store;
        _devices = devices;
        _registry = registry;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _trustEvidenceMaxAge = trustEvidenceMaxAge ?? TimeSpan.FromMinutes(1);
        if (_trustEvidenceMaxAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(trustEvidenceMaxAge));
    }

    public async Task<IReadOnlyList<KnownDeviceDto>> ListAsync(bool includeReachable = true, CancellationToken ct = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        IReadOnlyList<KnownDeviceRecord> stored = await _store.ListAsync(ct).ConfigureAwait(false);
        Dictionary<string, DeviceInfo> reachable = includeReachable
            ? await ReachableByUdidAsync(ct).ConfigureAwait(false)
            : new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<AppRegistration> registrations = await _registry.ListAsync(ct).ConfigureAwait(false);

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
        DateTimeOffset now = _timeProvider.GetUtcNow();
        // Manual inventory is deliberately usable while usbmux/netmux is down.
        // A discovery failure means "not currently observed"; it must not turn
        // a metadata-only record into a 500 or imply trust/acceptance.
        Dictionary<string, DeviceInfo> reachable;
        try
        {
            reachable = await ReachableByUdidAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            reachable = new Dictionary<string, DeviceInfo>(StringComparer.OrdinalIgnoreCase);
        }
        KnownDeviceRecord? previous = await _store.FindAsync(request.Udid, ct).ConfigureAwait(false);
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

        (KnownDeviceRecord saved, bool created) = await _store.UpsertAsync(merged, ct).ConfigureAwait(false);
        IReadOnlyList<AppRegistration> registrations = await _registry.ListAsync(ct).ConfigureAwait(false);
        return (ToDto(saved, current, registrations, now), created);
    }

    public async Task<KnownDeviceDto?> PatchAsync(string udid, KnownDevicePatchRequest request, CancellationToken ct = default)
    {
        ValidateUdid(udid);
        KnownDeviceRecord? existing = await _store.FindAsync(udid, ct).ConfigureAwait(false);
        if (existing is null)
            return null;

        DateTimeOffset now = _timeProvider.GetUtcNow();
        KnownDeviceRecord patched = existing with
        {
            DisplayName = FirstNonBlank(request.DisplayName, existing.DisplayName, existing.Udid),
            Owner = request.Owner is null ? existing.Owner : NormalizeOptional(request.Owner),
            Notes = request.Notes is null ? existing.Notes : NormalizeOptional(request.Notes),
            UpdatedAt = now,
        };
        (KnownDeviceRecord saved, _) = await _store.UpsertAsync(patched, ct).ConfigureAwait(false);
        Dictionary<string, DeviceInfo> reachable = await ReachableByUdidAsync(ct).ConfigureAwait(false);
        IReadOnlyList<AppRegistration> registrations = await _registry.ListAsync(ct).ConfigureAwait(false);
        return ToDto(saved, reachable.GetValueOrDefault(saved.Udid), registrations, now);
    }

    public async Task<KnownDeviceDto> AcceptAsync(
        DeviceInfo current,
        DeviceTrustProbe trust,
        string acceptedBy,
        string enrollmentOperationId,
        CancellationToken ct = default) =>
        await AcceptAsync(
            current,
            trust,
            acceptedBy,
            enrollmentOperationId,
            ownerMemberId: null,
            ct: ct).ConfigureAwait(false);

    public async Task<KnownDeviceDto> AcceptAsync(
        DeviceInfo current,
        DeviceTrustProbe trust,
        string acceptedBy,
        string enrollmentOperationId,
        string? ownerMemberId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(trust);
        ValidateUdid(current.Udid);
        if (!string.Equals(current.Udid, trust.Udid, StringComparison.OrdinalIgnoreCase))
            throw new KnownDeviceAcceptanceException("device-trust-mismatch", "The trust probe does not belong to the selected device.");
        if (string.IsNullOrWhiteSpace(acceptedBy))
            throw new ArgumentException("The accepting actor is required.", nameof(acceptedBy));
        if (string.IsNullOrWhiteSpace(enrollmentOperationId))
            throw new ArgumentException("The enrollment operation is required.", nameof(enrollmentOperationId));

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (current.Connection != DeviceConnection.Usb || trust.Connection != DeviceConnection.Usb)
            throw new KnownDeviceAcceptanceException("device-usb-required", "A current USB connection is required to accept an iPhone.");
        if (!string.Equals(trust.TrustState, "trusted", StringComparison.OrdinalIgnoreCase) || !trust.UsableForInstall)
            throw new KnownDeviceAcceptanceException("device-lockdown-untrusted", "A successful lockdown handshake is required to accept an iPhone.");
        if (trust.LockdownCheckedAt > now.AddSeconds(5) || now - trust.LockdownCheckedAt > _trustEvidenceMaxAge)
            throw new KnownDeviceAcceptanceException("device-trust-evidence-stale", "The lockdown result is no longer current. Check the iPhone again.");

        string? normalizedOwnerMemberId = NormalizeOptional(ownerMemberId);
        KnownDeviceRecord? previous = await _store.FindAsync(current.Udid, ct).ConfigureAwait(false);
        bool sameCompletedEnrollment =
            string.Equals(previous?.InventoryState, "accepted", StringComparison.Ordinal) &&
            string.Equals(previous?.EnrollmentOperationId, enrollmentOperationId, StringComparison.Ordinal);
        if (string.Equals(previous?.InventoryState, "accepted", StringComparison.Ordinal) && !sameCompletedEnrollment)
        {
            throw new KnownDeviceAcceptanceException(
                "device-already-accepted",
                "This iPhone is already accepted by Sideport and cannot be reassigned through enrollment.");
        }
        if (!string.IsNullOrWhiteSpace(previous?.OwnerMemberId) &&
            normalizedOwnerMemberId is not null &&
            !string.Equals(previous.OwnerMemberId, normalizedOwnerMemberId, StringComparison.Ordinal))
        {
            throw new KnownDeviceAcceptanceException(
                "device-owner-conflict",
                "This iPhone is already assigned to another Sideport member.");
        }
        KnownDeviceRecord accepted = PersistReachable(previous, current, now, trust) with
        {
            InventoryState = "accepted",
            AcceptedAt = sameCompletedEnrollment ? previous!.AcceptedAt : now,
            AcceptedBy = sameCompletedEnrollment ? previous!.AcceptedBy : acceptedBy.Trim(),
            EnrollmentOperationId = enrollmentOperationId.Trim(),
            OwnerMemberId = previous?.OwnerMemberId ?? normalizedOwnerMemberId,
            UpdatedAt = now,
        };

        (KnownDeviceRecord saved, _) = await _store.UpsertAsync(accepted, ct).ConfigureAwait(false);
        IReadOnlyList<AppRegistration> registrations = await _registry.ListAsync(ct).ConfigureAwait(false);
        return ToDto(saved, current with
        {
            TrustState = trust.TrustState,
            TrustReason = trust.TrustReason,
            LockdownCheckedAt = trust.LockdownCheckedAt,
            UsableForInstall = trust.UsableForInstall,
        }, registrations, now);
    }

    public async Task<(bool Removed, int RegistrationCount)> RemoveAsync(string udid, CancellationToken ct = default)
    {
        ValidateUdid(udid);
        IReadOnlyList<AppRegistration> registrations = await _registry.ListAsync(ct).ConfigureAwait(false);
        int registrationCount = registrations.Count(app => string.Equals(app.DeviceUdid, udid, StringComparison.OrdinalIgnoreCase));
        if (registrationCount > 0)
            return (false, registrationCount);

        bool removed = await _store.RemoveAsync(udid, ct).ConfigureAwait(false);
        return (removed, 0);
    }

    private async Task<Dictionary<string, DeviceInfo>> ReachableByUdidAsync(CancellationToken ct)
    {
        IReadOnlyList<DeviceInfo> reachable = await _devices.ListDevicesAsync(ct).ConfigureAwait(false);
        return reachable
            .GroupBy(device => device.Udid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(device => device.Connection == DeviceConnection.Usb ? 0 : 1).First(), StringComparer.OrdinalIgnoreCase);
    }

    private static KnownDeviceRecord OverlayReachable(KnownDeviceRecord? previous, DeviceInfo current, DateTimeOffset now) =>
        BuildReachableRecord(previous, current, now, persistLastSeen: false, trust: null);

    private static KnownDeviceRecord PersistReachable(
        KnownDeviceRecord? previous,
        DeviceInfo current,
        DateTimeOffset now,
        DeviceTrustProbe? trust = null) =>
        BuildReachableRecord(previous, current, now, persistLastSeen: true, trust);

    private static KnownDeviceRecord BuildReachableRecord(
        KnownDeviceRecord? previous,
        DeviceInfo current,
        DateTimeOffset now,
        bool persistLastSeen,
        DeviceTrustProbe? trust)
    {
        string trustState = NormalizeTrustState(trust?.TrustState ?? current.TrustState);
        return new KnownDeviceRecord(
            current.Udid,
            FirstNonBlank(previous?.DisplayName, current.Name, current.Udid),
            current.ProductType,
            current.OsVersion,
            ConnectionValue(current.Connection),
            previous?.FirstSeenAt ?? now,
            persistLastSeen ? now : previous?.LastSeenAt,
            persistLastSeen ? "live-poll" : previous?.LastSeenSource ?? "current-poll",
            now,
            trustState,
            previous?.Owner,
            previous?.Notes,
            persistLastSeen ? now : previous?.UpdatedAt ?? now,
            previous?.InventoryState ?? "discovered",
            previous?.AcceptedAt,
            previous?.AcceptedBy,
            previous?.EnrollmentOperationId,
            trust?.TrustReason ?? current.TrustReason,
            trust?.LockdownCheckedAt ?? current.LockdownCheckedAt,
            (trust?.UsableForInstall ?? current.UsableForInstall) && trustState == "trusted",
            previous?.OwnerMemberId);
    }

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
            "unknown",
            NormalizeOptional(request.Owner) ?? previous?.Owner,
            NormalizeOptional(request.Notes) ?? previous?.Notes,
            now,
            previous?.InventoryState ?? "discovered",
            previous?.AcceptedAt,
            previous?.AcceptedBy,
            previous?.EnrollmentOperationId,
            previous is null ? "This device has not been checked over USB." : previous.TrustReason,
            previous?.LockdownCheckedAt,
            false,
            previous?.OwnerMemberId);

    private static KnownDeviceDto ToDto(KnownDeviceRecord record, DeviceInfo? reachable, IReadOnlyList<AppRegistration> registrations, DateTimeOffset now)
    {
        bool isReachable = reachable is not null;
        int slotsUsed = registrations.Count(app => string.Equals(app.DeviceUdid, record.Udid, StringComparison.OrdinalIgnoreCase));
        string trustState = isReachable ? NormalizeTrustState(reachable!.TrustState) : "unknown";
        string? trustReason = isReachable ? reachable!.TrustReason : "The device is not reachable in the current poll.";
        DateTimeOffset? lockdownCheckedAt = isReachable ? reachable!.LockdownCheckedAt : record.LockdownCheckedAt;
        bool usableForInstall = isReachable && trustState == "trusted" && reachable!.UsableForInstall;
        bool supportedForFirstInstall = usableForInstall && reachable!.Connection == DeviceConnection.Usb;
        string healthState = !isReachable
            ? record.LastSeenAt is null ? "warning" : "offline"
            : trustState switch
            {
                "trusted" when usableForInstall => "healthy",
                "locked" or "untrusted" => "blocked",
                "error" => "failed",
                _ => "warning",
            };
        string healthReason = isReachable
            ? trustState == "trusted" && usableForInstall
                ? "Reachable with a verified lockdown session."
                : trustReason ?? "Reachable, but Sideport could not verify device trust."
            : record.LastSeenAt is null
                ? "Known manually, but Sideport has not seen this device in a live poll yet."
                : "Known device is not reachable in the current poll.";
        string? nextAction = healthState == "healthy"
            ? null
            : isReachable
                ? "Unlock the iPhone and complete Trust This Computer over USB."
                : "Connect the iPhone over USB, unlock it, then refresh device discovery.";

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
            record.InventoryState ?? "legacy-unverified",
            record.AcceptedAt,
            record.AcceptedBy,
            record.EnrollmentOperationId,
            trustState,
            trustReason,
            lockdownCheckedAt,
            usableForInstall,
            supportedForFirstInstall,
            new KnownDeviceHealthDto(healthState, healthReason, "derived", isReachable ? now : record.UpdatedAt, nextAction),
            new KnownDeviceAppSlotsDto(slotsUsed, AppSlotLimit),
            record.Owner,
            record.Notes,
            OwnerMemberId: record.OwnerMemberId);
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

    private static string NormalizeTrustState(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "trusted" => "trusted",
        "untrusted" => "untrusted",
        "locked" => "locked",
        "error" => "error",
        _ => "unknown",
    };
}

public sealed class KnownDeviceAcceptanceException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
