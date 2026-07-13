namespace Sideport.Api.DeviceInventory;

public sealed record KnownDeviceHealthDto(string State, string Reason, string Source, DateTimeOffset CheckedAt, string? NextAction = null);

public sealed record KnownDeviceAppSlotsDto(int Used, int Limit, string Source = "derived");

public sealed record KnownDeviceDto(
    string Udid,
    string DisplayName,
    string? ProductType,
    string? OsVersion,
    string Connection,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastSeenAt,
    string LastSeenSource,
    DateTimeOffset? CurrentPollAt,
    string InventoryState,
    DateTimeOffset? AcceptedAt,
    string? AcceptedBy,
    string? EnrollmentOperationId,
    string TrustState,
    string? TrustReason,
    DateTimeOffset? LockdownCheckedAt,
    bool UsableForInstall,
    bool SupportedForFirstInstall,
    KnownDeviceHealthDto Health,
    KnownDeviceAppSlotsDto AppSlots,
    string? Owner,
    string? Notes,
    string Source = "live",
    string? OwnerMemberId = null);

public sealed record KnownDeviceUpsertRequest(string Udid, string? DisplayName = null, string? Owner = null, string? Notes = null);

public sealed record KnownDevicePatchRequest(string? DisplayName = null, string? Owner = null, string? Notes = null);

public sealed record KnownDeviceErrorDto(string Error, string Message, string? Detail = null, int? RegistrationCount = null);
