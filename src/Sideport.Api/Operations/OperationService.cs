using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sideport.Api.AppleAccess;
using Sideport.Api.Catalog;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Onboarding;
using Sideport.Api.WorkspaceAccess;
using Sideport.Core;
using Sideport.DeveloperApi;
using Sideport.DeveloperApi.Packaging;
using Sideport.Orchestrator;

namespace Sideport.Api.Operations;

public sealed class OperationService(
    IAppRegistry registry,
    RefreshOrchestrator orchestrator,
    OperationStore store,
    OperationQueue queue,
    IAppCatalog catalog,
    Lazy<IPersonalAppleAccess> personalApple,
    KnownDeviceStore knownDevices,
    IDeviceController devices,
    IpaStore ipaStore,
    OnboardingCompletionStore onboardingCompletion,
    ISigningIdentityProvider signingIdentity,
    SchedulerSettingsStore schedulerSettings,
    SystemStatusService systemStatus,
    OrchestratorOptions orchestratorOptions,
    WorkspaceExecutionAuthorizer? executionAuthorization = null,
    SignerAuthorityGate? signerAuthorityGate = null)
{
    private static readonly TimeSpan InstallPreflightLifetime = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _submissionGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly ConcurrentDictionary<string, InstallPreflightAuthorization> _installPreflights =
        new(StringComparer.Ordinal);

    private static readonly string[] PlannedRefreshMutations =
    [
        "Use the server-custodied Apple account and selected team",
        "Reuse Sideport's persisted signing identity",
        "Ensure the registered device, App ID, and provisioning profile",
        "Re-sign IPA",
        "Install and verify the signed IPA on the device",
    ];

    public async Task<OperationPreflightDto> PreflightRefreshAsync(
        string deviceUdid,
        string bundleId,
        CancellationToken ct = default,
        string? excludingOperationId = null,
        bool allowAppleAuthentication = true,
        bool requireCurrentCatalogApproval = false)
    {
        deviceUdid = RequiredIntentValue(deviceUdid, nameof(deviceUdid));
        bundleId = RequiredIntentValue(bundleId, nameof(bundleId));
        AppRegistration? registration = await registry.FindAsync(deviceUdid, bundleId, ct).ConfigureAwait(false);
        var target = registration is null
            ? new OperationTargetDto(deviceUdid, bundleId, Kind: "app")
            : new OperationTargetDto(
                registration.DeviceUdid,
                registration.BundleId,
                TeamId: registration.TeamId,
                Kind: "app",
                CatalogAppId: registration.CatalogAppId,
                AccountProfileId: AppleAccountIdentity.ProfileIdFor(registration.AppleId),
                CatalogVersion: registration.CatalogVersion,
                CatalogSha256: registration.CatalogSha256);
        var blockers = new List<OperationIssueDto>();
        var warnings = new List<OperationIssueDto>();
        IReadOnlyList<AppRegistration> apps = await registry.ListAsync(ct).ConfigureAwait(false);
        IReadOnlyList<OperationRecordDto> records = await store.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        int deviceRegistrations = apps.Count(app => string.Equals(app.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase));

        SystemStatusDto operational = await systemStatus.GetAsync(ct).ConfigureAwait(false);
        foreach (SystemStatusCheckDto failed in operational.Checks.Where(check =>
                     string.Equals(check.Status, "fail", StringComparison.Ordinal)))
        {
            blockers.Add(new OperationIssueDto(failed.Id, failed.Reason));
        }

        if (registration is null)
        {
            blockers.Add(new OperationIssueDto(
                "registration-missing",
                "No Sideport registration exists for this device and bundle ID."));
        }
        else
        {
            if (requireCurrentCatalogApproval)
            {
                CatalogAppV2Dto? approved = string.IsNullOrWhiteSpace(registration.CatalogAppId)
                    ? null
                    : (await catalog.ListV2Async(ct).ConfigureAwait(false)).FirstOrDefault(app =>
                        string.Equals(app.Id, registration.CatalogAppId, StringComparison.OrdinalIgnoreCase));
                if (approved is null ||
                    !string.Equals(approved.Status, "ready", StringComparison.Ordinal) ||
                    !string.Equals(approved.BundleId, registration.BundleId, StringComparison.Ordinal) ||
                    approved.CatalogVersion != registration.CatalogVersion ||
                    string.IsNullOrWhiteSpace(approved.Sha256) ||
                    !string.Equals(approved.Sha256, registration.CatalogSha256, StringComparison.OrdinalIgnoreCase))
                {
                    blockers.Add(new OperationIssueDto(
                        "owner-action-required",
                        "The home Owner must approve the current app version before it can be refreshed."));
                }
            }

            if (registration.IsPendingInstall || string.IsNullOrWhiteSpace(registration.LastVerifiedOperationId))
            {
                blockers.Add(new OperationIssueDto(
                    "registration-verification-required",
                    "Install and verify this app on the iPhone before refreshing it."));
            }
            else if (FindVerifiedRegistrationEvidence(records, registration) is null)
            {
                blockers.Add(new OperationIssueDto(
                    "registration-verification-invalid",
                    "The registration's durable device-verification evidence is missing or no longer matches."));
            }

            if (!File.Exists(registration.InputIpaPath))
            {
                blockers.Add(new OperationIssueDto(
                    "ipa-missing",
                    "The registered IPA is missing from durable storage."));
            }
            else
            {
                try
                {
                    IpaInfo info = IpaInspector.Inspect(registration.InputIpaPath);
                    string actualSha256 = await ComputeFileSha256Async(registration.InputIpaPath, ct).ConfigureAwait(false);
                    target = target with
                    {
                        Version = PreferredVersion(info),
                        CatalogSha256 = actualSha256,
                    };
                    if (!string.Equals(info.BundleIdentifier, registration.BundleId, StringComparison.Ordinal))
                    {
                        blockers.Add(new OperationIssueDto(
                            "bundle-mismatch",
                            "The stored IPA bundle ID no longer matches the registration."));
                    }
                    else if (!string.IsNullOrWhiteSpace(registration.CatalogSha256) &&
                             !string.Equals(actualSha256, registration.CatalogSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        blockers.Add(new OperationIssueDto(
                            "registration-artifact-lineage-changed",
                            "The stored IPA changed after this registration was saved."));
                    }
                }
                catch (Exception ex) when (ex is FormatException || ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    blockers.Add(new OperationIssueDto(
                        "ipa-inspection-failed",
                        "The stored IPA could not be inspected before refresh."));
                }
            }

            PersonalAppleStatusDto apple = await personalApple.Value.StatusAsync(ct).ConfigureAwait(false);
            string registrationProfileId = AppleAccountIdentity.ProfileIdFor(registration.AppleId);
            if (!string.Equals(apple.AccountProfileId, registrationProfileId, StringComparison.Ordinal) ||
                !string.Equals(apple.SelectedTeamId, registration.TeamId, StringComparison.Ordinal))
            {
                blockers.Add(new OperationIssueDto(
                    "apple-refresh-lineage-mismatch",
                    "The registration no longer matches the selected Apple account and team."));
            }
            else if (apple.State is "two-factor-required" or "failed" or "unknown" or "credential-configured")
            {
                blockers.Add(new OperationIssueDto(
                    "apple-refresh-context-unavailable",
                    "Restore the server-custodied Apple account before refreshing."));
            }
            else if (!allowAppleAuthentication &&
                     (string.Equals(apple.State, "validation-stale", StringComparison.Ordinal) ||
                      !orchestrator.HasCachedAppleSession(registration.AppleId)))
            {
                blockers.Add(new OperationIssueDto(
                    "owner-action-required",
                    "The home Owner must refresh the Apple connection before this app can be updated."));
            }
            else if (string.Equals(apple.State, "validation-stale", StringComparison.Ordinal))
            {
                warnings.Add(new OperationIssueDto(
                    "apple-session-will-renew",
                    "Sideport will renew the server-custodied Apple session before changing the app."));
            }

            try
            {
                SigningIdentityInspection identity = await signingIdentity
                    .InspectAsync(registration.AppleId, registration.TeamId, ct)
                    .ConfigureAwait(false);
                if (!string.Equals(identity.State, "reusable", StringComparison.Ordinal) ||
                    identity.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
                {
                    blockers.Add(new OperationIssueDto(
                        "signing-identity-unavailable",
                        "A reusable persisted Sideport signing identity is required before refreshing."));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                blockers.Add(new OperationIssueDto(
                    "signing-identity-unavailable",
                    "Sideport could not verify its persisted signing identity."));
            }

            (string? deviceError, string? deviceMessage, DeviceConnection? connection) =
                await ValidateRefreshDeviceAsync(registration.DeviceUdid, ct).ConfigureAwait(false);
            if (deviceError is not null)
                blockers.Add(new OperationIssueDto(deviceError, deviceMessage!));
            else if (connection == DeviceConnection.Wifi)
            {
                warnings.Add(new OperationIssueDto(
                    "wifi-refresh-usb-fallback",
                    "Sideport can try this refresh over paired Wi-Fi; reconnect USB if the bulk transfer cannot finish."));
            }

            if (records.Any(operation =>
                    OperationReconciliationEvidence.IsUnresolvedForManualAction(operation, records) &&
                    string.Equals(operation.Target.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase)))
            {
                blockers.Add(new OperationIssueDto(
                    "device-operation-still-active",
                    "A previous device operation has unresolved state and must be reconciled before refreshing."));
            }

            OperationRecordDto? active = records.FirstOrDefault(operation =>
                !string.Equals(operation.OperationId, excludingOperationId, StringComparison.Ordinal) &&
                operation.Status is "queued" or "waiting" or "running" &&
                string.Equals(operation.Target.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase));
            if (active is not null)
            {
                blockers.Add(new OperationIssueDto(
                    "device-operation-active",
                    "Another operation is already using this iPhone; wait for it to finish before refreshing."));
            }
        }

        var limits = new[]
        {
            new OperationLimitDto("free-device-app-slots", "Free-account app slots", deviceRegistrations, 3),
        };

        return new OperationPreflightDto(
            Ready: blockers.Count == 0,
            Target: target,
            Blockers: blockers,
            Warnings: warnings,
            PlannedMutations: PlannedRefreshMutations,
            ScarceLimits: limits,
            RequiresConfirmation: true);
    }

    public async Task<OperationPreflightDto> PreflightInstallAsync(
        string deviceUdid,
        string bundleId,
        bool finishOnboarding,
        string? catalogAppId = null,
        string? accountProfileId = null,
        bool allowOwnerManagedAppleAuthority = true,
        CancellationToken ct = default)
    {
        InstallPreflightBuild build = await BuildInstallPreflightAsync(
            deviceUdid,
            bundleId,
            finishOnboarding,
            catalogAppId,
            accountProfileId,
            allowOwnerManagedAppleAuthority,
            persistAuthorization: true,
            ct).ConfigureAwait(false);
        return build.Preflight;
    }

    private async Task<InstallPreflightBuild> BuildInstallPreflightAsync(
        string deviceUdid,
        string bundleId,
        bool finishOnboarding,
        string? requestedCatalogAppId,
        string? requestedAccountProfileId,
        bool allowOwnerManagedAppleAuthority,
        bool persistAuthorization,
        CancellationToken ct)
    {
        deviceUdid = RequiredIntentValue(deviceUdid, nameof(deviceUdid));
        bundleId = RequiredIntentValue(bundleId, nameof(bundleId));
        requestedCatalogAppId = OptionalIntentValue(requestedCatalogAppId);
        requestedAccountProfileId = OptionalIntentValue(requestedAccountProfileId);

        var blockers = new List<OperationIssueDto>();
        var warnings = new List<OperationIssueDto>();
        var checks = new Dictionary<string, List<OperationPreflightCheckDto>>(StringComparer.Ordinal)
        {
            ["server"] = [],
            ["device"] = [],
            ["app"] = [],
            ["apple-account"] = [],
            ["signing"] = [],
            ["operations"] = [],
            ["scheduler"] = [],
        };

        void Passed(string group, string code, string label, string? detail = null) =>
            checks[group].Add(new OperationPreflightCheckDto(code, label, "passed", Detail: detail));
        void Blocked(string group, string code, string message, string? detail = null)
        {
            blockers.Add(new OperationIssueDto(code, message, Detail: detail));
            checks[group].Add(new OperationPreflightCheckDto(code, message, "blocked", Detail: detail));
        }
        void Warned(string group, string code, string message, string? detail = null)
        {
            warnings.Add(new OperationIssueDto(code, message, Detail: detail));
            checks[group].Add(new OperationPreflightCheckDto(code, message, "warning", Detail: detail));
        }

        SystemStatusDto operationalStatus = await systemStatus.GetAsync(ct).ConfigureAwait(false);
        if (operationalStatus.Operational)
        {
            Passed("server", "system-operational", "Sideport's protected runtime dependencies are available.");
        }
        else
        {
            foreach (SystemStatusCheckDto failed in operationalStatus.Checks.Where(check =>
                         string.Equals(check.Status, "fail", StringComparison.Ordinal)))
            {
                string group = failed.Id switch
                {
                    "device-transport" => "device",
                    "anisette-headers" => "apple-account",
                    "signer-executable" => "signing",
                    "operation-store" => "operations",
                    _ => "server",
                };
                Blocked(group, failed.Id, failed.Reason);
            }
        }

        (string? deviceError, string? deviceMessage) =
            await ValidateInstallDeviceAsync(deviceUdid, ct).ConfigureAwait(false);
        if (deviceError is null)
            Passed("device", "device-ready", "The accepted iPhone is trusted and connected over USB.");
        else
            Blocked("device", deviceError, deviceMessage!);

        IReadOnlyList<CatalogAppDto> catalogApps = await catalog.ListAsync(ct).ConfigureAwait(false);
        CatalogAppDto? catalogApp = null;
        if (requestedCatalogAppId is not null)
        {
            catalogApp = catalogApps.FirstOrDefault(app =>
                string.Equals(app.Id, requestedCatalogAppId, StringComparison.OrdinalIgnoreCase));
            if (catalogApp is null)
                Blocked("app", "catalog-app-not-found", "The selected catalog app was not found.");
            else if (!string.Equals(catalogApp.BundleId, bundleId, StringComparison.Ordinal))
                Blocked("app", "catalog-bundle-mismatch", "The selected catalog app no longer matches this bundle ID.");
        }
        else
        {
            CatalogAppDto[] matchingApps = catalogApps
                .Where(app => string.Equals(app.BundleId, bundleId, StringComparison.Ordinal))
                .ToArray();
            if (matchingApps.Length == 1)
                catalogApp = matchingApps[0];
            else if (matchingApps.Length == 0)
                Blocked("app", "catalog-app-not-found", "No catalog app matches this bundle ID.");
            else
                Blocked("app", "catalog-app-selection-required", "Choose one catalog artifact for this bundle ID.");
        }

        if (catalogApp is not null &&
            !blockers.Any(issue => issue.Code is "catalog-bundle-mismatch" or "catalog-app-not-found"))
        {
            if (!string.Equals(catalogApp.Status, "ready", StringComparison.Ordinal) ||
                !File.Exists(catalogApp.IpaPath))
            {
                Blocked("app", "catalog-app-not-ready", "The selected catalog app is not ready for installation.");
            }
            else
            {
                try
                {
                    IpaInfo inspected = IpaInspector.Inspect(catalogApp.IpaPath);
                    string actualSha256 = await ComputeFileSha256Async(catalogApp.IpaPath, ct).ConfigureAwait(false);
                    if (!string.Equals(inspected.BundleIdentifier, catalogApp.BundleId, StringComparison.Ordinal) ||
                        !string.Equals(inspected.BundleIdentifier, bundleId, StringComparison.Ordinal))
                    {
                        Blocked("app", "catalog-bundle-mismatch", "The selected catalog artifact no longer matches its inspected bundle ID.");
                    }
                    else if (catalogApp.CatalogVersion < 1 ||
                             string.IsNullOrWhiteSpace(catalogApp.Sha256) ||
                             !string.Equals(actualSha256, catalogApp.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        Blocked("app", "catalog-integrity-mismatch", "The selected catalog artifact changed after it was inspected.");
                    }
                    else
                    {
                        Passed("app", "catalog-artifact-ready", "The IPA is present and its bundle ID is verified.");
                    }
                }
                catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    Blocked("app", "catalog-app-not-ready", "The selected catalog artifact could not be verified.");
                }
            }
        }

        PersonalAppleInstallPreflightContext? applePreflight = null;
        PersonalAppleStatusDto appleStatus = await personalApple.Value.StatusAsync(ct).ConfigureAwait(false);
        string? accountProfileId = requestedAccountProfileId ?? appleStatus.AccountProfileId;
        if (accountProfileId is null)
        {
            Blocked("apple-account", "apple-credential-missing", "Connect and validate an Apple account before installing.");
        }
        else
        {
            try
            {
                applePreflight = await personalApple.Value
                    .ResolveFreshInstallPreflightContextAsync(accountProfileId, ct)
                    .ConfigureAwait(false);
                Passed("apple-account", "apple-account-ready", "The Apple account and selected team are validated.");
                if (!allowOwnerManagedAppleAuthority &&
                    !orchestrator.HasCachedAppleSession(applePreflight.Install.AppleId))
                {
                    Blocked(
                        "apple-account",
                        "owner-action-required",
                        "The home Owner must refresh the Apple connection before installing this app.");
                }
            }
            catch (AppleAccountProfileNotFoundException)
            {
                Blocked("apple-account", "apple-account-profile-not-found", "The selected Apple account profile was not found.");
            }
            catch (AppleTeamSelectionStaleException)
            {
                Blocked("apple-account", "apple-authentication-stale", "Sign in to Apple again before installing.");
            }
            catch (AppleTeamNotReturnedException)
            {
                Blocked("apple-account", "apple-team-required", "Choose a team returned by Apple before installing.");
            }
        }

        IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct).ConfigureAwait(false);
        AppRegistration? existingRegistration = registrations.FirstOrDefault(app =>
            string.Equals(app.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(app.BundleId, bundleId, StringComparison.Ordinal));
        int deviceRegistrationCount = registrations.Count(app =>
            string.Equals(app.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase));
        if (existingRegistration is not null && !existingRegistration.IsPendingInstall)
        {
            Blocked("app", "registration-already-active", "This app is already active on the selected iPhone.");
        }
        else if (existingRegistration is not null && catalogApp is not null && applePreflight is not null &&
                 (!string.Equals(existingRegistration.CatalogAppId, catalogApp.Id, StringComparison.OrdinalIgnoreCase) ||
                  existingRegistration.CatalogVersion != catalogApp.CatalogVersion ||
                  !string.Equals(existingRegistration.CatalogSha256, catalogApp.Sha256, StringComparison.OrdinalIgnoreCase) ||
                  !string.Equals(existingRegistration.AppleId, applePreflight.Install.AppleId, StringComparison.OrdinalIgnoreCase) ||
                  !string.Equals(existingRegistration.TeamId, applePreflight.Install.TeamId, StringComparison.Ordinal)))
        {
            Blocked("app", "pending-registration-conflict", "A different pending install already owns this iPhone and bundle ID.");
        }
        else if (existingRegistration is null && deviceRegistrationCount >= 3)
        {
            Blocked("app", "device-app-slot-limit", "The selected iPhone already has three Sideport app registrations.");
        }
        else
        {
            Passed("app", "registration-slot-ready", "A Sideport app slot is available for this iPhone.");
        }

        IReadOnlyList<OperationRecordDto> operations =
            await store.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        OperationRecordDto? unknownDeviceOperation = operations.FirstOrDefault(operation =>
            OperationReconciliationEvidence.IsUnresolvedForManualAction(operation, operations) &&
            string.Equals(operation.Target.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase));
        if (unknownDeviceOperation is not null)
        {
            Blocked(
                "operations",
                "device-operation-still-active",
                "A previous device operation has an unknown transfer state and must be reconciled before another install.",
                $"operation {unknownDeviceOperation.OperationId}");
        }

        OperationRecordDto? activeTargetOperation = operations.FirstOrDefault(operation =>
            string.Equals(operation.Type, "install", StringComparison.Ordinal) &&
            operation.Status is "queued" or "waiting" or "running" &&
            string.Equals(operation.Target.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Target.BundleId, bundleId, StringComparison.Ordinal));
        if (activeTargetOperation is not null)
        {
            Blocked(
                "operations",
                "install-already-active",
                "An install for this app and iPhone is already active.",
                $"operation {activeTargetOperation.OperationId}");
        }
        else if (operations.Any(operation =>
                     operation.Status is "queued" or "waiting" or "running" &&
                     string.Equals(operation.Target.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase)))
        {
            Warned("operations", "single-flight-busy", "Another operation may finish before this install can start.");
        }
        else
        {
            Passed("operations", "single-flight-available", "No active operation is using this iPhone.");
        }

        SchedulerSettingsState? schedulerState = null;
        try
        {
            schedulerState = await schedulerSettings.ReadAsync(ct).ConfigureAwait(false);
            if (schedulerState is null)
                Blocked("scheduler", "scheduler-store-unavailable", "Automatic refresh settings have not been initialized.");
            else
                Passed("scheduler", "scheduler-eligible", "Automatic due-only refresh can be enabled after device verification.");
        }
        catch (SchedulerSettingsStoreException)
        {
            Blocked("scheduler", "scheduler-store-unavailable", "Automatic refresh settings are unavailable.");
        }

        SigningIdentityInspection? localIdentity = null;
        IReadOnlyList<AppleDevelopmentCertificate> appleCertificates =
            applePreflight?.Certificates ?? [];
        string? inventoryVersion = applePreflight is null
            ? null
            : Digest(new
            {
                certificates = appleCertificates
                    .OrderBy(certificate => certificate.Id, StringComparer.Ordinal)
                    .Select(certificate => new
                    {
                        certificate.Id,
                        certificate.SerialNumber,
                        expiresAt = certificate.ExpiresAt?.ToUniversalTime().ToString("O"),
                    }),
            });
        string signingImpact = "unavailable";
        bool requiresCutover = false;
        if (applePreflight is not null)
        {
            localIdentity = await signingIdentity.InspectAsync(
                applePreflight.Install.AppleId,
                applePreflight.Install.TeamId,
                ct).ConfigureAwait(false);
            bool reusable = string.Equals(localIdentity.State, "reusable", StringComparison.Ordinal);
            bool localCertificatePresent = reusable &&
                localIdentity.SerialSuffix is { Length: > 0 } serialSuffix &&
                appleCertificates.Any(certificate =>
                    certificate.SerialNumber.EndsWith(serialSuffix, StringComparison.OrdinalIgnoreCase));
            if (reusable && localCertificatePresent)
            {
                signingImpact = "reuse-existing";
                Passed("signing", "signing-identity-reusable", "Sideport can reuse its persisted Apple signing identity.");
            }
            else if (!reusable && appleCertificates.Count == 0)
            {
                signingImpact = "mint-new";
                if (allowOwnerManagedAppleAuthority)
                {
                    Warned("signing", "signing-certificate-will-be-created", "Sideport will create and persist one Apple development certificate.");
                }
                else
                {
                    Blocked(
                        "signing",
                        "owner-action-required",
                        "The home Owner must create Sideport's signing identity before installing this app.");
                }
            }
            else
            {
                signingImpact = "replace-existing";
                requiresCutover = true;
                Blocked(
                    "signing",
                    "signing-cutover-required",
                    reusable
                        ? "The persisted Sideport identity does not match Apple's current certificate inventory. Review signer cutover before installing."
                        : "Apple already has development certificates that Sideport cannot replace without an explicit signer cutover.");
            }
        }

        var plannedMutations = new List<string>
        {
            "Register the selected iPhone with Apple if needed",
            "Ensure the app identifier and provisioning profile",
            "Re-sign the selected IPA",
            "Install and verify the app over USB",
            "Activate the verified app registration",
        };
        if (string.Equals(signingImpact, "mint-new", StringComparison.Ordinal))
            plannedMutations.Insert(1, "Create and persist one Apple development certificate");
        if (finishOnboarding)
        {
            if (schedulerState?.Enabled != true)
                plannedMutations.Add("Enable automatic hourly due-only refresh");
            plannedMutations.Add("Compute and save the next refresh evaluation");
            plannedMutations.Add("Write the immutable Sideport setup receipt");
        }

        var limits = new[]
        {
            new OperationLimitDto("free-device-app-slots", "Free-account app slots", deviceRegistrationCount, 3),
            new OperationLimitDto("apple-development-certificates", "Apple development certificates", appleCertificates.Count, 2, "live"),
        };
        var target = new OperationTargetDto(
            deviceUdid,
            bundleId,
            TeamId: applePreflight?.Install.TeamId,
            Kind: "catalog-app",
            CatalogAppId: catalogApp?.Id,
            AccountProfileId: applePreflight?.Install.AccountProfileId ?? accountProfileId,
            CatalogVersion: catalogApp?.CatalogVersion);
        OperationPreflightCheckGroupDto[] checkGroups =
        [
            new("server", "Sideport", checks["server"]),
            new("device", "iPhone", checks["device"]),
            new("app", "App", checks["app"]),
            new("apple-account", "Apple account", checks["apple-account"]),
            new("signing", "Signing", checks["signing"]),
            new("operations", "Install availability", checks["operations"]),
            new("scheduler", "Automatic refresh", checks["scheduler"]),
        ];
        string planVersion = Digest(new
        {
            type = "install",
            deviceUdid = deviceUdid.ToUpperInvariant(),
            bundleId,
            finishOnboarding,
            catalog = catalogApp is null ? null : new
            {
                catalogApp.Id,
                catalogApp.CatalogVersion,
                catalogApp.BundleId,
                catalogApp.Sha256,
                catalogApp.SizeBytes,
                catalogApp.Status,
            },
            accountProfileId = applePreflight?.Install.AccountProfileId ?? accountProfileId,
            teamId = applePreflight?.Install.TeamId,
            registration = existingRegistration is null ? null : new
            {
                existingRegistration.Lifecycle,
                existingRegistration.CatalogAppId,
                existingRegistration.CatalogVersion,
                existingRegistration.CatalogSha256,
                existingRegistration.LastVerifiedOperationId,
            },
            limits = limits.Select(limit => new { limit.Code, limit.Used, limit.Limit }),
            plannedMutations,
            blockers = blockers.Select(issue => new { issue.Code, issue.Source, issue.Detail }),
            warnings = warnings.Select(issue => new { issue.Code, issue.Source, issue.Detail }),
            signing = new
            {
                inventoryVersion,
                impact = signingImpact,
                localState = localIdentity?.State,
                localExpiresAt = localIdentity?.ExpiresAt?.ToUniversalTime().ToString("O"),
                requiresCutover,
            },
            scheduler = schedulerState is null ? null : new
            {
                schedulerState.SettingsVersion,
                schedulerState.Enabled,
                schedulerState.RequestedEnabled,
            },
        });

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string preflightId = $"install_preflight_{Guid.NewGuid():N}";
        DateTimeOffset expiresAt = now.Add(InstallPreflightLifetime);
        var signing = new OperationSigningReadinessDto(
            localIdentity?.State ?? "unavailable",
            localIdentity?.ExpiresAt,
            appleCertificates.Count,
            signingImpact,
            requiresCutover);
        var preflight = new OperationPreflightDto(
            Ready: blockers.Count == 0,
            Target: target,
            Blockers: blockers,
            Warnings: warnings,
            PlannedMutations: plannedMutations,
            ScarceLimits: limits,
            RequiresConfirmation: true,
            PreflightId: preflightId,
            ExpiresAt: expiresAt,
            CheckGroups: checkGroups,
            InventoryVersion: inventoryVersion,
            PlanVersion: planVersion,
            Signing: signing);
        var build = new InstallPreflightBuild(
            preflight,
            catalogApp,
            applePreflight?.Install,
            existingRegistration,
            registrations);

        if (persistAuthorization)
        {
            PruneExpiredInstallPreflights(now);
            _installPreflights[preflightId] = new InstallPreflightAuthorization(
                preflight,
                deviceUdid,
                bundleId,
                catalogApp?.Id,
                applePreflight?.Install.AccountProfileId ?? accountProfileId,
                finishOnboarding,
                allowOwnerManagedAppleAuthority,
                ConsumedByIdempotencyKey: null);
        }
        return build;
    }

    public Task<(OperationRecordDto Record, bool Created)> RefreshAsync(
        string deviceUdid,
        string bundleId,
        OperationActorDto actor,
        string? idempotencyKey,
        string? parentOperationId = null,
        int attempt = 1,
        CancellationToken ct = default) =>
        RefreshAsync(
            deviceUdid,
            bundleId,
            actor,
            idempotencyKey,
            actorMemberId: null,
            ownerMemberId: null,
            parentOperationId: parentOperationId,
            attempt: attempt,
            ct: ct);

    public async Task<(OperationRecordDto Record, bool Created)> RefreshAsync(
        string deviceUdid,
        string bundleId,
        OperationActorDto actor,
        string? idempotencyKey,
        string? actorMemberId,
        string? ownerMemberId,
        string? parentOperationId,
        int attempt,
        CancellationToken ct = default)
    {
        actorMemberId = NormalizeOwnershipId(actorMemberId);
        ownerMemberId = NormalizeOwnershipId(ownerMemberId);
        WorkspaceExecutionDecision? submissionAuthorization = executionAuthorization is null
            ? null
            : await executionAuthorization.AuthorizeSubmissionAsync(
                actor,
                actorMemberId,
                ownerMemberId,
                deviceUdid,
                enrollmentTarget: false,
                assignDefaultOwner: false,
                ct: ct).ConfigureAwait(false);
        await _submissionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string? trimmedKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
            bool allowOwnerManagedAppleAuthority =
                submissionAuthorization?.CanUseOwnerManagedAppleAuthority ?? true;
            OperationPreflightDto preflight = await PreflightRefreshAsync(
                deviceUdid,
                bundleId,
                ct,
                allowAppleAuthentication: allowOwnerManagedAppleAuthority,
                requireCurrentCatalogApproval: !allowOwnerManagedAppleAuthority)
                .ConfigureAwait(false);
            if (submissionAuthorization is { IsAllowed: false } denied)
            {
                preflight = preflight with
                {
                    Ready = false,
                    Blockers =
                    [
                        new OperationIssueDto(
                            denied.ErrorCode ?? "operation-access-revoked",
                            denied.Message ?? "Sideport access changed before this refresh could start."),
                        .. preflight.Blockers,
                    ],
                };
            }
            OperationTargetDto target = preflight.Target;

            if (trimmedKey is not null)
            {
                OperationRecordDto? existing = await store.FindByIdempotencyAsync("refresh", target, actor, trimmedKey, ct).ConfigureAwait(false);
                if (existing is not null)
                    return (existing, false);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string operationId = NewOperationId(now);
            OperationRecordDto record;

            if (!preflight.Ready)
            {
                OperationIssueDto error = preflight.Blockers.FirstOrDefault()
                    ?? new OperationIssueDto("preflight-blocked", "Preflight blocked the refresh operation.");
                record = new OperationRecordDto(
                    operationId,
                    "refresh",
                    "blocked",
                    now,
                    now,
                    now,
                    now,
                    actor,
                    trimmedKey,
                    attempt,
                    target,
                    [new OperationStageDto("preflight", "Preflight", "blocked", now, now, error.Message, error)],
                    null,
                    error,
                    Cancelable: false,
                    Retryable: true,
                    Rerunnable: false,
                    CorrelationId: operationId,
                    ParentOperationId: parentOperationId,
                    ActorMemberId: actorMemberId,
                    OwnerMemberId: ownerMemberId);

                return await store.AddIfIdempotentMissingAsync(record, ct).ConfigureAwait(false);
            }

            var stages = new List<OperationStageDto>
            {
                new("preflight", "Preflight", "succeeded", now, now, "Ready to refresh.", null),
                new("refresh", "Sign and install", "pending", null, null, "Waiting for the single-flight signer.", null),
            };

            record = new OperationRecordDto(
                operationId,
                "refresh",
                "queued",
                now,
                null,
                now,
                null,
                actor,
                trimmedKey,
                attempt,
                target,
                stages,
                null,
                null,
                Cancelable: true,
                Retryable: false,
                Rerunnable: false,
                CorrelationId: operationId,
                ParentOperationId: parentOperationId,
                ActorMemberId: actorMemberId,
                OwnerMemberId: ownerMemberId);

            (OperationRecordDto initialRecord, bool created) = await store.AddIfIdempotentMissingAsync(record, ct).ConfigureAwait(false);
            if (!created)
                return (initialRecord, false);

            queue.Enqueue(initialRecord.OperationId);
            return (initialRecord, true);
        }
        finally
        {
            _submissionGate.Release();
        }
    }

    public Task<VerifyExistingRegistrationSubmissionResult> VerifyExistingRegistrationAsync(
        string deviceUdid,
        string bundleId,
        OperationActorDto actor,
        string idempotencyKey,
        CancellationToken ct = default) =>
        VerifyExistingRegistrationAsync(
            deviceUdid,
            bundleId,
            actor,
            idempotencyKey,
            actorMemberId: null,
            ownerMemberId: null,
            ct: ct);

    public async Task<VerifyExistingRegistrationSubmissionResult> VerifyExistingRegistrationAsync(
        string deviceUdid,
        string bundleId,
        OperationActorDto actor,
        string idempotencyKey,
        string? actorMemberId,
        string? ownerMemberId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        deviceUdid = RequiredIntentValue(deviceUdid, nameof(deviceUdid));
        bundleId = RequiredIntentValue(bundleId, nameof(bundleId));
        idempotencyKey = RequiredIntentValue(idempotencyKey, nameof(idempotencyKey));
        if (idempotencyKey.Length > 256)
            throw new ArgumentException("Idempotency key must be 256 characters or fewer.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(actor.Kind) || string.IsNullOrWhiteSpace(actor.DisplayName))
            throw new ArgumentException("A verified operation actor is required.", nameof(actor));
        actorMemberId = NormalizeOwnershipId(actorMemberId);
        ownerMemberId = NormalizeOwnershipId(ownerMemberId);

        await _submissionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OperationRecordDto? keyed = await store.FindByActorAndIdempotencyAsync(
                "verify-existing-registration",
                actor,
                idempotencyKey,
                ct).ConfigureAwait(false);
            if (keyed is not null)
            {
                bool sameTarget =
                    string.Equals(keyed.Target.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(keyed.Target.BundleId, bundleId, StringComparison.Ordinal);
                return sameTarget
                    ? new VerifyExistingRegistrationSubmissionResult(keyed, Created: false)
                    : new VerifyExistingRegistrationSubmissionResult(
                        null,
                        Created: false,
                        "idempotency-target-conflict",
                        "This idempotency key was already used for another registration verification.");
            }

            AppRegistration? registration = await registry.FindAsync(deviceUdid, bundleId, ct).ConfigureAwait(false);
            if (registration is null)
            {
                return new VerifyExistingRegistrationSubmissionResult(
                    null,
                    Created: false,
                    "registration-not-found",
                    "No Sideport registration exists for this iPhone and bundle ID.");
            }
            if (registration.IsPendingInstall)
            {
                return new VerifyExistingRegistrationSubmissionResult(
                    null,
                    Created: false,
                    "registration-pending-install",
                    "Use the install flow to finish this pending app registration.");
            }

            (string? expectedVersion, OperationIssueDto? artifactError) = InspectRegistrationArtifact(registration);
            if (artifactError is not null)
            {
                return new VerifyExistingRegistrationSubmissionResult(
                    null,
                    Created: false,
                    artifactError.Code,
                    artifactError.Message);
            }
            string artifactSha256;
            try
            {
                artifactSha256 = await ComputeFileSha256Async(registration.InputIpaPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new VerifyExistingRegistrationSubmissionResult(
                    null,
                    Created: false,
                    "registration-artifact-unavailable",
                    "Sideport could not hash the saved IPA before verification.");
            }

            IReadOnlyList<OperationRecordDto> records = await store.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
            OperationRecordDto? conflicting = records.FirstOrDefault(operation =>
                string.Equals(operation.Target.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase) &&
                (operation.Status is "queued" or "waiting" or "running" ||
                 OperationReconciliationEvidence.IsUnresolvedMutation(operation, records)));
            if (conflicting is not null)
            {
                return new VerifyExistingRegistrationSubmissionResult(
                    null,
                    Created: false,
                    "device-operation-still-active",
                    "Another operation still owns or has unresolved state for this iPhone.");
            }

            (string? deviceError, string? deviceMessage) =
                await ValidateInstallDeviceAsync(deviceUdid, ct).ConfigureAwait(false);
            if (deviceError is not null)
            {
                return new VerifyExistingRegistrationSubmissionResult(
                    null,
                    Created: false,
                    deviceError,
                    deviceMessage);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string operationId = NewOperationId(now);
            var target = new OperationTargetDto(
                deviceUdid,
                bundleId,
                TeamId: registration.TeamId,
                Kind: "app",
                CatalogAppId: registration.CatalogAppId,
                AccountProfileId: AppleAccountIdentity.ProfileIdFor(registration.AppleId),
                CatalogVersion: registration.CatalogVersion,
                Version: expectedVersion,
                CatalogSha256: artifactSha256);
            var record = new OperationRecordDto(
                operationId,
                "verify-existing-registration",
                "queued",
                now,
                null,
                now,
                null,
                actor,
                idempotencyKey,
                Attempt: 1,
                target,
                [
                    new OperationStageDto(
                        "preflight",
                        "Preflight",
                        "succeeded",
                        now,
                        now,
                        "The active registration and trusted USB iPhone are ready for verification."),
                    new OperationStageDto(
                        "verify",
                        "Verify existing app",
                        "pending",
                        null,
                        null,
                        $"Waiting to read installed version {expectedVersion} from the iPhone."),
                    new OperationStageDto(
                        "activate-registration",
                        "Save verification",
                        "pending",
                        null,
                        null,
                        "Waiting for device verification."),
                ],
                Result: null,
                Error: null,
                Cancelable: true,
                Retryable: false,
                Rerunnable: false,
                CorrelationId: operationId,
                ActorMemberId: actorMemberId,
                OwnerMemberId: ownerMemberId);

            (OperationRecordDto stored, bool created) =
                await store.AddIfIdempotentMissingAsync(record, ct).ConfigureAwait(false);
            if (!created)
                return new VerifyExistingRegistrationSubmissionResult(stored, Created: false);

            queue.Enqueue(stored.OperationId);
            return new VerifyExistingRegistrationSubmissionResult(stored, Created: true);
        }
        finally
        {
            _submissionGate.Release();
        }
    }

    public Task<OperationReconciliationSubmissionResult> ReconcileAsync(
        string sourceOperationId,
        OperationActorDto actor,
        string idempotencyKey,
        string? note = null,
        CancellationToken ct = default) =>
        ReconcileAsync(
            sourceOperationId,
            actor,
            idempotencyKey,
            note,
            actorMemberId: null,
            ownerMemberId: null,
            ct: ct);

    public async Task<OperationReconciliationSubmissionResult> ReconcileAsync(
        string sourceOperationId,
        OperationActorDto actor,
        string idempotencyKey,
        string? note,
        string? actorMemberId,
        string? ownerMemberId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        sourceOperationId = RequiredIntentValue(sourceOperationId, nameof(sourceOperationId));
        idempotencyKey = RequiredIntentValue(idempotencyKey, nameof(idempotencyKey));
        if (idempotencyKey.Length > 256)
            throw new ArgumentException("Idempotency key must be 256 characters or fewer.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(actor.Kind) || string.IsNullOrWhiteSpace(actor.DisplayName))
            throw new ArgumentException("A verified operation actor is required.", nameof(actor));
        actorMemberId = NormalizeOwnershipId(actorMemberId);
        ownerMemberId = NormalizeOwnershipId(ownerMemberId);
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > 500)
            throw new ArgumentException("Reconciliation note must be 500 characters or fewer.", nameof(note));

        await _submissionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OperationRecordDto? keyed = await store.FindByActorAndIdempotencyAsync(
                OperationReconciliationEvidence.OperationType,
                actor,
                idempotencyKey,
                ct).ConfigureAwait(false);
            if (keyed is not null)
            {
                return string.Equals(keyed.ParentOperationId, sourceOperationId, StringComparison.Ordinal)
                    ? new OperationReconciliationSubmissionResult(keyed, Created: false)
                    : new OperationReconciliationSubmissionResult(
                        null,
                        Created: false,
                        "idempotency-target-conflict",
                        "This idempotency key was already used for another reconciliation.");
            }

            OperationRecordDto? source = await store.FindAsync(sourceOperationId, ct).ConfigureAwait(false);
            if (source is null)
            {
                return new OperationReconciliationSubmissionResult(
                    null,
                    Created: false,
                    "operation-not-found",
                    "The operation to reconcile was not found.");
            }
            if (source.Type is not ("install" or "refresh") ||
                !string.Equals(source.Status, "unknown", StringComparison.Ordinal))
            {
                return new OperationReconciliationSubmissionResult(
                    null,
                    Created: false,
                    "operation-not-reconcilable",
                    "Only an unknown install or refresh can be reconciled.");
            }

            if (ownerMemberId is not null &&
                !string.Equals(ownerMemberId, source.OwnerMemberId, StringComparison.Ordinal))
            {
                return new OperationReconciliationSubmissionResult(
                    null,
                    Created: false,
                    "resource-not-found",
                    "The operation to reconcile was not found.");
            }
            if (executionAuthorization is not null)
            {
                WorkspaceExecutionDecision authorization = await executionAuthorization
                    .AuthorizeOperationAsync(source with
                    {
                        Actor = actor,
                        ActorMemberId = actorMemberId,
                    }, ct: ct)
                    .ConfigureAwait(false);
                if (!authorization.IsAllowed)
                {
                    return new OperationReconciliationSubmissionResult(
                        null,
                        Created: false,
                        authorization.ErrorCode ?? "operation-access-revoked",
                        authorization.Message ?? "Sideport access changed after this operation was submitted.");
                }
            }
            ownerMemberId = source.OwnerMemberId;

            if (string.IsNullOrWhiteSpace(source.Target.DeviceUdid) ||
                string.IsNullOrWhiteSpace(source.Target.BundleId) ||
                string.IsNullOrWhiteSpace(source.Target.Version) ||
                string.IsNullOrWhiteSpace(source.Target.CatalogSha256) ||
                source.Result?.ExpiresAt is null)
            {
                return new OperationReconciliationSubmissionResult(
                    null,
                    Created: false,
                    "operation-reconciliation-evidence-missing",
                    "The unknown operation does not retain the exact version and profile expiry needed for safe reconciliation.");
            }

            IReadOnlyList<OperationRecordDto> records = await store.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
            if (OperationReconciliationEvidence.HasCompletedReconciliation(source, records))
            {
                return new OperationReconciliationSubmissionResult(
                    null,
                    Created: false,
                    "operation-already-reconciled",
                    "This unknown operation already has a successful reconciliation.");
            }

            string deviceUdid = source.Target.DeviceUdid;
            if (orchestrator.IsDeviceMutationActive(deviceUdid) ||
                records.Any(operation =>
                    !string.Equals(operation.OperationId, source.OperationId, StringComparison.Ordinal) &&
                    operation.Status is "queued" or "waiting" or "running" &&
                    string.Equals(operation.Target.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase)))
            {
                return new OperationReconciliationSubmissionResult(
                    null,
                    Created: false,
                    "device-operation-still-active",
                    "The original device transfer or another operation is still active; wait before checking the iPhone.");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string operationId = NewOperationId(now);
            var target = source.Target with { Kind = "reconciliation" };
            var record = new OperationRecordDto(
                operationId,
                OperationReconciliationEvidence.OperationType,
                "queued",
                now,
                StartedAt: null,
                now,
                CompletedAt: null,
                actor,
                idempotencyKey,
                Attempt: 1,
                target,
                [
                    new OperationStageDto(
                        "preflight",
                        "Reconciliation preflight",
                        "succeeded",
                        now,
                        now,
                        "The unknown operation is eligible for a verify-only iPhone check."),
                    new OperationStageDto(
                        "verify",
                        "Check iPhone",
                        "pending",
                        StartedAt: null,
                        CompletedAt: null,
                        "Waiting for a fresh device read."),
                    new OperationStageDto(
                        "activate-registration",
                        "Save verified state",
                        "pending",
                        StartedAt: null,
                        CompletedAt: null,
                        "Waiting for device evidence."),
                ],
                Result: null,
                Error: null,
                Cancelable: true,
                Retryable: false,
                Rerunnable: false,
                CorrelationId: operationId,
                ParentOperationId: source.OperationId,
                ActorMemberId: actorMemberId,
                OwnerMemberId: ownerMemberId);

            (OperationRecordDto stored, bool created) =
                await store.AddIfIdempotentMissingAsync(record, ct).ConfigureAwait(false);
            if (!created)
                return new OperationReconciliationSubmissionResult(stored, Created: false);

            queue.Enqueue(stored.OperationId);
            return new OperationReconciliationSubmissionResult(stored, Created: true);
        }
        finally
        {
            _submissionGate.Release();
        }
    }

    public Task<InstallSubmissionResult> InstallAsync(
        FirstInstallRequest request,
        OperationActorDto actor,
        CancellationToken ct = default) =>
        InstallAsync(
            request,
            actor,
            actorMemberId: null,
            ownerMemberId: null,
            ct: ct);

    public async Task<InstallSubmissionResult> InstallAsync(
        FirstInstallRequest request,
        OperationActorDto actor,
        string? actorMemberId,
        string? ownerMemberId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        string deviceUdid = RequiredIntentValue(request.DeviceUdid, nameof(request.DeviceUdid));
        string bundleId = RequiredIntentValue(request.BundleId, nameof(request.BundleId));
        string? requestedCatalogAppId = OptionalIntentValue(request.CatalogAppId);
        string? requestedAccountProfileId = OptionalIntentValue(request.AccountProfileId);
        string idempotencyKey = RequiredIntentValue(request.IdempotencyKey, nameof(request.IdempotencyKey));
        if (idempotencyKey.Length > 256)
            throw new ArgumentException("Idempotency key must be 256 characters or fewer.", nameof(request));
        if (string.IsNullOrWhiteSpace(actor.Kind) || string.IsNullOrWhiteSpace(actor.DisplayName))
            throw new ArgumentException("A verified operation actor is required.", nameof(actor));
        actorMemberId = NormalizeOwnershipId(actorMemberId);
        ownerMemberId = NormalizeOwnershipId(ownerMemberId);

        bool allowOwnerManagedAppleAuthority = true;
        if (executionAuthorization is not null)
        {
            WorkspaceExecutionDecision executionDecision = await executionAuthorization
                .AuthorizeSubmissionAsync(
                    actor,
                    actorMemberId,
                    ownerMemberId,
                    deviceUdid,
                    enrollmentTarget: false,
                    assignDefaultOwner: false,
                    ct: ct)
                .ConfigureAwait(false);
            if (!executionDecision.IsAllowed)
            {
                return InstallRejected(
                    executionDecision.ErrorCode ?? "operation-access-revoked",
                    executionDecision.Message ?? "Sideport access changed before this install could start.");
            }
            allowOwnerManagedAppleAuthority = executionDecision.CanUseOwnerManagedAppleAuthority;
        }

        await _submissionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OperationRecordDto? replay = await store.FindByActorAndIdempotencyAsync(
                "install",
                actor,
                idempotencyKey,
                ct).ConfigureAwait(false);
            if (replay is not null)
            {
                bool matches = InstallIntentMatches(
                    replay.InstallIntent,
                    deviceUdid,
                    bundleId,
                    requestedCatalogAppId,
                    requestedAccountProfileId,
                    request.FinishOnboarding);
                if (matches && ShouldEnqueueInstall(replay))
                    queue.Enqueue(replay.OperationId);
                return matches
                    ? new InstallSubmissionResult(replay, Created: false)
                    : new InstallSubmissionResult(
                        null,
                        Created: false,
                        "idempotency-target-conflict",
                        "This idempotency key was already used for a different install target.");
            }

            string? preflightId = OptionalIntentValue(request.PreflightId);
            string? confirmedPlanVersion = OptionalIntentValue(request.PlanVersion);
            if (preflightId is null || confirmedPlanVersion is null)
                return InstallRejected("install-preflight-required", "Review and confirm the current install plan before installing.");
            if (!request.ConfirmedPlannedMutations)
                return InstallRejected("install-confirmation-required", "Confirm the planned Apple and device changes before installing.");

            if (!_installPreflights.TryGetValue(preflightId, out InstallPreflightAuthorization? authorization) ||
                authorization.Preflight.ExpiresAt is not { } authorizationExpiry ||
                authorizationExpiry <= DateTimeOffset.UtcNow)
            {
                _installPreflights.TryRemove(preflightId, out _);
                InstallPreflightBuild replacement = await BuildInstallPreflightAsync(
                    deviceUdid,
                    bundleId,
                    request.FinishOnboarding,
                    requestedCatalogAppId,
                    requestedAccountProfileId,
                    allowOwnerManagedAppleAuthority,
                    persistAuthorization: true,
                    ct).ConfigureAwait(false);
                return InstallPreflightStale(replacement.Preflight, "The install preflight expired or is no longer available.");
            }
            if (authorization.FinishOnboarding != request.FinishOnboarding)
                return InstallRejected("install-confirmation-mismatch", "The onboarding finish choice does not match the confirmed install plan.");

            bool targetMatchesAuthorization =
                string.Equals(authorization.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(authorization.BundleId, bundleId, StringComparison.Ordinal) &&
                (requestedCatalogAppId is null ||
                 string.Equals(authorization.CatalogAppId, requestedCatalogAppId, StringComparison.OrdinalIgnoreCase)) &&
                (requestedAccountProfileId is null ||
                 string.Equals(authorization.AccountProfileId, requestedAccountProfileId, StringComparison.Ordinal)) &&
                authorization.AllowOwnerManagedAppleAuthority == allowOwnerManagedAppleAuthority;
            if (!targetMatchesAuthorization ||
                !string.Equals(authorization.Preflight.PlanVersion, confirmedPlanVersion, StringComparison.Ordinal) ||
                authorization.ConsumedByIdempotencyKey is not null)
            {
                InstallPreflightBuild replacement = await BuildInstallPreflightAsync(
                    deviceUdid,
                    bundleId,
                    request.FinishOnboarding,
                    requestedCatalogAppId,
                    requestedAccountProfileId,
                    allowOwnerManagedAppleAuthority,
                    persistAuthorization: true,
                    ct).ConfigureAwait(false);
                return InstallPreflightStale(replacement.Preflight, "The confirmed install plan no longer matches this request.");
            }

            InstallPreflightBuild current = await BuildInstallPreflightAsync(
                deviceUdid,
                bundleId,
                request.FinishOnboarding,
                authorization.CatalogAppId,
                authorization.AccountProfileId,
                allowOwnerManagedAppleAuthority,
                persistAuthorization: true,
                ct).ConfigureAwait(false);
            if (!string.Equals(current.Preflight.PlanVersion, authorization.Preflight.PlanVersion, StringComparison.Ordinal))
                return InstallPreflightStale(current.Preflight, "The install plan changed after it was confirmed.");
            if (!current.Preflight.Ready)
            {
                OperationIssueDto blocker = current.Preflight.Blockers.FirstOrDefault()
                    ?? new OperationIssueDto("install-preflight-blocked", "The current install plan is blocked.");
                return InstallRejected(blocker.Code, blocker.Message);
            }

            CatalogAppDto catalogApp = current.CatalogApp
                ?? throw new InvalidOperationException("A ready install preflight has no catalog artifact.");
            PersonalAppleInstallContext apple = current.Apple
                ?? throw new InvalidOperationException("A ready install preflight has no Apple signing context.");
            AppRegistration? existingRegistration = current.ExistingRegistration;

            string durableIpaPath = await ipaStore.StoreAsync(
                deviceUdid,
                catalogApp.BundleId,
                catalogApp.IpaPath,
                ct).ConfigureAwait(false);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var registration = new AppRegistration(
                catalogApp.BundleId,
                apple.AppleId,
                apple.TeamId,
                deviceUdid,
                durableIpaPath,
                Lifecycle: "pending-install",
                CatalogAppId: catalogApp.Id,
                CreatedAt: existingRegistration?.CreatedAt ?? now,
                ActivatedAt: null,
                LastVerifiedOperationId: null,
                CatalogVersion: catalogApp.CatalogVersion,
                CatalogSha256: catalogApp.Sha256);
            await registry.UpsertAsync(registration, ct).ConfigureAwait(false);

            var intent = new InstallOperationIntentDto(
                deviceUdid,
                catalogApp.Id,
                apple.AccountProfileId,
                catalogApp.BundleId,
                request.FinishOnboarding,
                registration.Key,
                preflightId,
                confirmedPlanVersion,
                current.Preflight.InventoryVersion,
                request.ConfirmedPlannedMutations,
                CatalogVersion: catalogApp.CatalogVersion,
                CatalogSha256: catalogApp.Sha256);
            string operationId = NewOperationId(now);
            var stages = new List<OperationStageDto>
            {
                new("preflight", "Preflight", "succeeded", now, now, "Server, Apple, iPhone, and catalog checks passed."),
                new("install", "Sign and install", "pending", null, null, "Waiting for Sideport's single-flight signer."),
                new("verify", "Verify on iPhone", "pending", null, null, "Waiting for installation."),
                new("activate-registration", "Activate app", "pending", null, null, "Waiting for device verification."),
            };
            if (request.FinishOnboarding)
            {
                stages.Add(new("enable-scheduler", "Enable automatic refresh", "pending", null, null, "Waiting for app activation."));
                stages.Add(new("compute-next-evaluation", "Schedule next check", "pending", null, null, "Waiting for automatic refresh."));
                stages.Add(new("write-completion-receipt", "Finish setup", "pending", null, null, "Waiting for final verification."));
            }
            var record = new OperationRecordDto(
                operationId,
                "install",
                "queued",
                now,
                null,
                now,
                null,
                actor,
                idempotencyKey,
                Attempt: 1,
                new OperationTargetDto(
                    deviceUdid,
                    catalogApp.BundleId,
                    TeamId: apple.TeamId,
                    Kind: "catalog-app",
                    CatalogAppId: catalogApp.Id,
                    AccountProfileId: apple.AccountProfileId,
                    CatalogVersion: catalogApp.CatalogVersion,
                    Version: PreferredVersion(catalogApp),
                    CatalogSha256: catalogApp.Sha256),
                stages,
                Result: null,
                Error: null,
                Cancelable: true,
                Retryable: false,
                Rerunnable: false,
                CorrelationId: operationId,
                InstallIntent: intent,
                ActorMemberId: actorMemberId,
                OwnerMemberId: ownerMemberId);

            (OperationRecordDto stored, bool created) = await store.AddIfIdempotentMissingAsync(record, ct).ConfigureAwait(false);
            if (!created)
            {
                bool matches = InstallIntentMatches(
                    stored.InstallIntent,
                    deviceUdid,
                    bundleId,
                    requestedCatalogAppId,
                    requestedAccountProfileId,
                    request.FinishOnboarding);
                if (matches && ShouldEnqueueInstall(stored))
                    queue.Enqueue(stored.OperationId);
                return matches
                    ? new InstallSubmissionResult(stored, Created: false)
                    : new InstallSubmissionResult(null, false, "idempotency-target-conflict", "This idempotency key targets a different install.");
            }

            _installPreflights.TryUpdate(
                preflightId,
                authorization with { ConsumedByIdempotencyKey = idempotencyKey },
                authorization);
            queue.Enqueue(stored.OperationId);
            return new InstallSubmissionResult(stored, Created: true);
        }
        finally
        {
            _submissionGate.Release();
        }
    }

    public async Task ProcessQueuedOperationAsync(string operationId, CancellationToken ct = default)
    {
        OperationRecordDto? record = await store.FindAsync(operationId, ct).ConfigureAwait(false);
        if (record is null)
            return;
        if (string.Equals(record.Type, "install", StringComparison.Ordinal))
        {
            await ProcessQueuedInstallAsync(operationId, ct).ConfigureAwait(false);
            return;
        }
        if (string.Equals(record.Type, "verify-existing-registration", StringComparison.Ordinal))
        {
            await ProcessQueuedExistingRegistrationVerificationAsync(operationId, ct).ConfigureAwait(false);
            return;
        }
        if (string.Equals(record.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal))
        {
            await ProcessQueuedReconciliationAsync(operationId, ct).ConfigureAwait(false);
            return;
        }
        if (string.Equals(record.Type, "refresh", StringComparison.Ordinal))
            await ProcessQueuedRefreshAsync(operationId, ct).ConfigureAwait(false);
    }

    public async Task<(OnboardingCompletionReceipt? Receipt, bool Created, string? Error, string? Message)> CompleteOnboardingAsync(
        string verifiedOperationId,
        string idempotencyKey,
        OperationActorDto actor,
        CancellationToken ct = default)
    {
        string operationId = RequiredIntentValue(verifiedOperationId, nameof(verifiedOperationId));
        string replayKey = RequiredIntentValue(idempotencyKey, nameof(idempotencyKey));
        if (replayKey.Length > 256)
            throw new ArgumentException("Idempotency key must be 256 characters or fewer.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(actor.Kind) || string.IsNullOrWhiteSpace(actor.DisplayName))
            throw new ArgumentException("A verified operation actor is required.", nameof(actor));

        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OnboardingCompletionReceipt? existingReceipt = await onboardingCompletion.ReadAsync(ct).ConfigureAwait(false);
            if (existingReceipt is not null)
                return (existingReceipt, false, null, null);

            OperationRecordDto? operation = await store.FindAsync(operationId, ct).ConfigureAwait(false);
            if (operation is not null &&
                string.Equals(operation.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) &&
                HasVerifiedReconciliationEvidence(operation) &&
                operation.Status is "running" or "waiting" or "recovery-required" or "succeeded" &&
                !string.IsNullOrWhiteSpace(operation.ParentOperationId))
            {
                OperationRecordDto? source = await store.FindAsync(operation.ParentOperationId, ct).ConfigureAwait(false);
                if (source is null ||
                    !string.Equals(source.Type, "install", StringComparison.Ordinal) ||
                    source.InstallIntent?.FinishOnboarding != true)
                {
                    return (
                        null,
                        false,
                        "onboarding-incomplete",
                        "The reconciliation is not linked to a first-install setup operation.");
                }

                await FinalizeVerifiedReconciliationAsync(operation, source, ct).ConfigureAwait(false);
                OnboardingCompletionReceipt? reconciledReceipt = await onboardingCompletion.ReadAsync(ct).ConfigureAwait(false);
                return reconciledReceipt is null
                    ? (null, false, "onboarding-incomplete", "Sideport still needs to finish one or more reconciled setup writes.")
                    : (reconciledReceipt, true, null, null);
            }

            // Compatibility callers retain the original first-install ID. If
            // that install became outcome-unknown and a linked verify-only
            // reconciliation later saved matching evidence, resume from the
            // child without asking the client to discover a new identifier.
            if (operation is not null &&
                string.Equals(operation.Type, "install", StringComparison.Ordinal) &&
                string.Equals(operation.Status, "unknown", StringComparison.Ordinal) &&
                operation.InstallIntent?.FinishOnboarding == true)
            {
                IReadOnlyList<OperationRecordDto> records = await store.ListAsync(
                    limit: null,
                    ct: ct).ConfigureAwait(false);
                OperationRecordDto? reconciliation = records.FirstOrDefault(candidate =>
                    string.Equals(candidate.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) &&
                    string.Equals(candidate.ParentOperationId, operation.OperationId, StringComparison.Ordinal) &&
                    candidate.Status is "running" or "waiting" or "recovery-required" or "succeeded" &&
                    HasVerifiedReconciliationEvidence(candidate) &&
                    ReconciliationTargetMatchesSource(candidate, operation));
                if (reconciliation is not null)
                {
                    await FinalizeVerifiedReconciliationAsync(reconciliation, operation, ct).ConfigureAwait(false);
                    OnboardingCompletionReceipt? reconciledReceipt = await onboardingCompletion
                        .ReadAsync(ct)
                        .ConfigureAwait(false);
                    return reconciledReceipt is null
                        ? (null, false, "onboarding-incomplete", "Sideport still needs to finish one or more reconciled setup writes.")
                        : (reconciledReceipt, true, null, null);
                }
            }

            if (operation is null ||
                !string.Equals(operation.Type, "install", StringComparison.Ordinal) ||
                operation.InstallIntent?.FinishOnboarding != true ||
                !HasVerifiedInstallEvidence(operation) ||
                operation.Status is not ("running" or "waiting" or "recovery-required" or "succeeded"))
            {
                return (
                    null,
                    false,
                    "onboarding-incomplete",
                    "The selected operation does not contain a device-verified first install.");
            }

            await FinalizeVerifiedInstallAsync(operation, ct).ConfigureAwait(false);
            OnboardingCompletionReceipt? receipt = await onboardingCompletion.ReadAsync(ct).ConfigureAwait(false);
            return receipt is null
                ? (null, false, "onboarding-incomplete", "Sideport still needs to finish one or more verified setup writes.")
                : (receipt, true, null, null);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ProcessQueuedInstallAsync(string operationId, CancellationToken ct = default)
    {
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OperationRecordDto? submitted = await store.FindAsync(operationId, ct).ConfigureAwait(false);
            if (submitted is null || !string.Equals(submitted.Type, "install", StringComparison.Ordinal))
                return;
            if (!HasVerifiedInstallEvidence(submitted) &&
                !await EnsureExecutionAuthorizedAsync(submitted, "install", ct).ConfigureAwait(false))
            {
                return;
            }

            DateTimeOffset started = DateTimeOffset.UtcNow;
            OperationRecordDto? record = await store.TransitionAsync(operationId, existing =>
            {
                if (!string.Equals(existing.Type, "install", StringComparison.Ordinal))
                    return null;
                if (string.Equals(existing.Status, "running", StringComparison.Ordinal) &&
                    HasVerifiedInstallEvidence(existing))
                    return existing;
                if (string.Equals(existing.Status, "waiting", StringComparison.Ordinal) &&
                    HasVerifiedInstallEvidence(existing))
                {
                    return existing with
                    {
                        Status = "running",
                        UpdatedAt = started,
                        Error = null,
                    };
                }
                if (!string.Equals(existing.Status, "queued", StringComparison.Ordinal))
                    return null;
                return existing with
                {
                    Status = "running",
                    StartedAt = started,
                    UpdatedAt = started,
                    Cancelable = false,
                    Stages = existing.Stages.Select(stage =>
                        string.Equals(stage.Id, "install", StringComparison.Ordinal)
                            ? stage with { Status = "running", StartedAt = started, Message = "Sideport is signing and installing the app." }
                            : stage).ToArray(),
                };
            }, ct).ConfigureAwait(false);
            if (record is null || !string.Equals(record.Status, "running", StringComparison.Ordinal))
                return;

            InstallOperationIntentDto? intent = record.InstallIntent;
            if (intent is null ||
                string.IsNullOrWhiteSpace(record.Target.DeviceUdid) ||
                string.IsNullOrWhiteSpace(record.Target.BundleId))
            {
                await FailInstallAsync(record.OperationId, "install-intent-invalid", "The durable install intent is incomplete.", ct).ConfigureAwait(false);
                return;
            }

            if (HasVerifiedInstallEvidence(record))
            {
                await FinalizeVerifiedInstallAsync(record, ct).ConfigureAwait(false);
                return;
            }

            if (!await EnsureExecutionAuthorizedAsync(record, "install", ct).ConfigureAwait(false))
                return;

            PersonalAppleInstallContext apple;
            try
            {
                apple = await personalApple.Value.ResolveFreshInstallContextAsync(intent.AccountProfileId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is AppleAccountProfileNotFoundException or AppleTeamSelectionStaleException or AppleTeamNotReturnedException)
            {
                await FailInstallAsync(record.OperationId, "apple-install-context-stale", "The Apple account or selected team is no longer ready.", ct).ConfigureAwait(false);
                return;
            }

            AppRegistration? registration = await registry.FindAsync(intent.DeviceUdid, intent.BundleId, ct).ConfigureAwait(false);
            if (registration is null ||
                (!registration.IsPendingInstall && !string.Equals(registration.LastVerifiedOperationId, record.OperationId, StringComparison.Ordinal)) ||
                !string.Equals(registration.CatalogAppId, intent.CatalogAppId, StringComparison.OrdinalIgnoreCase) ||
                intent.CatalogVersion is null ||
                string.IsNullOrWhiteSpace(intent.CatalogSha256) ||
                registration.CatalogVersion != intent.CatalogVersion ||
                record.Target.CatalogVersion != intent.CatalogVersion ||
                !string.Equals(registration.CatalogSha256, intent.CatalogSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(registration.AppleId, apple.AppleId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(registration.TeamId, apple.TeamId, StringComparison.Ordinal))
            {
                await FailInstallAsync(record.OperationId, "pending-registration-missing", "The pending app registration no longer matches this install.", ct).ConfigureAwait(false);
                return;
            }

            try
            {
                string durableSha256 = await ComputeFileSha256Async(registration.InputIpaPath, ct).ConfigureAwait(false);
                if (!string.Equals(durableSha256, intent.CatalogSha256, StringComparison.OrdinalIgnoreCase))
                {
                    await FailInstallAsync(
                        record.OperationId,
                        "install-artifact-lineage-changed",
                        "The saved IPA changed after the install plan was confirmed.",
                        ct).ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await FailInstallAsync(
                    record.OperationId,
                    "install-artifact-lineage-unavailable",
                    "The saved IPA could not be revalidated before installation.",
                    ct).ConfigureAwait(false);
                return;
            }

            if (!await EnsureExecutionAuthorizedAsync(record, "install", ct).ConfigureAwait(false))
                return;
            (string? DeviceError, string? DeviceMessage) = await ValidateInstallDeviceAsync(intent.DeviceUdid, ct).ConfigureAwait(false);
            if (DeviceError is not null)
            {
                await FailInstallAsync(record.OperationId, DeviceError, DeviceMessage!, ct).ConfigureAwait(false);
                return;
            }

            WorkspaceExecutionDecision? installExecutionDecision =
                await EnsureExecutionAuthorizationDecisionAsync(record, "install", ct).ConfigureAwait(false);
            if (installExecutionDecision is { IsAllowed: false })
                return;
            if (installExecutionDecision?.CanUseOwnerManagedAppleAuthority == false &&
                !await CurrentCatalogApprovalMatchesAsync(intent, ct).ConfigureAwait(false))
            {
                await FailInstallAsync(
                    record.OperationId,
                    "owner-action-required",
                    "The home Owner must approve the current app version before installation can continue.",
                    ct).ConfigureAwait(false);
                return;
            }
            RefreshExecutionPolicy installPolicy = installExecutionDecision?.CanUseOwnerManagedAppleAuthority == false
                ? RefreshExecutionPolicy.ExistingAuthorityOnly
                : RefreshExecutionPolicy.OwnerManaged;
            RefreshResult refresh = signerAuthorityGate is null
                ? await orchestrator.RefreshAsync(intent.DeviceUdid, intent.BundleId, installPolicy, ct).ConfigureAwait(false)
                : await signerAuthorityGate.RunAsync(
                    gateCt => orchestrator.RefreshAsync(intent.DeviceUdid, intent.BundleId, installPolicy, gateCt), ct).ConfigureAwait(false);
            if (!refresh.Success)
            {
                if (string.Equals(refresh.ErrorCode, "install-outcome-unknown", StringComparison.Ordinal))
                {
                    await MarkInstallUnknownAsync(
                        record.OperationId,
                        refresh.Error ?? "Sideport cannot prove whether the iPhone install completed.",
                        refresh.NewExpiry,
                        ct).ConfigureAwait(false);
                    return;
                }
                await FailInstallAsync(
                    record.OperationId,
                    refresh.ErrorCode ?? "install-failed",
                    refresh.ErrorCode is null
                        ? "Sideport could not sign or install the selected app."
                        : refresh.Error ?? "Sideport needs an additional signing action before installation can continue.",
                    ct).ConfigureAwait(false);
                return;
            }

            DateTimeOffset installedAt = DateTimeOffset.UtcNow;
            record = (await store.TransitionAsync(record.OperationId, existing => existing with
            {
                UpdatedAt = installedAt,
                Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "install", StringComparison.Ordinal)
                    ? stage with { Status = "succeeded", CompletedAt = installedAt, Message = "The app was installed." }
                    : string.Equals(stage.Id, "verify", StringComparison.Ordinal)
                        ? stage with { Status = "running", StartedAt = installedAt, Message = "Reading the installed signature from the iPhone." }
                        : stage).ToArray(),
            }, ct).ConfigureAwait(false))!;

            IReadOnlyList<InstalledApp> installedApps;
            try
            {
                installedApps = await devices.ListInstalledAppsFreshAsync(intent.DeviceUdid, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await FailInstallAsync(record.OperationId, "install-verification-unavailable", "Sideport could not read installed apps from the iPhone.", ct).ConfigureAwait(false);
                return;
            }
            InstalledApp? installed = installedApps.FirstOrDefault(app =>
                string.Equals(app.BundleId, intent.BundleId, StringComparison.Ordinal));
            (string? expectedVersion, OperationIssueDto? artifactError) = InspectRegistrationArtifact(registration);
            bool expiryMatches = installed?.SignatureExpiresAt is { } installedExpiry &&
                installedExpiry > DateTimeOffset.UtcNow &&
                refresh.NewExpiry is { } preparedExpiry &&
                Math.Abs((installedExpiry - preparedExpiry).TotalSeconds) <= 60;
            if (artifactError is not null ||
                installed is null ||
                string.IsNullOrWhiteSpace(installed.Version) ||
                !string.Equals(installed.Version.Trim(), expectedVersion, StringComparison.Ordinal) ||
                !expiryMatches)
            {
                await FailInstallAsync(
                    record.OperationId,
                    "install-verification-failed",
                    "The installed bundle, version, and signing expiry could not be verified.",
                    ct).ConfigureAwait(false);
                return;
            }

            DateTimeOffset verifiedAt = DateTimeOffset.UtcNow;
            var successful = new OperationResultDto(
                Success: true,
                BundleId: intent.BundleId,
                ExpiresAt: installed.SignatureExpiresAt,
                Error: null,
                Version: installed.Version.Trim());
            record = (await store.TransitionAsync(record.OperationId, existing => existing with
            {
                UpdatedAt = verifiedAt,
                Result = successful,
                Error = null,
                Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "verify", StringComparison.Ordinal)
                    ? stage with { Status = "succeeded", CompletedAt = verifiedAt, Message = "The bundle and signature expiry were verified on the iPhone." }
                    : string.Equals(stage.Id, "activate-registration", StringComparison.Ordinal)
                        ? stage with { Status = "running", StartedAt = verifiedAt, Message = "Enabling automatic refresh for this app." }
                        : stage).ToArray(),
            }, ct).ConfigureAwait(false))!;

            await FinalizeVerifiedInstallAsync(record, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            OperationRecordDto? persisted = null;
            try
            {
                persisted = await store.FindAsync(operationId, ct).ConfigureAwait(false);
            }
            catch (OperationStoreException)
            {
                // The worker-level logger will preserve the store failure if the
                // best-effort transition below also cannot be persisted.
            }

            if (persisted is not null && HasVerifiedInstallEvidence(persisted))
            {
                await MarkInstallFinalizationPendingAsync(
                    persisted.OperationId,
                    "install-finalization-pending",
                    "The app was verified, but Sideport still needs to save automatic refresh and setup completion.",
                    ct).ConfigureAwait(false);
                return;
            }

            await FailInstallAsync(
                operationId,
                "install-worker-failed",
                "The install stopped because a required Sideport service became unavailable.",
                ct).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ProcessQueuedExistingRegistrationVerificationAsync(
        string operationId,
        CancellationToken ct = default)
    {
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OperationRecordDto? submitted = await store.FindAsync(operationId, ct).ConfigureAwait(false);
            if (submitted is null ||
                !string.Equals(submitted.Type, "verify-existing-registration", StringComparison.Ordinal))
            {
                return;
            }
            if (!HasVerifiedExistingRegistrationEvidence(submitted) &&
                !await EnsureExecutionAuthorizedAsync(submitted, "verify", ct).ConfigureAwait(false))
            {
                return;
            }

            DateTimeOffset started = DateTimeOffset.UtcNow;
            OperationRecordDto? record = await store.TransitionAsync(operationId, existing =>
            {
                if (!string.Equals(existing.Type, "verify-existing-registration", StringComparison.Ordinal))
                    return null;
                if (existing.Status is not ("queued" or "waiting" or "running"))
                    return null;

                if (HasVerifiedExistingRegistrationEvidence(existing))
                {
                    return existing with
                    {
                        Status = "running",
                        UpdatedAt = started,
                        Error = null,
                        Cancelable = false,
                    };
                }

                return existing with
                {
                    Status = "running",
                    StartedAt = existing.StartedAt ?? started,
                    UpdatedAt = started,
                    Error = null,
                    Cancelable = false,
                    Retryable = false,
                    Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "verify", StringComparison.Ordinal)
                        ? stage with
                        {
                            Status = "running",
                            StartedAt = stage.StartedAt ?? started,
                            CompletedAt = null,
                            Message = "Reading the installed app and signing profile from the iPhone.",
                            Error = null,
                        }
                        : stage).ToArray(),
                };
            }, ct).ConfigureAwait(false);
            if (record is null ||
                !string.Equals(record.Type, "verify-existing-registration", StringComparison.Ordinal) ||
                !string.Equals(record.Status, "running", StringComparison.Ordinal))
            {
                return;
            }

            if (HasVerifiedExistingRegistrationEvidence(record))
            {
                await FinalizeVerifiedExistingRegistrationAsync(record, ct).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(record.Target.DeviceUdid) ||
                string.IsNullOrWhiteSpace(record.Target.BundleId) ||
                string.IsNullOrWhiteSpace(record.Target.Version) ||
                string.IsNullOrWhiteSpace(record.Target.CatalogSha256))
            {
                await FailExistingRegistrationVerificationAsync(
                    operationId,
                    "operation-target-invalid",
                    "The saved verification target is incomplete.",
                    ct).ConfigureAwait(false);
                return;
            }

            string deviceUdid = record.Target.DeviceUdid;
            string bundleId = record.Target.BundleId;
            IReadOnlyList<OperationRecordDto> operations = await store.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
            if (operations.Any(operation =>
                    !string.Equals(operation.OperationId, operationId, StringComparison.Ordinal) &&
                    string.Equals(operation.Target.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase) &&
                    (operation.Status is "queued" or "waiting" or "running" ||
                     OperationReconciliationEvidence.IsUnresolvedMutation(operation, operations))))
            {
                await FailExistingRegistrationVerificationAsync(
                    operationId,
                    "device-operation-still-active",
                    "Another operation still owns or has unresolved state for this iPhone.",
                    ct).ConfigureAwait(false);
                return;
            }

            AppRegistration? registration = await registry.FindAsync(deviceUdid, bundleId, ct).ConfigureAwait(false);
            if (registration is null || registration.IsPendingInstall || !VerificationTargetMatches(record.Target, registration))
            {
                await FailExistingRegistrationVerificationAsync(
                    operationId,
                    registration is null ? "registration-not-found" : "registration-lineage-changed",
                    registration is null
                        ? "The app registration was removed before Sideport could verify it."
                        : "The app registration changed after verification was requested; review it before trying again.",
                    ct).ConfigureAwait(false);
                return;
            }

            (string? expectedVersion, OperationIssueDto? artifactError) = InspectRegistrationArtifact(registration);
            if (artifactError is not null ||
                !string.Equals(expectedVersion, record.Target.Version, StringComparison.Ordinal) ||
                !await VerificationArtifactHashMatchesAsync(record.Target, registration, ct).ConfigureAwait(false))
            {
                await FailExistingRegistrationVerificationAsync(
                    operationId,
                    artifactError?.Code ?? "registration-lineage-changed",
                    artifactError?.Message ?? "The saved IPA changed after verification was requested.",
                    ct).ConfigureAwait(false);
                return;
            }

            if (!await EnsureExecutionAuthorizedAsync(record, "verify", ct).ConfigureAwait(false))
                return;
            (string? deviceError, string? deviceMessage) =
                await ValidateInstallDeviceAsync(deviceUdid, ct).ConfigureAwait(false);
            if (deviceError is not null)
            {
                await FailExistingRegistrationVerificationAsync(
                    operationId,
                    deviceError,
                    deviceMessage!,
                    ct).ConfigureAwait(false);
                return;
            }

            IReadOnlyList<InstalledApp> installedApps;
            try
            {
                if (!await EnsureExecutionAuthorizedAsync(record, "verify", ct).ConfigureAwait(false))
                    return;
                installedApps = await devices.ListInstalledAppsFreshAsync(deviceUdid, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                await FailExistingRegistrationVerificationAsync(
                    operationId,
                    "existing-registration-verification-unavailable",
                    "Sideport could not read installed apps and profiles from the iPhone.",
                    ct).ConfigureAwait(false);
                return;
            }

            InstalledApp? installed = installedApps.FirstOrDefault(app =>
                string.Equals(app.BundleId, bundleId, StringComparison.Ordinal));
            if (installed is null)
            {
                await FailExistingRegistrationVerificationAsync(
                    operationId,
                    "installed-app-not-found",
                    "This app is not installed on the iPhone; use Install to add it.",
                    ct).ConfigureAwait(false);
                return;
            }
            if (string.IsNullOrWhiteSpace(installed.Version) ||
                !string.Equals(installed.Version.Trim(), expectedVersion, StringComparison.Ordinal))
            {
                await FailExistingRegistrationVerificationAsync(
                    operationId,
                    "installed-app-version-mismatch",
                    "The installed app version does not match Sideport's saved IPA; use Install to replace it.",
                    ct).ConfigureAwait(false);
                return;
            }
            if (installed.SignatureExpiresAt is not { } signatureExpiresAt ||
                signatureExpiresAt <= DateTimeOffset.UtcNow)
            {
                await FailExistingRegistrationVerificationAsync(
                    operationId,
                    "installed-profile-unverified",
                    "Sideport could not verify a current signing profile for the installed app; use Install to replace it.",
                    ct).ConfigureAwait(false);
                return;
            }

            DateTimeOffset verifiedAt = DateTimeOffset.UtcNow;
            record = (await store.TransitionAsync(operationId, existing => existing with
            {
                UpdatedAt = verifiedAt,
                Result = new OperationResultDto(
                    Success: true,
                    BundleId: bundleId,
                    ExpiresAt: signatureExpiresAt,
                    Error: null,
                    Version: installed.Version.Trim()),
                Error = null,
                Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "verify", StringComparison.Ordinal)
                    ? stage with
                    {
                        Status = "succeeded",
                        CompletedAt = verifiedAt,
                        Message = "The installed bundle, version, and signing expiry were verified on the iPhone.",
                        Error = null,
                    }
                    : string.Equals(stage.Id, "activate-registration", StringComparison.Ordinal)
                        ? stage with
                        {
                            Status = "running",
                            StartedAt = verifiedAt,
                            Message = "Saving the verified registration.",
                            Error = null,
                        }
                        : stage).ToArray(),
            }, ct).ConfigureAwait(false))!;

            await FinalizeVerifiedExistingRegistrationAsync(record, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            OperationRecordDto? persisted = null;
            try
            {
                persisted = await store.FindAsync(operationId, ct).ConfigureAwait(false);
            }
            catch (OperationStoreException)
            {
                // The worker logger retains the store failure if the best-effort
                // recovery marker below cannot be persisted either.
            }

            if (persisted is not null && HasVerifiedExistingRegistrationEvidence(persisted))
            {
                await MarkExistingVerificationFinalizationPendingAsync(
                    persisted.OperationId,
                    ct).ConfigureAwait(false);
                return;
            }

            await FailExistingRegistrationVerificationAsync(
                operationId,
                "existing-registration-verification-failed",
                "Sideport could not complete the existing app verification.",
                ct).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ProcessQueuedReconciliationAsync(
        string operationId,
        CancellationToken ct = default)
    {
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OperationRecordDto? submitted = await store.FindAsync(operationId, ct).ConfigureAwait(false);
            if (submitted is null ||
                !string.Equals(submitted.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal))
            {
                return;
            }
            if (!HasVerifiedReconciliationEvidence(submitted) &&
                !await EnsureExecutionAuthorizedAsync(submitted, "verify", ct).ConfigureAwait(false))
            {
                return;
            }

            DateTimeOffset startedAt = DateTimeOffset.UtcNow;
            OperationRecordDto? record = await store.TransitionAsync(operationId, existing =>
            {
                if (!string.Equals(existing.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) ||
                    existing.Status is not ("queued" or "waiting" or "running"))
                {
                    return null;
                }

                if (HasVerifiedReconciliationEvidence(existing))
                {
                    return existing with
                    {
                        Status = "running",
                        UpdatedAt = startedAt,
                        Error = null,
                        Cancelable = false,
                    };
                }

                return existing with
                {
                    Status = "running",
                    StartedAt = existing.StartedAt ?? startedAt,
                    UpdatedAt = startedAt,
                    CompletedAt = null,
                    Error = null,
                    Cancelable = false,
                    Retryable = false,
                    Rerunnable = false,
                    Stages = existing.Stages.Select(stage =>
                        string.Equals(stage.Id, "verify", StringComparison.Ordinal)
                            ? stage with
                            {
                                Status = "running",
                                StartedAt = stage.StartedAt ?? startedAt,
                                CompletedAt = null,
                                Message = "Reading installed apps and signing profiles directly from the iPhone.",
                                Error = null,
                            }
                            : stage).ToArray(),
                };
            }, ct).ConfigureAwait(false);
            if (record is null || !string.Equals(record.Status, "running", StringComparison.Ordinal))
                return;

            OperationRecordDto? source = string.IsNullOrWhiteSpace(record.ParentOperationId)
                ? null
                : await store.FindAsync(record.ParentOperationId, ct).ConfigureAwait(false);
            if (source is null ||
                source.Type is not ("install" or "refresh") ||
                !string.Equals(source.Status, "unknown", StringComparison.Ordinal))
            {
                await BlockReconciliationAsync(
                    record.OperationId,
                    "reconciliation-source-invalid",
                    "The original unknown operation is missing or no longer eligible for reconciliation.",
                    ct).ConfigureAwait(false);
                return;
            }
            if (!string.Equals(record.OwnerMemberId, source.OwnerMemberId, StringComparison.Ordinal))
            {
                await BlockReconciliationAsync(
                    record.OperationId,
                    "resource-ownership-changed",
                    "The iPhone ownership snapshot no longer matches the original operation.",
                    ct).ConfigureAwait(false);
                return;
            }

            if (HasVerifiedReconciliationEvidence(record))
            {
                await FinalizeVerifiedReconciliationAsync(record, source, ct).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrWhiteSpace(record.Target.DeviceUdid) ||
                string.IsNullOrWhiteSpace(record.Target.BundleId) ||
                string.IsNullOrWhiteSpace(record.Target.Version) ||
                string.IsNullOrWhiteSpace(record.Target.CatalogSha256) ||
                source.Result?.ExpiresAt is null ||
                !ReconciliationTargetMatchesSource(record, source))
            {
                await BlockReconciliationAsync(
                    record.OperationId,
                    "reconciliation-lineage-invalid",
                    "The saved reconciliation no longer has the exact unknown-operation lineage it needs.",
                    ct).ConfigureAwait(false);
                return;
            }

            string deviceUdid = record.Target.DeviceUdid;
            string bundleId = record.Target.BundleId;
            IReadOnlyList<OperationRecordDto> operations = await store.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
            if (orchestrator.IsDeviceMutationActive(deviceUdid) ||
                operations.Any(operation =>
                    !string.Equals(operation.OperationId, record.OperationId, StringComparison.Ordinal) &&
                    !string.Equals(operation.OperationId, source.OperationId, StringComparison.Ordinal) &&
                    operation.Status is "queued" or "waiting" or "running" &&
                    string.Equals(operation.Target.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase)))
            {
                await BlockReconciliationAsync(
                    record.OperationId,
                    "device-operation-still-active",
                    "A device mutation is still active; Sideport did not read or change the iPhone.",
                    ct).ConfigureAwait(false);
                return;
            }

            if (!await EnsureExecutionAuthorizedAsync(record, "verify", ct).ConfigureAwait(false))
                return;
            (string? deviceError, string? deviceMessage) =
                await ValidateInstallDeviceAsync(deviceUdid, ct).ConfigureAwait(false);
            if (deviceError is not null)
            {
                await BlockReconciliationAsync(
                    record.OperationId,
                    deviceError,
                    deviceMessage!,
                    ct).ConfigureAwait(false);
                return;
            }

            AppRegistration? registration = await registry.FindAsync(deviceUdid, bundleId, ct).ConfigureAwait(false);
            if (registration is null || !ReconciliationRegistrationMatches(record, source, registration))
            {
                await BlockReconciliationAsync(
                    record.OperationId,
                    "reconciliation-registration-lineage-changed",
                    "The app registration changed after the unknown operation; Sideport did not infer a device result.",
                    ct).ConfigureAwait(false);
                return;
            }

            (string? expectedVersion, OperationIssueDto? artifactError) = InspectRegistrationArtifact(registration);
            if (artifactError is not null ||
                !string.Equals(expectedVersion, record.Target.Version, StringComparison.Ordinal) ||
                !await ReconciliationArtifactHashMatchesAsync(record.Target, registration, ct).ConfigureAwait(false))
            {
                await BlockReconciliationAsync(
                    record.OperationId,
                    "reconciliation-artifact-lineage-changed",
                    "The saved IPA no longer matches the artifact used by the unknown operation.",
                    ct).ConfigureAwait(false);
                return;
            }

            IReadOnlyList<InstalledApp> installedApps;
            try
            {
                if (!await EnsureExecutionAuthorizedAsync(record, "verify", ct).ConfigureAwait(false))
                    return;
                installedApps = await devices.ListInstalledAppsFreshAsync(deviceUdid, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                await BlockReconciliationAsync(
                    record.OperationId,
                    "reconciliation-device-read-unavailable",
                    "Sideport could not read installed apps and signing profiles from the iPhone.",
                    ct).ConfigureAwait(false);
                return;
            }

            InstalledApp? installed = installedApps.FirstOrDefault(app =>
                string.Equals(app.BundleId, bundleId, StringComparison.Ordinal));
            if (installed is null)
            {
                if (orchestrator.IsDeviceMutationActive(deviceUdid))
                {
                    await BlockReconciliationAsync(
                        record.OperationId,
                        "device-operation-still-active",
                        "A device mutation became active while Sideport checked the iPhone.",
                        ct).ConfigureAwait(false);
                    return;
                }

                await CompleteSafeToRerunReconciliationAsync(record, source, ct).ConfigureAwait(false);
                return;
            }

            bool expiryMatches = installed.SignatureExpiresAt is { } installedExpiry &&
                installedExpiry > DateTimeOffset.UtcNow &&
                Math.Abs((installedExpiry - source.Result.ExpiresAt.Value).TotalSeconds) <= 60;
            if (string.IsNullOrWhiteSpace(installed.Version) ||
                !string.Equals(installed.Version.Trim(), record.Target.Version, StringComparison.Ordinal) ||
                !expiryMatches)
            {
                await BlockReconciliationAsync(
                    record.OperationId,
                    "reconciliation-evidence-mismatch",
                    "The installed version or signing profile does not match the unknown operation.",
                    ct,
                    installed.Version,
                    installed.SignatureExpiresAt).ConfigureAwait(false);
                return;
            }

            DateTimeOffset verifiedAt = DateTimeOffset.UtcNow;
            record = (await store.TransitionAsync(record.OperationId, existing => existing with
            {
                Status = "running",
                UpdatedAt = verifiedAt,
                CompletedAt = null,
                Result = new OperationResultDto(
                    Success: true,
                    BundleId: bundleId,
                    ExpiresAt: installed.SignatureExpiresAt,
                    Error: null,
                    Version: installed.Version.Trim(),
                    SafeToRerun: false,
                    ReconciledOperationId: source.OperationId),
                Error = null,
                Cancelable = false,
                Retryable = false,
                Rerunnable = false,
                Stages = EnsureReconciliationFinalizationStages(
                    existing.Stages.Select(stage =>
                        string.Equals(stage.Id, "verify", StringComparison.Ordinal)
                            ? stage with
                            {
                                Status = "succeeded",
                                CompletedAt = verifiedAt,
                                Message = "The installed bundle, version, and signing profile match the unknown operation.",
                                Error = null,
                            }
                            : string.Equals(stage.Id, "activate-registration", StringComparison.Ordinal)
                                ? stage with
                                {
                                    Status = "running",
                                    StartedAt = verifiedAt,
                                    Message = "Saving the verified reconciliation.",
                                    Error = null,
                                }
                                : stage),
                        source),
            }, ct).ConfigureAwait(false))!;

            await FinalizeVerifiedReconciliationAsync(record, source, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            OperationRecordDto? persisted = null;
            try
            {
                persisted = await store.FindAsync(operationId, ct).ConfigureAwait(false);
            }
            catch (OperationStoreException)
            {
                // The worker logger retains the durable-store failure.
            }

            if (persisted is not null && HasVerifiedReconciliationEvidence(persisted))
            {
                await MarkReconciliationFinalizationPendingAsync(persisted.OperationId, ct).ConfigureAwait(false);
                return;
            }

            await BlockReconciliationAsync(
                operationId,
                "reconciliation-failed",
                "Sideport could not complete the verify-only iPhone check.",
                ct).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ProcessQueuedRefreshAsync(string operationId, CancellationToken ct = default)
    {
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OperationRecordDto? submitted = await store.FindAsync(operationId, ct).ConfigureAwait(false);
            if (submitted is null || !string.Equals(submitted.Type, "refresh", StringComparison.Ordinal))
                return;
            WorkspaceExecutionDecision? executionDecision =
                await EnsureExecutionAuthorizationDecisionAsync(submitted, "refresh", ct).ConfigureAwait(false);
            if (executionDecision is { IsAllowed: false })
                return;

            DateTimeOffset started = DateTimeOffset.UtcNow;
            OperationRecordDto? record = await store.TransitionAsync(operationId, existing =>
            {
                if (!string.Equals(existing.Type, "refresh", StringComparison.Ordinal) ||
                    !string.Equals(existing.Status, "queued", StringComparison.Ordinal))
                    return null;

                OperationStageDto[] runningStages = existing.Stages.Select(stage =>
                    string.Equals(stage.Id, "refresh", StringComparison.Ordinal)
                        ? stage with { Status = "running", StartedAt = started, Message = "Refresh is running." }
                        : stage).ToArray();

                return existing with
                {
                    Status = "running",
                    StartedAt = started,
                    UpdatedAt = started,
                    Stages = runningStages,
                    Cancelable = false,
                };
            }, ct).ConfigureAwait(false);
            if (record is null || !string.Equals(record.Status, "running", StringComparison.Ordinal))
                return;

            if (string.IsNullOrWhiteSpace(record.Target.DeviceUdid) || string.IsNullOrWhiteSpace(record.Target.BundleId))
            {
                DateTimeOffset failedAt = DateTimeOffset.UtcNow;
                var invalidTarget = new OperationIssueDto(
                    "refresh-target-invalid",
                    "The durable refresh operation is missing its device or app target.");
                await store.TransitionAsync(record.OperationId, existing => existing with
                {
                    Status = "failed",
                    UpdatedAt = failedAt,
                    CompletedAt = failedAt,
                    Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "refresh", StringComparison.Ordinal)
                        ? stage with
                        {
                            Status = "failed",
                            CompletedAt = failedAt,
                            Message = invalidTarget.Message,
                            Error = invalidTarget,
                        }
                        : stage).ToArray(),
                    Error = invalidTarget,
                    Cancelable = false,
                    Retryable = false,
                    Rerunnable = false,
                }, ct).ConfigureAwait(false);
                return;
            }

            OperationPreflightDto currentPreflight;
            try
            {
                executionDecision = await EnsureExecutionAuthorizationDecisionAsync(
                    record,
                    "refresh",
                    ct).ConfigureAwait(false);
                if (executionDecision is { IsAllowed: false })
                    return;
                bool allowOwnerManagedAppleAuthority =
                    executionDecision?.CanUseOwnerManagedAppleAuthority ?? true;
                currentPreflight = await PreflightRefreshAsync(
                    record.Target.DeviceUdid,
                    record.Target.BundleId,
                    ct,
                    excludingOperationId: record.OperationId,
                    allowAppleAuthentication: allowOwnerManagedAppleAuthority,
                    requireCurrentCatalogApproval: !allowOwnerManagedAppleAuthority).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                DateTimeOffset failedAt = DateTimeOffset.UtcNow;
                var unavailable = new OperationIssueDto(
                    "refresh-preflight-unavailable",
                    "Sideport could not recheck the refresh prerequisites before changing the iPhone.");
                await store.TransitionAsync(record.OperationId, existing => existing with
                {
                    Status = "failed",
                    UpdatedAt = failedAt,
                    CompletedAt = failedAt,
                    Error = unavailable,
                    Cancelable = false,
                    Retryable = true,
                    Rerunnable = false,
                    Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "refresh", StringComparison.Ordinal)
                        ? stage with
                        {
                            Status = "failed",
                            CompletedAt = failedAt,
                            Message = unavailable.Message,
                            Error = unavailable,
                        }
                        : stage).ToArray(),
                }, ct).ConfigureAwait(false);
                return;
            }
            if (!currentPreflight.Ready)
            {
                DateTimeOffset blockedAt = DateTimeOffset.UtcNow;
                OperationIssueDto blocker = currentPreflight.Blockers.FirstOrDefault()
                    ?? new OperationIssueDto("refresh-preflight-blocked", "Current checks blocked the refresh.");
                await store.TransitionAsync(record.OperationId, existing => existing with
                {
                    Status = "blocked",
                    UpdatedAt = blockedAt,
                    CompletedAt = blockedAt,
                    Error = blocker,
                    Cancelable = false,
                    Retryable = true,
                    Rerunnable = false,
                    Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "refresh", StringComparison.Ordinal)
                        ? stage with
                        {
                            Status = "blocked",
                            CompletedAt = blockedAt,
                            Message = blocker.Message,
                            Error = blocker,
                        }
                        : stage).ToArray(),
                }, ct).ConfigureAwait(false);
                return;
            }

            executionDecision = await EnsureExecutionAuthorizationDecisionAsync(
                record,
                "refresh",
                ct).ConfigureAwait(false);
            if (executionDecision is { IsAllowed: false })
                return;
            RefreshExecutionPolicy refreshPolicy = executionDecision?.CanUseOwnerManagedAppleAuthority == false
                ? RefreshExecutionPolicy.ExistingAuthorityOnly
                : RefreshExecutionPolicy.OwnerManaged;
            RefreshResult result = signerAuthorityGate is null
                ? await orchestrator.RefreshAsync(record.Target.DeviceUdid, record.Target.BundleId, refreshPolicy, ct).ConfigureAwait(false)
                : await signerAuthorityGate.RunAsync(
                    gateCt => orchestrator.RefreshAsync(record.Target.DeviceUdid, record.Target.BundleId, refreshPolicy, gateCt), ct).ConfigureAwait(false);
            string? verifiedVersion = null;
            if (result.Success)
            {
                AppRegistration? registration = await registry.FindAsync(
                    record.Target.DeviceUdid,
                    record.Target.BundleId,
                    ct).ConfigureAwait(false);
                (string? expectedVersion, OperationIssueDto? artifactError) = registration is null
                    ? (null, new OperationIssueDto("registration-missing", "The app registration disappeared during refresh."))
                    : InspectRegistrationArtifact(registration);
                IReadOnlyList<InstalledApp>? installedApps = null;
                if (artifactError is null)
                {
                    try
                    {
                        installedApps = await devices
                            .ListInstalledAppsFreshAsync(record.Target.DeviceUdid, ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        artifactError = new OperationIssueDto(
                            "install-verification-unknown",
                            "The transfer stopped, but Sideport could not read the installed app back from the iPhone.");
                    }
                }

                InstalledApp? installed = installedApps?.FirstOrDefault(app =>
                    string.Equals(app.BundleId, record.Target.BundleId, StringComparison.Ordinal));
                bool expiryMatches = installed?.SignatureExpiresAt is { } installedExpiry &&
                    installedExpiry > DateTimeOffset.UtcNow &&
                    result.NewExpiry is { } preparedExpiry &&
                    Math.Abs((installedExpiry - preparedExpiry).TotalSeconds) <= 60;
                if (artifactError is not null ||
                    installed is null ||
                    string.IsNullOrWhiteSpace(installed.Version) ||
                    !string.Equals(installed.Version.Trim(), expectedVersion, StringComparison.Ordinal) ||
                    !expiryMatches)
                {
                    result = result with
                    {
                        Success = false,
                        Error = artifactError?.Message ??
                            "The transfer stopped, but the installed bundle, version, or signing profile did not match the refresh plan.",
                        ErrorCode = "install-verification-unknown",
                    };
                }
                else
                {
                    verifiedVersion = installed.Version.Trim();
                    result = result with { NewExpiry = installed.SignatureExpiresAt };
                }
            }
            DateTimeOffset completed = DateTimeOffset.UtcNow;
            bool outcomeUnknown = result.ErrorCode is "install-outcome-unknown" or "install-verification-unknown";
            OperationIssueDto? terminalError = result.Success
                ? null
                : new OperationIssueDto(result.ErrorCode ?? "refresh-failed", result.Error ?? "Refresh failed.");
            OperationStageDto[] completedStages = record.Stages.Select(stage =>
                string.Equals(stage.Id, "refresh", StringComparison.Ordinal)
                    ? stage with
                    {
                        Status = result.Success ? "succeeded" : outcomeUnknown ? "unknown" : "failed",
                        CompletedAt = outcomeUnknown ? null : completed,
                        Message = result.Success
                            ? "The refreshed bundle, version, and signing expiry were verified on the iPhone."
                            : terminalError!.Message,
                        Error = terminalError,
                    }
                    : stage).ToArray();

            record = record with
            {
                Status = result.Success ? "succeeded" : outcomeUnknown ? "unknown" : "failed",
                UpdatedAt = completed,
                CompletedAt = outcomeUnknown ? null : completed,
                Stages = completedStages,
                Result = new OperationResultDto(
                    result.Success,
                    result.BundleId,
                    result.NewExpiry,
                    result.Error,
                    Version: verifiedVersion),
                Error = terminalError,
                Cancelable = false,
                Retryable = !result.Success && !outcomeUnknown,
                Rerunnable = result.Success,
            };
            await store.UpdateAsync(record, ct).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RequeuePendingAsync(CancellationToken ct = default)
    {
        IReadOnlyList<OperationRecordDto> records = await store.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        foreach (OperationRecordDto record in records.Where(record =>
                     (string.Equals(record.Type, "refresh", StringComparison.Ordinal) &&
                      record.Status is "queued" or "waiting") ||
                     (string.Equals(record.Type, "install", StringComparison.Ordinal) &&
                      (record.Status is "queued" or "waiting" ||
                       (string.Equals(record.Status, "running", StringComparison.Ordinal) &&
                        HasVerifiedInstallEvidence(record)))) ||
                     (string.Equals(record.Type, "verify-existing-registration", StringComparison.Ordinal) &&
                      record.Status is "queued" or "waiting" or "running") ||
                     (string.Equals(record.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) &&
                      (record.Status is "queued" or "waiting" ||
                       (string.Equals(record.Status, "running", StringComparison.Ordinal) &&
                        HasVerifiedReconciliationEvidence(record))))))
            queue.Enqueue(record.OperationId);
    }

    public async Task<(OperationRecordDto? Record, string? Error)> CancelAsync(string operationId, string? reason, CancellationToken ct = default)
    {
        OperationRecordDto? record = await store.FindAsync(operationId, ct).ConfigureAwait(false);
        if (record is null)
            return (null, "operation-not-found");
        if (record.Status is "canceled" or "canceling")
            return (record, null);
        if (record.Status is not ("queued" or "waiting"))
            return (record, "operation-not-cancelable");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationIssueDto canceled = new("operation-canceled", string.IsNullOrWhiteSpace(reason) ? "Operation canceled before signing started." : reason.Trim());
        OperationRecordDto? updated = await store.TransitionAsync(operationId, existing =>
        {
            if (existing.Status is "canceled" or "canceling")
                return existing;
            if (existing.Status is not ("queued" or "waiting"))
                return null;
            return existing with
            {
                Status = "canceled",
                UpdatedAt = now,
                CompletedAt = now,
                Error = canceled,
                Cancelable = false,
                Retryable = false,
                Rerunnable = string.Equals(existing.Type, "refresh", StringComparison.Ordinal),
            };
        }, ct).ConfigureAwait(false);
        if (updated is null)
            return (null, "operation-not-found");
        return updated.Status == "canceled" ? (updated, null) : (updated, "operation-not-cancelable");
    }

    public Task<(OperationRecordDto? Record, bool Created, string? Error)> RetryAsync(
        string operationId,
        OperationActorDto actor,
        string? idempotencyKey,
        CancellationToken ct = default) =>
        RetryAsync(operationId, actor, idempotencyKey, actorMemberId: null, ownerMemberId: null, ct: ct);

    public async Task<(OperationRecordDto? Record, bool Created, string? Error)> RetryAsync(
        string operationId,
        OperationActorDto actor,
        string? idempotencyKey,
        string? actorMemberId,
        string? ownerMemberId,
        CancellationToken ct = default)
    {
        OperationRecordDto? source = await store.FindAsync(operationId, ct).ConfigureAwait(false);
        if (source is null)
            return (null, false, "operation-not-found");
        if (!string.Equals(source.Type, "refresh", StringComparison.Ordinal) || !source.Retryable)
            return (source, false, "operation-not-retryable");
        return await RefreshFromSourceAsync(
            source,
            actor,
            idempotencyKey,
            actorMemberId,
            ownerMemberId,
            source.Attempt + 1,
            ct).ConfigureAwait(false);
    }

    public Task<(OperationRecordDto? Record, bool Created, string? Error)> RerunAsync(
        string operationId,
        OperationActorDto actor,
        string? idempotencyKey,
        CancellationToken ct = default) =>
        RerunAsync(operationId, actor, idempotencyKey, actorMemberId: null, ownerMemberId: null, ct: ct);

    public async Task<(OperationRecordDto? Record, bool Created, string? Error)> RerunAsync(
        string operationId,
        OperationActorDto actor,
        string? idempotencyKey,
        string? actorMemberId,
        string? ownerMemberId,
        CancellationToken ct = default)
    {
        OperationRecordDto? source = await store.FindAsync(operationId, ct).ConfigureAwait(false);
        if (source is null)
            return (null, false, "operation-not-found");

        if (string.Equals(source.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) &&
            string.Equals(source.Status, "succeeded", StringComparison.Ordinal) &&
            source.Result?.SafeToRerun == true &&
            !string.IsNullOrWhiteSpace(source.ParentOperationId))
        {
            OperationRecordDto? original = await store.FindAsync(source.ParentOperationId, ct).ConfigureAwait(false);
            return original is not null && string.Equals(original.Type, "refresh", StringComparison.Ordinal)
                ? await RefreshFromSourceAsync(original, actor, idempotencyKey, actorMemberId, ownerMemberId, 1, ct).ConfigureAwait(false)
                : (source, false, "operation-not-rerunnable");
        }

        if (string.Equals(source.Type, "refresh", StringComparison.Ordinal) &&
            string.Equals(source.Status, "unknown", StringComparison.Ordinal))
        {
            IReadOnlyList<OperationRecordDto> records = await store.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
            bool safeToRerun = records.Any(candidate =>
                string.Equals(candidate.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) &&
                string.Equals(candidate.Status, "succeeded", StringComparison.Ordinal) &&
                string.Equals(candidate.ParentOperationId, source.OperationId, StringComparison.Ordinal) &&
                candidate.Result?.SafeToRerun == true &&
                string.Equals(candidate.Result.ReconciledOperationId, source.OperationId, StringComparison.Ordinal));
            return safeToRerun
                ? await RefreshFromSourceAsync(source, actor, idempotencyKey, actorMemberId, ownerMemberId, 1, ct).ConfigureAwait(false)
                : (source, false, "operation-not-rerunnable");
        }

        if (!string.Equals(source.Type, "refresh", StringComparison.Ordinal) ||
            source.CompletedAt is null ||
            !source.Rerunnable)
            return (source, false, "operation-not-rerunnable");
        return await RefreshFromSourceAsync(source, actor, idempotencyKey, actorMemberId, ownerMemberId, 1, ct).ConfigureAwait(false);
    }

    private async Task<(OperationRecordDto? Record, bool Created, string? Error)> RefreshFromSourceAsync(
        OperationRecordDto source,
        OperationActorDto actor,
        string? idempotencyKey,
        string? actorMemberId,
        string? ownerMemberId,
        int attempt,
        CancellationToken ct)
    {
        if (!string.Equals(source.Type, "refresh", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(source.Target.DeviceUdid) ||
            string.IsNullOrWhiteSpace(source.Target.BundleId))
        {
            return (source, false, "operation-target-invalid");
        }

        actorMemberId = NormalizeOwnershipId(actorMemberId);
        ownerMemberId = NormalizeOwnershipId(ownerMemberId);
        if (ownerMemberId is not null &&
            !string.Equals(ownerMemberId, source.OwnerMemberId, StringComparison.Ordinal))
        {
            return (source, false, "resource-not-found");
        }

        if (executionAuthorization is not null)
        {
            WorkspaceExecutionDecision authorization = await executionAuthorization
                .AuthorizeOperationAsync(source with
                {
                    Actor = actor,
                    ActorMemberId = actorMemberId,
                }, ct: ct)
                .ConfigureAwait(false);
            if (!authorization.IsAllowed)
                return (source, false, authorization.ErrorCode ?? "operation-access-revoked");
        }

        (OperationRecordDto record, bool created) = await RefreshAsync(
            source.Target.DeviceUdid,
            source.Target.BundleId,
            actor,
            idempotencyKey,
            actorMemberId,
            source.OwnerMemberId,
            source.OperationId,
            attempt,
            ct).ConfigureAwait(false);
        return (record, created, null);
    }

    public async Task<IReadOnlyList<RenewalItemDto>> RenewalsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AppRegistration> apps = await registry.ListAsync(ct).ConfigureAwait(false);
        IReadOnlyList<OperationRecordDto> operations = await store.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return apps.Select(app => ToRenewal(
            app,
            orchestrator.GetState(app.DeviceUdid, app.BundleId),
            LatestOperation(app, operations),
            LatestSuccessfulOperation(app, operations),
            now)).ToArray();
    }

    private static RenewalItemDto ToRenewal(
        AppRegistration app,
        RefreshState? state,
        OperationRecordDto? latestOperation,
        OperationRecordDto? latestSuccessfulOperation,
        DateTimeOffset now)
    {
        if (latestSuccessfulOperation is not null &&
            string.Equals(latestSuccessfulOperation.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) &&
            (state?.LastAttemptUtc is null || latestSuccessfulOperation.UpdatedAt >= state.LastAttemptUtc))
        {
            state = null;
        }
        DateTimeOffset? effectiveExpiry = state?.ExpiresAt ?? DurableSuccessfulExpiry(latestSuccessfulOperation);
        string risk = RenewalRisk(state, latestOperation, effectiveExpiry, now);
        string status = latestOperation?.Status switch
        {
            "running" => "running",
            "blocked" => "blocked",
            "failed" => "failed",
            "unknown" or "recovery-required" => "blocked",
            _ when state?.LastSucceeded == false => "failed",
            _ => "idle",
        };
        string? blocker = latestOperation?.Error?.Message ?? state?.LastError;
        return new RenewalItemDto(
            $"{app.DeviceUdid}:{app.BundleId}",
            app.DeviceUdid,
            app.BundleId,
            app.TeamId,
            risk,
            status,
            effectiveExpiry,
            blocker,
            latestOperation?.OperationId);
    }

    private static DateTimeOffset? DurableSuccessfulExpiry(OperationRecordDto? latestOperation) =>
        latestOperation is { Status: "succeeded", Result.Success: true }
            ? latestOperation.Result.ExpiresAt
            : null;

    private static OperationRecordDto? LatestOperation(AppRegistration app, IReadOnlyList<OperationRecordDto> operations) =>
        operations.FirstOrDefault(operation =>
            (string.Equals(operation.Type, "refresh", StringComparison.Ordinal) ||
             (string.Equals(operation.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) &&
              operation.Result?.Success == true)) &&
            string.Equals(operation.Target.DeviceUdid, app.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Target.BundleId, app.BundleId, StringComparison.Ordinal));

    private static OperationRecordDto? LatestSuccessfulOperation(AppRegistration app, IReadOnlyList<OperationRecordDto> operations) =>
        operations.FirstOrDefault(operation =>
            operation.Type is "refresh" or "reconcile" &&
            string.Equals(operation.Status, "succeeded", StringComparison.Ordinal) &&
            operation.Result?.Success == true &&
            string.Equals(operation.Target.DeviceUdid, app.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Target.BundleId, app.BundleId, StringComparison.Ordinal));

    private static (string? Version, OperationIssueDto? Error) InspectRegistrationArtifact(
        AppRegistration registration)
    {
        if (!File.Exists(registration.InputIpaPath))
        {
            return (
                null,
                new OperationIssueDto(
                    "registration-artifact-unavailable",
                    "Sideport's saved IPA is missing; add the app again before verifying this registration."));
        }

        try
        {
            IpaInfo info = IpaInspector.Inspect(registration.InputIpaPath);
            if (!string.Equals(info.BundleIdentifier, registration.BundleId, StringComparison.Ordinal))
            {
                return (
                    null,
                    new OperationIssueDto(
                        "registration-artifact-mismatch",
                        "Sideport's saved IPA no longer matches this app registration."));
            }

            string? version = string.IsNullOrWhiteSpace(info.ShortVersion)
                ? info.Version?.Trim()
                : info.ShortVersion.Trim();
            return string.IsNullOrWhiteSpace(version)
                ? (
                    null,
                    new OperationIssueDto(
                        "registration-version-unavailable",
                        "Sideport's saved IPA has no version that can be matched to the installed app."))
                : (version, null);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return (
                null,
                new OperationIssueDto(
                    "registration-artifact-unavailable",
                    "Sideport could not inspect the saved IPA; add the app again before verifying this registration."));
        }
    }

    private static string? PreferredVersion(IpaInfo info) =>
        string.IsNullOrWhiteSpace(info.ShortVersion)
            ? info.Version?.Trim()
            : info.ShortVersion.Trim();

    private static string? PreferredVersion(CatalogAppDto app) =>
        string.IsNullOrWhiteSpace(app.ShortVersion)
            ? app.Version?.Trim()
            : app.ShortVersion.Trim();

    private static bool VerificationTargetMatches(
        OperationTargetDto target,
        AppRegistration registration) =>
        string.Equals(target.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(target.BundleId, registration.BundleId, StringComparison.Ordinal) &&
        string.Equals(target.TeamId, registration.TeamId, StringComparison.Ordinal) &&
        string.Equals(target.CatalogAppId, registration.CatalogAppId, StringComparison.OrdinalIgnoreCase) &&
        target.CatalogVersion == registration.CatalogVersion &&
        (string.IsNullOrWhiteSpace(registration.CatalogSha256) ||
         string.Equals(target.CatalogSha256, registration.CatalogSha256, StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(
            target.AccountProfileId,
            AppleAccountIdentity.ProfileIdFor(registration.AppleId),
            StringComparison.Ordinal);

    private static async Task<bool> VerificationArtifactHashMatchesAsync(
        OperationTargetDto target,
        AppRegistration registration,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target.CatalogSha256))
            return false;
        try
        {
            string actual = await ComputeFileSha256Async(registration.InputIpaPath, ct).ConfigureAwait(false);
            return string.Equals(actual, target.CatalogSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string RenewalRisk(RefreshState? state, OperationRecordDto? latestOperation, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (state?.LastSucceeded == false || latestOperation?.Status is "blocked" or "failed") return "blocked";
        if (expiresAt is not { } expiry) return "unknown";
        TimeSpan remaining = expiry - now;
        if (remaining <= TimeSpan.Zero) return "due-now";
        if (remaining <= TimeSpan.FromDays(2)) return "upcoming";
        return "healthy";
    }

    private async Task<(string? Error, string? Message)> ValidateInstallDeviceAsync(
        string deviceUdid,
        CancellationToken ct)
    {
        KnownDeviceRecord? known = await knownDevices.FindAsync(deviceUdid, ct).ConfigureAwait(false);
        if (known is null ||
            !string.Equals(known.InventoryState, "accepted", StringComparison.Ordinal) ||
            known.AcceptedAt is null ||
            string.IsNullOrWhiteSpace(known.AcceptedBy) ||
            string.IsNullOrWhiteSpace(known.EnrollmentOperationId))
        {
            return ("device-not-accepted", "Add and accept this iPhone in Sideport before installing an app.");
        }

        IReadOnlyList<DeviceInfo> reachable;
        try
        {
            reachable = await devices.ListDevicesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ("device-not-reachable", "Sideport could not discover the accepted iPhone over USB.");
        }
        DeviceInfo? usb = reachable.FirstOrDefault(device =>
            string.Equals(device.Udid, deviceUdid, StringComparison.OrdinalIgnoreCase) &&
            device.Connection == DeviceConnection.Usb);
        if (usb is null)
        {
            bool wifiOnly = reachable.Any(device =>
                string.Equals(device.Udid, deviceUdid, StringComparison.OrdinalIgnoreCase) &&
                device.Connection == DeviceConnection.Wifi);
            return wifiOnly
                ? ("device-usb-required", "Connect the accepted iPhone over USB for its first install.")
                : ("device-not-reachable", "Connect the accepted iPhone over USB, unlock it, and try again.");
        }

        DeviceTrustProbe trust;
        try
        {
            trust = await devices.ProbeTrustAsync(deviceUdid, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ("device-trust-check-unavailable", "Sideport could not verify Trust with the accepted iPhone.");
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (trust.Connection != DeviceConnection.Usb)
            return ("device-usb-required", "The current trusted connection must be USB for the first install.");
        if (!string.Equals(trust.TrustState, "trusted", StringComparison.OrdinalIgnoreCase) ||
            !trust.UsableForInstall ||
            trust.LockdownCheckedAt > now.AddSeconds(5) ||
            now - trust.LockdownCheckedAt > TimeSpan.FromMinutes(1))
        {
            return ("device-not-trusted", "Unlock the iPhone and complete Trust This Computer before installing.");
        }
        return (null, null);
    }

    private async Task<(string? Error, string? Message, DeviceConnection? Connection)>
        ValidateRefreshDeviceAsync(
            string deviceUdid,
            CancellationToken ct)
    {
        KnownDeviceRecord? known = await knownDevices.FindAsync(deviceUdid, ct).ConfigureAwait(false);
        if (known is null ||
            !string.Equals(known.InventoryState, "accepted", StringComparison.Ordinal) ||
            known.AcceptedAt is null ||
            string.IsNullOrWhiteSpace(known.EnrollmentOperationId))
        {
            return (
                "device-not-accepted",
                "Add and accept this iPhone over USB before refreshing an app.",
                null);
        }

        IReadOnlyList<DeviceInfo> reachable;
        try
        {
            reachable = await devices.ListDevicesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (
                "device-not-reachable",
                "Reconnect the accepted iPhone over USB or paired Wi-Fi before refreshing.",
                null);
        }

        DeviceInfo? current = reachable
            .Where(device => string.Equals(device.Udid, deviceUdid, StringComparison.OrdinalIgnoreCase))
            .OrderBy(device => device.Connection == DeviceConnection.Usb ? 0 : 1)
            .FirstOrDefault();
        if (current is null)
        {
            return (
                "device-not-reachable",
                "Reconnect the accepted iPhone over USB or paired Wi-Fi before refreshing.",
                null);
        }

        try
        {
            DeviceTrustProbe trust = await devices.ProbeTrustAsync(deviceUdid, ct).ConfigureAwait(false);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (!string.Equals(trust.TrustState, "trusted", StringComparison.OrdinalIgnoreCase) ||
                !trust.UsableForInstall ||
                trust.LockdownCheckedAt > now.AddSeconds(5) ||
                now - trust.LockdownCheckedAt > TimeSpan.FromMinutes(1))
            {
                return (
                    "device-not-trusted",
                    "Unlock the iPhone and restore its saved Trust connection before refreshing.",
                    trust.Connection);
            }
            return (null, null, trust.Connection);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (
                "device-trust-check-unavailable",
                "Sideport could not verify the iPhone's saved Trust connection.",
                current.Connection);
        }
    }

    private static OperationRecordDto? FindVerifiedRegistrationEvidence(
        IReadOnlyList<OperationRecordDto> records,
        AppRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(registration.LastVerifiedOperationId))
            return null;

        return records.FirstOrDefault(operation =>
            string.Equals(operation.OperationId, registration.LastVerifiedOperationId, StringComparison.Ordinal) &&
            operation.Type is "install" or "refresh" or "verify-existing-registration" or "reconcile" &&
            string.Equals(operation.Status, "succeeded", StringComparison.Ordinal) &&
            operation.Result is { Success: true, ExpiresAt: not null } &&
            string.Equals(operation.Target.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Target.BundleId, registration.BundleId, StringComparison.Ordinal));
    }

    private async Task FinalizeVerifiedExistingRegistrationAsync(
        OperationRecordDto record,
        CancellationToken ct)
    {
        if (!HasVerifiedExistingRegistrationEvidence(record) ||
            string.IsNullOrWhiteSpace(record.Target.DeviceUdid) ||
            string.IsNullOrWhiteSpace(record.Target.BundleId))
        {
            throw new InvalidOperationException("Existing registration verification has no durable device evidence.");
        }

        if (record.Result!.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await BlockExistingVerificationFinalizationAsync(
                record.OperationId,
                "installed-profile-expired-after-verification",
                "The saved signing profile expired before Sideport could finish linking it; use Install to replace the app.",
                ct).ConfigureAwait(false);
            return;
        }

        AppRegistration? registration = await registry.FindAsync(
            record.Target.DeviceUdid,
            record.Target.BundleId,
            ct).ConfigureAwait(false);
        if (registration is null || registration.IsPendingInstall || !VerificationTargetMatches(record.Target, registration))
        {
            await BlockExistingVerificationFinalizationAsync(
                record.OperationId,
                "registration-lineage-changed",
                "The app registration changed after the iPhone was checked; review the registration and use Install if needed.",
                ct).ConfigureAwait(false);
            return;
        }

        (string? expectedVersion, OperationIssueDto? artifactError) = InspectRegistrationArtifact(registration);
        if (artifactError is not null ||
            !string.Equals(expectedVersion, record.Result!.Version, StringComparison.Ordinal) ||
            !string.Equals(expectedVersion, record.Target.Version, StringComparison.Ordinal) ||
            !await VerificationArtifactHashMatchesAsync(record.Target, registration, ct).ConfigureAwait(false))
        {
            await BlockExistingVerificationFinalizationAsync(
                record.OperationId,
                "registration-lineage-changed",
                "Sideport's saved IPA changed after the iPhone was checked; review the registration and use Install if needed.",
                ct).ConfigureAwait(false);
            return;
        }

        DateTimeOffset verifiedAt = record.Stages
            .First(stage => string.Equals(stage.Id, "verify", StringComparison.Ordinal))
            .CompletedAt!.Value;
        if (!string.Equals(registration.LastVerifiedOperationId, record.OperationId, StringComparison.Ordinal))
        {
            await registry.UpsertAsync(registration with
            {
                Lifecycle = "active",
                ActivatedAt = registration.ActivatedAt ?? verifiedAt,
                LastVerifiedOperationId = record.OperationId,
            }, ct).ConfigureAwait(false);
        }

        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        await store.TransitionAsync(record.OperationId, existing => existing with
        {
            Status = "succeeded",
            UpdatedAt = completedAt,
            CompletedAt = completedAt,
            Error = null,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
            Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "activate-registration", StringComparison.Ordinal)
                ? stage with
                {
                    Status = "succeeded",
                    StartedAt = stage.StartedAt ?? completedAt,
                    CompletedAt = completedAt,
                    Message = "The existing app registration now has durable device-verification evidence.",
                    Error = null,
                }
                : stage).ToArray(),
        }, ct).ConfigureAwait(false);
    }

    private async Task BlockExistingVerificationFinalizationAsync(
        string operationId,
        string code,
        string message,
        CancellationToken ct)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        var issue = new OperationIssueDto(code, message);
        await store.TransitionAsync(operationId, existing => existing with
        {
            Status = "blocked",
            UpdatedAt = completedAt,
            CompletedAt = completedAt,
            Error = issue,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
            Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "activate-registration", StringComparison.Ordinal)
                ? stage with
                {
                    Status = "blocked",
                    StartedAt = stage.StartedAt ?? completedAt,
                    CompletedAt = completedAt,
                    Message = message,
                    Error = issue,
                }
                : stage).ToArray(),
        }, ct).ConfigureAwait(false);
    }

    private async Task FailExistingRegistrationVerificationAsync(
        string operationId,
        string code,
        string message,
        CancellationToken ct)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        var issue = new OperationIssueDto(code, message);
        await store.TransitionAsync(operationId, existing => existing with
        {
            Status = "blocked",
            UpdatedAt = completedAt,
            CompletedAt = completedAt,
            Result = new OperationResultDto(false, existing.Target.BundleId, null, message),
            Error = issue,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
            Stages = existing.Stages.Select(stage => string.Equals(stage.Status, "running", StringComparison.Ordinal)
                ? stage with
                {
                    Status = "blocked",
                    CompletedAt = completedAt,
                    Message = message,
                    Error = issue,
                }
                : stage).ToArray(),
        }, ct).ConfigureAwait(false);
    }

    private async Task MarkExistingVerificationFinalizationPendingAsync(
        string operationId,
        CancellationToken ct)
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var issue = new OperationIssueDto(
            "existing-registration-finalization-pending",
            "The iPhone evidence is saved, but Sideport still needs to link it to the registration.");
        await store.TransitionAsync(operationId, existing => existing with
        {
            Status = "waiting",
            UpdatedAt = observedAt,
            CompletedAt = null,
            Error = issue,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
            Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "activate-registration", StringComparison.Ordinal)
                ? stage with
                {
                    Status = "waiting",
                    StartedAt = stage.StartedAt ?? observedAt,
                    CompletedAt = null,
                    Message = issue.Message,
                    Error = issue,
                }
                : stage).ToArray(),
        }, ct).ConfigureAwait(false);
    }

    private async Task FinalizeVerifiedReconciliationAsync(
        OperationRecordDto record,
        OperationRecordDto source,
        CancellationToken ct)
    {
        if (!HasVerifiedReconciliationEvidence(record) ||
            !ReconciliationTargetMatchesSource(record, source))
        {
            throw new InvalidOperationException("Reconciliation finalization has no durable matching device evidence.");
        }

        if (record.Result!.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await BlockReconciliationAsync(
                record.OperationId,
                "reconciliation-profile-expired-after-verification",
                "The verified signing profile expired before Sideport could save the reconciliation.",
                ct,
                record.Result.Version,
                record.Result.ExpiresAt).ConfigureAwait(false);
            return;
        }

        AppRegistration? registration = await registry.FindAsync(
            record.Target.DeviceUdid!,
            record.Target.BundleId!,
            ct).ConfigureAwait(false);
        if (registration is null || !ReconciliationRegistrationMatches(record, source, registration))
        {
            await BlockReconciliationAsync(
                record.OperationId,
                "reconciliation-registration-lineage-changed",
                "The app registration changed after the iPhone was verified.",
                ct,
                record.Result.Version,
                record.Result.ExpiresAt).ConfigureAwait(false);
            return;
        }

        (string? expectedVersion, OperationIssueDto? artifactError) = InspectRegistrationArtifact(registration);
        if (artifactError is not null ||
            !string.Equals(expectedVersion, record.Result.Version, StringComparison.Ordinal) ||
            !await ReconciliationArtifactHashMatchesAsync(record.Target, registration, ct).ConfigureAwait(false))
        {
            await BlockReconciliationAsync(
                record.OperationId,
                "reconciliation-artifact-lineage-changed",
                "The saved IPA changed after the iPhone was verified.",
                ct,
                record.Result.Version,
                record.Result.ExpiresAt).ConfigureAwait(false);
            return;
        }

        DateTimeOffset verifiedAt = record.Stages
            .First(stage => string.Equals(stage.Id, "verify", StringComparison.Ordinal))
            .CompletedAt!.Value;
        if (registration.IsPendingInstall ||
            !string.Equals(registration.LastVerifiedOperationId, record.OperationId, StringComparison.Ordinal))
        {
            registration = registration with
            {
                Lifecycle = "active",
                ActivatedAt = registration.ActivatedAt ?? verifiedAt,
                LastVerifiedOperationId = record.OperationId,
            };
            await registry.UpsertAsync(registration, ct).ConfigureAwait(false);
        }

        DateTimeOffset activationSavedAt = DateTimeOffset.UtcNow;
        record = (await store.TransitionAsync(record.OperationId, existing => existing with
        {
            Status = "running",
            UpdatedAt = activationSavedAt,
            CompletedAt = null,
            Error = null,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
            Stages = existing.Stages.Select(stage =>
                string.Equals(stage.Id, "activate-registration", StringComparison.Ordinal)
                    ? stage with
                    {
                        Status = "succeeded",
                        StartedAt = stage.StartedAt ?? activationSavedAt,
                        CompletedAt = activationSavedAt,
                        Message = "The registration now points to this verified reconciliation.",
                        Error = null,
                    }
                    : stage).ToArray(),
        }, ct).ConfigureAwait(false))!;

        if (string.Equals(source.Type, "install", StringComparison.Ordinal) &&
            source.InstallIntent?.FinishOnboarding == true)
        {
            var provenInstallStage = new OperationStageDto(
                "install",
                "Original install outcome",
                "succeeded",
                source.StartedAt ?? source.CreatedAt,
                verifiedAt,
                "The linked reconciliation proved that the original install reached this iPhone.");
            OperationRecordDto syntheticVerifiedInstall = record with
            {
                Type = "install",
                Target = source.Target,
                InstallIntent = source.InstallIntent,
                Stages = [provenInstallStage, .. record.Stages],
            };
            await FinalizeVerifiedInstallAsync(syntheticVerifiedInstall, ct).ConfigureAwait(false);
            return;
        }

        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        await store.TransitionAsync(record.OperationId, existing => existing with
        {
            Status = "succeeded",
            UpdatedAt = completedAt,
            CompletedAt = completedAt,
            Error = null,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
        }, ct).ConfigureAwait(false);
    }

    private async Task CompleteSafeToRerunReconciliationAsync(
        OperationRecordDto record,
        OperationRecordDto source,
        CancellationToken ct)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        await store.TransitionAsync(record.OperationId, existing => existing with
        {
            Status = "succeeded",
            UpdatedAt = completedAt,
            CompletedAt = completedAt,
            Result = new OperationResultDto(
                Success: false,
                BundleId: existing.Target.BundleId,
                ExpiresAt: null,
                Error: null,
                Version: null,
                SafeToRerun: true,
                ReconciledOperationId: source.OperationId),
            Error = null,
            Cancelable = false,
            Retryable = false,
            Rerunnable = string.Equals(source.Type, "refresh", StringComparison.Ordinal),
            Stages = existing.Stages.Select(stage => stage.Id switch
            {
                "verify" => stage with
                {
                    Status = "succeeded",
                    CompletedAt = completedAt,
                    Message = "The app is absent and no device mutation remains active.",
                    Error = null,
                },
                "activate-registration" => stage with
                {
                    Status = "succeeded",
                    StartedAt = completedAt,
                    CompletedAt = completedAt,
                    Message = "No registration activation was needed.",
                    Error = null,
                },
                _ => stage,
            }).ToArray(),
        }, ct).ConfigureAwait(false);
    }

    private async Task BlockReconciliationAsync(
        string operationId,
        string code,
        string message,
        CancellationToken ct,
        string? observedVersion = null,
        DateTimeOffset? observedExpiry = null)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        var issue = new OperationIssueDto(code, message);
        await store.TransitionAsync(operationId, existing => existing with
        {
            Status = "blocked",
            UpdatedAt = completedAt,
            CompletedAt = completedAt,
            Result = new OperationResultDto(
                Success: false,
                BundleId: existing.Target.BundleId,
                ExpiresAt: observedExpiry,
                Error: message,
                Version: string.IsNullOrWhiteSpace(observedVersion) ? null : observedVersion.Trim(),
                SafeToRerun: false,
                ReconciledOperationId: existing.ParentOperationId),
            Error = issue,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
            Stages = existing.Stages.Select(stage =>
                string.Equals(stage.Status, "running", StringComparison.Ordinal)
                    ? stage with
                    {
                        Status = "blocked",
                        CompletedAt = completedAt,
                        Message = message,
                        Error = issue,
                    }
                    : stage).ToArray(),
        }, ct).ConfigureAwait(false);
    }

    private async Task MarkReconciliationFinalizationPendingAsync(
        string operationId,
        CancellationToken ct)
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var issue = new OperationIssueDto(
            "reconciliation-finalization-pending",
            "The iPhone evidence is saved, but Sideport still needs to finish durable registration state.");
        await store.TransitionAsync(operationId, existing => existing with
        {
            Status = "waiting",
            UpdatedAt = observedAt,
            CompletedAt = null,
            Error = issue,
            Cancelable = false,
            Retryable = true,
            Rerunnable = false,
        }, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<OperationStageDto> EnsureReconciliationFinalizationStages(
        IEnumerable<OperationStageDto> stages,
        OperationRecordDto source)
    {
        List<OperationStageDto> result = stages.ToList();
        if (!string.Equals(source.Type, "install", StringComparison.Ordinal) ||
            source.InstallIntent?.FinishOnboarding != true)
        {
            return result;
        }

        void AddIfMissing(OperationStageDto stage)
        {
            if (!result.Any(existing => string.Equals(existing.Id, stage.Id, StringComparison.Ordinal)))
                result.Add(stage);
        }

        AddIfMissing(new OperationStageDto(
            "enable-scheduler",
            "Enable automatic refresh",
            "pending",
            null,
            null,
            "Waiting for reconciled app activation."));
        AddIfMissing(new OperationStageDto(
            "compute-next-evaluation",
            "Schedule next check",
            "pending",
            null,
            null,
            "Waiting for automatic refresh."));
        AddIfMissing(new OperationStageDto(
            "write-completion-receipt",
            "Finish setup",
            "pending",
            null,
            null,
            "Waiting for final reconciliation evidence."));
        return result;
    }

    private static bool ReconciliationTargetMatchesSource(
        OperationRecordDto reconciliation,
        OperationRecordDto source) =>
        string.Equals(reconciliation.ParentOperationId, source.OperationId, StringComparison.Ordinal) &&
        string.Equals(reconciliation.Target.DeviceUdid, source.Target.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(reconciliation.Target.BundleId, source.Target.BundleId, StringComparison.Ordinal) &&
        string.Equals(reconciliation.Target.TeamId, source.Target.TeamId, StringComparison.Ordinal) &&
        string.Equals(reconciliation.Target.CatalogAppId, source.Target.CatalogAppId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(reconciliation.Target.AccountProfileId, source.Target.AccountProfileId, StringComparison.Ordinal) &&
        reconciliation.Target.CatalogVersion == source.Target.CatalogVersion &&
        string.Equals(reconciliation.Target.Version, source.Target.Version, StringComparison.Ordinal) &&
        string.Equals(reconciliation.Target.CatalogSha256, source.Target.CatalogSha256, StringComparison.OrdinalIgnoreCase);

    private static bool ReconciliationRegistrationMatches(
        OperationRecordDto reconciliation,
        OperationRecordDto source,
        AppRegistration registration)
    {
        bool common =
            string.Equals(registration.DeviceUdid, reconciliation.Target.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(registration.BundleId, reconciliation.Target.BundleId, StringComparison.Ordinal) &&
            string.Equals(registration.TeamId, reconciliation.Target.TeamId, StringComparison.Ordinal) &&
            string.Equals(registration.CatalogAppId, reconciliation.Target.CatalogAppId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                AppleAccountIdentity.ProfileIdFor(registration.AppleId),
                reconciliation.Target.AccountProfileId,
                StringComparison.Ordinal) &&
            (reconciliation.Target.CatalogVersion is null ||
             registration.CatalogVersion == reconciliation.Target.CatalogVersion);
        if (!common)
            return false;

        if (string.Equals(source.Type, "refresh", StringComparison.Ordinal))
        {
            return !registration.IsPendingInstall &&
                (string.IsNullOrWhiteSpace(registration.CatalogSha256) ||
                 string.Equals(registration.CatalogSha256, reconciliation.Target.CatalogSha256, StringComparison.OrdinalIgnoreCase));
        }

        InstallOperationIntentDto? intent = source.InstallIntent;
        return intent is not null &&
            string.Equals(intent.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(intent.BundleId, registration.BundleId, StringComparison.Ordinal) &&
            string.Equals(intent.CatalogAppId, registration.CatalogAppId, StringComparison.OrdinalIgnoreCase) &&
            intent.CatalogVersion == registration.CatalogVersion &&
            string.Equals(intent.CatalogSha256, registration.CatalogSha256, StringComparison.OrdinalIgnoreCase) &&
            (registration.IsPendingInstall ||
             string.Equals(registration.LastVerifiedOperationId, reconciliation.OperationId, StringComparison.Ordinal));
    }

    private static async Task<bool> ReconciliationArtifactHashMatchesAsync(
        OperationTargetDto target,
        AppRegistration registration,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(target.CatalogSha256))
            return false;
        if (!string.IsNullOrWhiteSpace(registration.CatalogSha256) &&
            !string.Equals(registration.CatalogSha256, target.CatalogSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            string actual = await ComputeFileSha256Async(registration.InputIpaPath, ct).ConfigureAwait(false);
            return string.Equals(actual, target.CatalogSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<bool> EnsureExecutionAuthorizedAsync(
        OperationRecordDto record,
        string stageId,
        CancellationToken ct) =>
        (await EnsureExecutionAuthorizationDecisionAsync(record, stageId, ct).ConfigureAwait(false))
            is not { IsAllowed: false };

    private async Task<WorkspaceExecutionDecision?> EnsureExecutionAuthorizationDecisionAsync(
        OperationRecordDto record,
        string stageId,
        CancellationToken ct)
    {
        if (executionAuthorization is null)
            return null;

        WorkspaceExecutionDecision authorization = await executionAuthorization
            .AuthorizeOperationAsync(record, ct: ct)
            .ConfigureAwait(false);
        if (authorization.IsAllowed)
            return authorization;

        DateTimeOffset stoppedAt = DateTimeOffset.UtcNow;
        var issue = new OperationIssueDto(
            authorization.ErrorCode ?? "operation-access-revoked",
            authorization.Message ?? "Sideport access changed after this operation was submitted.");
        string status = authorization.Retryable ? "failed" : "blocked";
        await store.TransitionAsync(record.OperationId, existing =>
        {
            if (existing.Status is not ("queued" or "waiting" or "running"))
                return null;

            bool markedStage = false;
            OperationStageDto[] stages = existing.Stages.Select(stage =>
            {
                bool match = !markedStage &&
                    (string.Equals(stage.Id, stageId, StringComparison.Ordinal) ||
                     (!existing.Stages.Any(item => string.Equals(item.Id, stageId, StringComparison.Ordinal)) &&
                      stage.Status is "running" or "waiting" or "pending"));
                if (!match)
                    return stage;
                markedStage = true;
                return stage with
                {
                    Status = status,
                    StartedAt = stage.StartedAt ?? stoppedAt,
                    CompletedAt = stoppedAt,
                    Message = issue.Message,
                    Error = issue,
                };
            }).ToArray();

            return existing with
            {
                Status = status,
                UpdatedAt = stoppedAt,
                CompletedAt = stoppedAt,
                Error = issue,
                Cancelable = false,
                Retryable = authorization.Retryable,
                Rerunnable = false,
                Stages = stages,
            };
        }, ct).ConfigureAwait(false);
        return authorization;
    }

    private async Task FailInstallAsync(
        string operationId,
        string code,
        string message,
        CancellationToken ct)
    {
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;
        var issue = new OperationIssueDto(code, message);
        await store.TransitionAsync(operationId, existing => existing with
        {
            Status = "failed",
            UpdatedAt = failedAt,
            CompletedAt = failedAt,
            Result = new OperationResultDto(false, existing.Target.BundleId, null, message),
            Error = issue,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
            Stages = existing.Stages.Select(stage => stage.Status is "running" or "pending"
                ? stage with
                {
                    Status = string.Equals(stage.Status, "running", StringComparison.Ordinal) ? "failed" : stage.Status,
                    CompletedAt = string.Equals(stage.Status, "running", StringComparison.Ordinal) ? failedAt : stage.CompletedAt,
                    Message = string.Equals(stage.Status, "running", StringComparison.Ordinal) ? message : stage.Message,
                    Error = string.Equals(stage.Status, "running", StringComparison.Ordinal) ? issue : stage.Error,
                }
                : stage).ToArray(),
        }, ct).ConfigureAwait(false);
    }

    private async Task MarkInstallUnknownAsync(
        string operationId,
        string message,
        DateTimeOffset? signingExpiry,
        CancellationToken ct)
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var issue = new OperationIssueDto("install-outcome-unknown", message);
        await store.TransitionAsync(operationId, existing => existing with
        {
            Status = "unknown",
            UpdatedAt = observedAt,
            CompletedAt = null,
            Result = new OperationResultDto(false, existing.Target.BundleId, signingExpiry, message),
            Error = issue,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
            Stages = existing.Stages.Select(stage => stage.Status is "running" or "pending"
                ? stage with
                {
                    Status = string.Equals(stage.Status, "running", StringComparison.Ordinal) ? "unknown" : stage.Status,
                    CompletedAt = null,
                    Message = string.Equals(stage.Status, "running", StringComparison.Ordinal) ? message : stage.Message,
                    Error = string.Equals(stage.Status, "running", StringComparison.Ordinal) ? issue : stage.Error,
                }
                : stage).ToArray(),
        }, ct).ConfigureAwait(false);
    }

    private async Task FinalizeVerifiedInstallAsync(OperationRecordDto record, CancellationToken ct)
    {
        InstallOperationIntentDto intent = record.InstallIntent
            ?? throw new InvalidOperationException("Verified install evidence has no durable intent.");
        if (!HasVerifiedInstallEvidence(record))
            throw new InvalidOperationException("The install has no durable device-verification evidence.");
        if (record.Result!.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await BlockInstallFinalizationAsync(
                record.OperationId,
                "onboarding-verified-profile-expired",
                "The verified signing profile expired before Sideport could finish setup; review and install the app again.",
                ct).ConfigureAwait(false);
            return;
        }

        OnboardingCompletionReceipt? existingReceipt = intent.FinishOnboarding
            ? await onboardingCompletion.ReadAsync(ct).ConfigureAwait(false)
            : null;
        if (existingReceipt is not null &&
            string.Equals(existingReceipt.VerifiedOperationId, record.OperationId, StringComparison.Ordinal))
        {
            await CompleteInstallFromExistingReceiptAsync(record.OperationId, existingReceipt, ct).ConfigureAwait(false);
            return;
        }

        AppRegistration? registration = await registry.FindAsync(intent.DeviceUdid, intent.BundleId, ct).ConfigureAwait(false);
        bool activeForThisOperation = registration is not null &&
            !registration.IsPendingInstall &&
            string.Equals(registration.LastVerifiedOperationId, record.OperationId, StringComparison.Ordinal);
        if (registration is null ||
            (!registration.IsPendingInstall && !activeForThisOperation) ||
            !string.Equals(registration.CatalogAppId, intent.CatalogAppId, StringComparison.OrdinalIgnoreCase) ||
            intent.CatalogVersion is null ||
            string.IsNullOrWhiteSpace(intent.CatalogSha256) ||
            registration.CatalogVersion != intent.CatalogVersion ||
            record.Target.CatalogVersion != intent.CatalogVersion ||
            !string.Equals(registration.CatalogSha256, intent.CatalogSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(registration.DeviceUdid, intent.DeviceUdid, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(registration.BundleId, intent.BundleId, StringComparison.Ordinal))
        {
            await FailInstallAsync(
                record.OperationId,
                "verified-registration-missing",
                "The verified app registration no longer matches this install.",
                ct).ConfigureAwait(false);
            return;
        }

        SystemStatusDto? finalOperationalStatus = null;
        if (intent.FinishOnboarding)
        {
            var validation = await ValidateOnboardingFinalizationAsync(record, registration, ct)
                .ConfigureAwait(false);
            finalOperationalStatus = validation.Status;
            if (validation.Error is not null)
            {
                if (validation.Error is "onboarding-apple-lineage-mismatch" or
                    "onboarding-artifact-lineage-unavailable")
                {
                    await BlockInstallFinalizationAsync(
                        record.OperationId,
                        validation.Error,
                        validation.Message!,
                        ct).ConfigureAwait(false);
                }
                else
                {
                    await MarkInstallFinalizationPendingAsync(
                        record.OperationId,
                        validation.Error,
                        validation.Message!,
                        ct).ConfigureAwait(false);
                }
                return;
            }
        }

        DateTimeOffset activatedAt = registration.ActivatedAt
            ?? record.Stages.FirstOrDefault(stage => string.Equals(stage.Id, "verify", StringComparison.Ordinal))?.CompletedAt
            ?? DateTimeOffset.UtcNow;
        AppRegistration activated = activeForThisOperation
            ? registration
            : registration with
            {
                Lifecycle = "active",
                ActivatedAt = activatedAt,
                LastVerifiedOperationId = record.OperationId,
            };
        if (!activeForThisOperation)
            await registry.UpsertAsync(activated, ct).ConfigureAwait(false);

        DateTimeOffset activationSavedAt = DateTimeOffset.UtcNow;
        record = (await store.TransitionAsync(record.OperationId, existing => existing with
        {
            Status = "running",
            UpdatedAt = activationSavedAt,
            CompletedAt = null,
            Error = null,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
            Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "activate-registration", StringComparison.Ordinal)
                ? stage with
                {
                    Status = "succeeded",
                    StartedAt = stage.StartedAt ?? activationSavedAt,
                    CompletedAt = stage.CompletedAt ?? activationSavedAt,
                    Message = "The device-verified app registration is active.",
                    Error = null,
                }
                : intent.FinishOnboarding && string.Equals(stage.Id, "enable-scheduler", StringComparison.Ordinal)
                    ? stage with
                    {
                        Status = "running",
                        StartedAt = stage.StartedAt ?? activationSavedAt,
                        Message = "Enabling automatic hourly due-only refresh.",
                        Error = null,
                    }
                : stage).ToArray(),
        }, ct).ConfigureAwait(false))!;

        if (intent.FinishOnboarding)
        {
            (SchedulerSettingsState enabledScheduler, _) = await schedulerSettings
                .SetEnabledAsync(true, ct)
                .ConfigureAwait(false);
            string settingsVersion = $"settings_{enabledScheduler.SettingsVersion}";
            DateTimeOffset schedulerEnabledAt = DateTimeOffset.UtcNow;
            record = (await store.TransitionAsync(record.OperationId, existing => existing with
            {
                UpdatedAt = schedulerEnabledAt,
                Result = existing.Result is null
                    ? existing.Result
                    : existing.Result with { SchedulerSettingsVersion = settingsVersion },
                Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "enable-scheduler", StringComparison.Ordinal)
                    ? stage with
                    {
                        Status = "succeeded",
                        StartedAt = stage.StartedAt ?? schedulerEnabledAt,
                        CompletedAt = stage.CompletedAt ?? schedulerEnabledAt,
                        Message = "Automatic hourly due-only refresh is enabled.",
                        Error = null,
                    }
                    : string.Equals(stage.Id, "compute-next-evaluation", StringComparison.Ordinal)
                        ? stage with
                        {
                            Status = "running",
                            StartedAt = stage.StartedAt ?? schedulerEnabledAt,
                            Message = "Saving the next automatic refresh check.",
                            Error = null,
                        }
                        : stage).ToArray(),
            }, ct).ConfigureAwait(false))!;

            DateTimeOffset nextEvaluationAt = record.Result?.NextEvaluationAt
                ?? DateTimeOffset.UtcNow.Add(orchestratorOptions.ScheduleInterval);
            record = (await store.TransitionAsync(record.OperationId, existing => existing with
            {
                UpdatedAt = DateTimeOffset.UtcNow,
                Result = existing.Result is null
                    ? existing.Result
                    : existing.Result with
                    {
                        NextEvaluationAt = nextEvaluationAt,
                        SchedulerSettingsVersion = settingsVersion,
                    },
            }, ct).ConfigureAwait(false))!;
            _ = await schedulerSettings.SetNextEvaluationAtAsync(nextEvaluationAt, ct).ConfigureAwait(false);

            DateTimeOffset evaluationSavedAt = DateTimeOffset.UtcNow;
            record = (await store.TransitionAsync(record.OperationId, existing => existing with
            {
                UpdatedAt = evaluationSavedAt,
                Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "compute-next-evaluation", StringComparison.Ordinal)
                    ? stage with
                    {
                        Status = "succeeded",
                        StartedAt = stage.StartedAt ?? evaluationSavedAt,
                        CompletedAt = stage.CompletedAt ?? evaluationSavedAt,
                        Message = "The next automatic refresh check is saved.",
                        Error = null,
                    }
                    : string.Equals(stage.Id, "write-completion-receipt", StringComparison.Ordinal)
                        ? stage with
                        {
                            Status = "running",
                            StartedAt = stage.StartedAt ?? evaluationSavedAt,
                            Message = "Checking final setup evidence.",
                            Error = null,
                        }
                        : stage).ToArray(),
            }, ct).ConfigureAwait(false))!;

            DateTimeOffset receiptWrittenAt = DateTimeOffset.UtcNow;
            await onboardingCompletion.CreateAsync(new OnboardingCompletionReceipt(
                OnboardingCompletionStore.CurrentSchemaVersion,
                receiptWrittenAt,
                record.Actor,
                intent.AccountProfileId,
                activated.TeamId,
                intent.DeviceUdid,
                intent.CatalogAppId,
                intent.CatalogVersion!.Value,
                intent.CatalogSha256!,
                intent.BundleId,
                record.OperationId,
                settingsVersion,
                finalOperationalStatus!.CheckedAt), ct).ConfigureAwait(false);

            record = (await store.TransitionAsync(record.OperationId, existing => existing with
            {
                UpdatedAt = receiptWrittenAt,
                Stages = existing.Stages.Select(stage => string.Equals(stage.Id, "write-completion-receipt", StringComparison.Ordinal)
                    ? stage with
                    {
                        Status = "succeeded",
                        StartedAt = stage.StartedAt ?? receiptWrittenAt,
                        CompletedAt = stage.CompletedAt ?? receiptWrittenAt,
                        Message = "Verified setup completion is saved.",
                        Error = null,
                    }
                    : stage).ToArray(),
            }, ct).ConfigureAwait(false))!;
        }

        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        await store.TransitionAsync(record.OperationId, existing => existing with
        {
            Status = "succeeded",
            UpdatedAt = completedAt,
            CompletedAt = completedAt,
            Error = null,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
        }, ct).ConfigureAwait(false);
    }

    private async Task<(SystemStatusDto? Status, string? Error, string? Message)>
        ValidateOnboardingFinalizationAsync(
            OperationRecordDto record,
            AppRegistration registration,
            CancellationToken ct)
    {
        InstallOperationIntentDto intent = record.InstallIntent!;
        SystemStatusDto operational = await systemStatus.GetAsync(ct).ConfigureAwait(false);
        if (!operational.Operational)
        {
            return (
                operational,
                "onboarding-operational-check-failed",
                "The app is verified, but a current Sideport system check must recover before setup can finish.");
        }

        PersonalAppleInstallContext apple;
        try
        {
            apple = await personalApple.Value
                .ResolveFreshInstallContextAsync(intent.AccountProfileId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AppleAccountProfileNotFoundException or
                                   AppleTeamSelectionStaleException or
                                   AppleTeamNotReturnedException)
        {
            return (
                operational,
                "onboarding-apple-context-stale",
                "Sign in to Apple again and confirm the selected team before finishing setup.");
        }

        if (!string.Equals(apple.AppleId, registration.AppleId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(apple.TeamId, registration.TeamId, StringComparison.Ordinal) ||
            !string.Equals(record.Target.TeamId, registration.TeamId, StringComparison.Ordinal))
        {
            return (
                operational,
                "onboarding-apple-lineage-mismatch",
                "The verified install no longer matches the selected Apple account and team.");
        }

        SigningIdentityInspection identity;
        try
        {
            identity = await signingIdentity
                .InspectAsync(registration.AppleId, registration.TeamId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return (
                operational,
                "onboarding-signing-identity-unavailable",
                "Sideport could not verify the persisted signing identity needed for automatic refresh.");
        }
        if (!string.Equals(identity.State, "reusable", StringComparison.Ordinal) ||
            identity.ExpiresAt is { } identityExpiry && identityExpiry <= DateTimeOffset.UtcNow)
        {
            return (
                operational,
                "onboarding-signing-identity-unavailable",
                "A reusable persisted signing identity is required before setup can finish.");
        }

        (string? deviceError, _) = await ValidateInstallDeviceAsync(intent.DeviceUdid, ct).ConfigureAwait(false);
        if (deviceError is not null)
        {
            return (
                operational,
                "onboarding-device-verification-stale",
                "Reconnect the accepted iPhone over USB, unlock it, and retry finishing setup.");
        }

        if (intent.CatalogVersion is null ||
            string.IsNullOrWhiteSpace(intent.CatalogSha256) ||
            registration.CatalogVersion != intent.CatalogVersion ||
            record.Target.CatalogVersion != intent.CatalogVersion ||
            !string.Equals(registration.CatalogSha256, intent.CatalogSha256, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(registration.InputIpaPath))
        {
            return (
                operational,
                "onboarding-artifact-lineage-unavailable",
                "The verified app no longer has its durable Sideport artifact lineage.");
        }

        try
        {
            IpaInfo inspected = IpaInspector.Inspect(registration.InputIpaPath);
            string durableSha256 = await ComputeFileSha256Async(registration.InputIpaPath, ct).ConfigureAwait(false);
            if (!string.Equals(inspected.BundleIdentifier, intent.BundleId, StringComparison.Ordinal) ||
                !string.Equals(durableSha256, intent.CatalogSha256, StringComparison.OrdinalIgnoreCase))
            {
                return (
                    operational,
                    "onboarding-artifact-lineage-unavailable",
                    "The verified app artifact changed after device verification.");
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return (
                operational,
                "onboarding-artifact-lineage-unavailable",
                "The verified app artifact could not be revalidated.");
        }

        return (operational, null, null);
    }

    private async Task CompleteInstallFromExistingReceiptAsync(
        string operationId,
        OnboardingCompletionReceipt receipt,
        CancellationToken ct)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        await store.TransitionAsync(operationId, existing => existing with
        {
            Status = "succeeded",
            UpdatedAt = completedAt,
            CompletedAt = completedAt,
            Error = null,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
            Result = existing.Result is null
                ? null
                : existing.Result with { SchedulerSettingsVersion = receipt.SchedulerSettingsVersion },
            Stages = existing.Stages.Select(stage => stage.Id is
                    "activate-registration" or
                    "enable-scheduler" or
                    "compute-next-evaluation" or
                    "write-completion-receipt"
                ? stage with
                {
                    Status = "succeeded",
                    StartedAt = stage.StartedAt ?? receipt.CompletedAt,
                    CompletedAt = stage.CompletedAt ?? receipt.CompletedAt,
                    Message = stage.Id == "write-completion-receipt"
                        ? "Verified setup completion is saved."
                        : stage.Message,
                    Error = null,
                }
                : stage).ToArray(),
        }, ct).ConfigureAwait(false);
    }

    private async Task MarkInstallFinalizationPendingAsync(
        string operationId,
        string code,
        string message,
        CancellationToken ct)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var issue = new OperationIssueDto(code, message);
        await store.TransitionAsync(operationId, existing => existing with
        {
            Status = "waiting",
            UpdatedAt = now,
            CompletedAt = null,
            Error = issue,
            Cancelable = false,
            Retryable = true,
            Rerunnable = false,
        }, ct).ConfigureAwait(false);
    }

    private async Task BlockInstallFinalizationAsync(
        string operationId,
        string code,
        string message,
        CancellationToken ct)
    {
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        var issue = new OperationIssueDto(code, message);
        await store.TransitionAsync(operationId, existing => existing with
        {
            Status = "blocked",
            UpdatedAt = completedAt,
            CompletedAt = completedAt,
            Error = issue,
            Cancelable = false,
            Retryable = false,
            Rerunnable = false,
            Stages = existing.Stages.Select(stage =>
                stage.Status is "running" or "waiting"
                    ? stage with
                    {
                        Status = "blocked",
                        CompletedAt = completedAt,
                        Message = message,
                        Error = issue,
                    }
                    : stage).ToArray(),
        }, ct).ConfigureAwait(false);
    }

    private static InstallSubmissionResult InstallRejected(string code, string message) =>
        new(null, Created: false, code, message);

    private static InstallSubmissionResult InstallPreflightStale(
        OperationPreflightDto replacement,
        string message) =>
        new(null, Created: false, "install-preflight-stale", message, replacement);

    private static bool InstallIntentMatches(
        InstallOperationIntentDto? intent,
        string deviceUdid,
        string bundleId,
        string? catalogAppId,
        string? accountProfileId,
        bool finishOnboarding) =>
        intent is not null &&
        string.Equals(intent.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(intent.BundleId, bundleId, StringComparison.Ordinal) &&
        (catalogAppId is null ||
         string.Equals(intent.CatalogAppId, catalogAppId, StringComparison.OrdinalIgnoreCase)) &&
        (accountProfileId is null ||
         string.Equals(intent.AccountProfileId, accountProfileId, StringComparison.Ordinal)) &&
        intent.FinishOnboarding == finishOnboarding;

    private static bool HasVerifiedInstallEvidence(OperationRecordDto record) =>
        string.Equals(record.Type, "install", StringComparison.Ordinal) &&
        record.InstallIntent is { } intent &&
        record.Result is { Success: true, ExpiresAt: not null, Version.Length: > 0 } result &&
        record.Stages.Any(stage =>
            string.Equals(stage.Id, "install", StringComparison.Ordinal) &&
            string.Equals(stage.Status, "succeeded", StringComparison.Ordinal)) &&
        record.Stages.Any(stage =>
            string.Equals(stage.Id, "verify", StringComparison.Ordinal) &&
            string.Equals(stage.Status, "succeeded", StringComparison.Ordinal)) &&
        string.Equals(result.BundleId, intent.BundleId, StringComparison.Ordinal) &&
        string.Equals(record.Target.BundleId, intent.BundleId, StringComparison.Ordinal) &&
        string.Equals(record.Target.DeviceUdid, intent.DeviceUdid, StringComparison.OrdinalIgnoreCase);

    private static bool HasVerifiedExistingRegistrationEvidence(OperationRecordDto record) =>
        string.Equals(record.Type, "verify-existing-registration", StringComparison.Ordinal) &&
        record.Result is { Success: true, ExpiresAt: not null, Version.Length: > 0 } result &&
        record.Stages.Any(stage =>
            string.Equals(stage.Id, "verify", StringComparison.Ordinal) &&
            string.Equals(stage.Status, "succeeded", StringComparison.Ordinal) &&
            stage.CompletedAt is not null) &&
        string.Equals(result.BundleId, record.Target.BundleId, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(record.Target.DeviceUdid);

    private static bool HasVerifiedReconciliationEvidence(OperationRecordDto record) =>
        string.Equals(record.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(record.ParentOperationId) &&
        record.Result is
        {
            Success: true,
            ExpiresAt: not null,
            Version.Length: > 0,
            ReconciledOperationId.Length: > 0,
        } result &&
        record.Stages.Any(stage =>
            string.Equals(stage.Id, "verify", StringComparison.Ordinal) &&
            string.Equals(stage.Status, "succeeded", StringComparison.Ordinal) &&
            stage.CompletedAt is not null) &&
        string.Equals(result.BundleId, record.Target.BundleId, StringComparison.Ordinal) &&
        string.Equals(result.ReconciledOperationId, record.ParentOperationId, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(record.Target.DeviceUdid);

    private static bool ShouldEnqueueInstall(OperationRecordDto record) =>
        record.Status is "queued" or "waiting" ||
        (string.Equals(record.Status, "running", StringComparison.Ordinal) && HasVerifiedInstallEvidence(record));

    private static string RequiredIntentValue(string? value, string field)
    {
        string result = value?.Trim() ?? string.Empty;
        if (result.Length == 0)
            throw new ArgumentException($"{field} is required.", field);
        if (result.Length > 512)
            throw new ArgumentException($"{field} must be 512 characters or fewer.", field);
        return result;
    }

    private static string? OptionalIntentValue(string? value)
    {
        string? result = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (result?.Length > 512)
            throw new ArgumentException("Optional install values must be 512 characters or fewer.");
        return result;
    }

    private static string? NormalizeOwnershipId(string? value)
    {
        string? result = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (result?.Length > 512)
            throw new ArgumentException("Member IDs must be 512 characters or fewer.");
        return result;
    }

    private static string Digest(object value)
    {
        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        byte[] hash = SHA256.HashData(serialized);
        return $"sha256:{Convert.ToHexStringLower(hash)}";
    }

    private async Task<bool> CurrentCatalogApprovalMatchesAsync(
        InstallOperationIntentDto intent,
        CancellationToken ct)
    {
        if (intent.CatalogVersion is null || string.IsNullOrWhiteSpace(intent.CatalogSha256))
            return false;
        CatalogAppV2Dto? approved = (await catalog.ListV2Async(ct).ConfigureAwait(false))
            .FirstOrDefault(app => string.Equals(
                app.Id,
                intent.CatalogAppId,
                StringComparison.OrdinalIgnoreCase));
        return approved is not null &&
            string.Equals(approved.Status, "ready", StringComparison.Ordinal) &&
            string.Equals(approved.BundleId, intent.BundleId, StringComparison.Ordinal) &&
            approved.CatalogVersion == intent.CatalogVersion &&
            !string.IsNullOrWhiteSpace(approved.Sha256) &&
            string.Equals(approved.Sha256, intent.CatalogSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken ct)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private void PruneExpiredInstallPreflights(DateTimeOffset now)
    {
        foreach ((string id, InstallPreflightAuthorization authorization) in _installPreflights)
        {
            if (authorization.Preflight.ExpiresAt is not { } expiresAt || expiresAt <= now)
                _installPreflights.TryRemove(id, out _);
        }
    }

    private static string NewOperationId(DateTimeOffset now) => $"op_{now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..31];

    private sealed record InstallPreflightBuild(
        OperationPreflightDto Preflight,
        CatalogAppDto? CatalogApp,
        PersonalAppleInstallContext? Apple,
        AppRegistration? ExistingRegistration,
        IReadOnlyList<AppRegistration> Registrations);

    private sealed record InstallPreflightAuthorization(
        OperationPreflightDto Preflight,
        string DeviceUdid,
        string BundleId,
        string? CatalogAppId,
        string? AccountProfileId,
        bool FinishOnboarding,
        bool AllowOwnerManagedAppleAuthority,
        string? ConsumedByIdempotencyKey);
}
