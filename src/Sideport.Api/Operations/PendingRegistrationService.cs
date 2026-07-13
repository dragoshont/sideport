using Sideport.Api.AppleAccess;
using Sideport.Api.Catalog;
using Sideport.Api.DeviceInventory;
using Sideport.DeveloperApi.Packaging;
using Sideport.Orchestrator;

namespace Sideport.Api.Operations;

public sealed record CatalogAppRegistrationRequest(
    string CatalogAppId,
    string DeviceUdid,
    string AccountProfileId,
    string Lifecycle = "pending-install");

public sealed record CatalogAppRegistrationDto(
    string BundleId,
    string AppleIdHint,
    string TeamId,
    string DeviceUdid,
    string Lifecycle,
    string CatalogAppId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ActivatedAt,
    string? LastVerifiedOperationId,
    int CatalogVersion,
    string CatalogSha256,
    string Source = "live");

public sealed record CatalogAppRegistrationResult(
    CatalogAppRegistrationDto? Registration,
    bool Created,
    string? Error = null,
    string? Message = null);

/// <summary>
/// Persists the user's catalog/device/account choice before install preflight so
/// first-run setup can resume after a reload. It never installs, signs, or calls
/// Apple beyond resolving already-validated local account/team evidence.
/// </summary>
public sealed class PendingRegistrationService(
    IAppRegistry registry,
    IAppCatalog catalog,
    Lazy<IPersonalAppleAccess> personalApple,
    KnownDeviceStore knownDevices,
    IpaStore ipaStore)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<CatalogAppRegistrationResult> CreateAsync(
        CatalogAppRegistrationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string catalogAppId = Required(request.CatalogAppId, nameof(request.CatalogAppId));
        string deviceUdid = Required(request.DeviceUdid, nameof(request.DeviceUdid));
        string accountProfileId = Required(request.AccountProfileId, nameof(request.AccountProfileId));
        if (!string.Equals(request.Lifecycle, "pending-install", StringComparison.Ordinal))
            return Reject("registration-lifecycle-invalid", "New catalog selections must remain pending until device verification succeeds.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CatalogAppDto? selected = (await catalog.ListAsync(ct).ConfigureAwait(false))
                .FirstOrDefault(app => string.Equals(app.Id, catalogAppId, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
                return Reject("catalog-app-not-found", "The selected catalog app was not found.");
            if (!string.Equals(selected.Status, "ready", StringComparison.Ordinal) ||
                !File.Exists(selected.IpaPath) ||
                selected.CatalogVersion < 1 ||
                string.IsNullOrWhiteSpace(selected.Sha256))
                return Reject("catalog-app-not-ready", "The selected catalog app is not ready for installation.");

            IpaInfo inspected;
            try
            {
                inspected = IpaInspector.Inspect(selected.IpaPath);
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                return Reject("catalog-app-not-ready", "The selected catalog artifact could not be verified.");
            }
            if (!string.Equals(inspected.BundleIdentifier, selected.BundleId, StringComparison.Ordinal))
                return Reject("catalog-bundle-mismatch", "The selected catalog artifact no longer matches its inspected bundle ID.");

            KnownDeviceRecord? known = await knownDevices.FindAsync(deviceUdid, ct).ConfigureAwait(false);
            if (known is null || !string.Equals(known.InventoryState, "accepted", StringComparison.Ordinal))
                return Reject("device-not-accepted", "Add and accept this iPhone before choosing an app for it.");

            PersonalAppleInstallContext apple;
            try
            {
                apple = await personalApple.Value.ResolveFreshInstallContextAsync(accountProfileId, ct).ConfigureAwait(false);
            }
            catch (AppleAccountProfileNotFoundException)
            {
                return Reject("apple-account-profile-not-found", "The selected Apple account profile was not found.");
            }
            catch (AppleTeamSelectionStaleException)
            {
                return Reject("apple-authentication-stale", "Sign in to Apple again before choosing the app.");
            }
            catch (AppleTeamNotReturnedException)
            {
                return Reject("apple-team-required", "Choose a team returned by Apple before choosing the app.");
            }

            IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct).ConfigureAwait(false);
            AppRegistration? existing = registrations.FirstOrDefault(app =>
                string.Equals(app.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(app.BundleId, selected.BundleId, StringComparison.Ordinal));
            if (existing is not null)
            {
                bool replay = existing.IsPendingInstall &&
                    string.Equals(existing.CatalogAppId, selected.Id, StringComparison.OrdinalIgnoreCase) &&
                    existing.CatalogVersion == selected.CatalogVersion &&
                    string.Equals(existing.CatalogSha256, selected.Sha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.AppleId, apple.AppleId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.TeamId, apple.TeamId, StringComparison.Ordinal);
                return replay
                    ? new CatalogAppRegistrationResult(ToDto(existing), Created: false)
                    : Reject(
                        existing.IsPendingInstall ? "pending-registration-conflict" : "registration-already-active",
                        existing.IsPendingInstall
                            ? "A different pending selection already owns this app and iPhone."
                            : "This app is already active on the selected iPhone.");
            }

            int used = registrations.Count(app =>
                string.Equals(app.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase));
            if (used >= 3)
                return Reject("device-app-slot-limit", "The selected iPhone already has three Sideport app registrations.");

            string durableIpaPath = await ipaStore.StoreAsync(
                deviceUdid,
                selected.BundleId,
                selected.IpaPath,
                ct).ConfigureAwait(false);
            var pending = new AppRegistration(
                selected.BundleId,
                apple.AppleId,
                apple.TeamId,
                deviceUdid,
                durableIpaPath,
                Lifecycle: "pending-install",
                CatalogAppId: selected.Id,
                CreatedAt: DateTimeOffset.UtcNow,
                ActivatedAt: null,
                LastVerifiedOperationId: null,
                CatalogVersion: selected.CatalogVersion,
                CatalogSha256: selected.Sha256);
            await registry.UpsertAsync(pending, ct).ConfigureAwait(false);
            return new CatalogAppRegistrationResult(ToDto(pending), Created: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static CatalogAppRegistrationDto ToDto(AppRegistration registration) =>
        new(
            registration.BundleId,
            AppleAccountIdentity.Redact(registration.AppleId),
            registration.TeamId,
            registration.DeviceUdid,
            registration.Lifecycle,
            registration.CatalogAppId!,
            registration.CreatedAt ?? DateTimeOffset.UnixEpoch,
            registration.ActivatedAt,
            registration.LastVerifiedOperationId,
            registration.CatalogVersion ?? 0,
            registration.CatalogSha256 ?? string.Empty);

    private static CatalogAppRegistrationResult Reject(string code, string message) =>
        new(null, Created: false, code, message);

    private static string Required(string? value, string field)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            throw new ArgumentException($"{field} is required.", field);
        if (normalized.Length > 512)
            throw new ArgumentException($"{field} must be 512 characters or fewer.", field);
        return normalized;
    }
}
