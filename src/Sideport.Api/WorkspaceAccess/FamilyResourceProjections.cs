using Sideport.Api.Catalog;
using Sideport.Api.DeviceInventory;
using Sideport.Api.DiagnosticsIssues;
using Sideport.Api.Operations;
using Sideport.Orchestrator;

namespace Sideport.Api.WorkspaceAccess;

internal sealed record FamilyDeviceDto(
    string DeviceUdid,
    string DisplayName,
    string? ProductType,
    string? OsVersion,
    string Connection,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastSeenAt,
    string InventoryState,
    DateTimeOffset? AcceptedAt,
    string TrustState,
    bool UsableForInstall,
    bool SupportedForFirstInstall,
    KnownDeviceHealthDto Health,
    KnownDeviceAppSlotsDto AppSlots,
    string Source);

internal sealed record FamilyCatalogAppDto(
    string Id,
    int CatalogVersion,
    string Name,
    string Purpose,
    string BundleId,
    string? Version,
    string? ShortVersion,
    string Status,
    long? SizeBytes,
    string Source,
    string? Icon);

internal sealed record FamilyRegistrationRefreshDto(
    string State,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSucceededAt);

internal sealed record FamilyRegistrationDto(
    string DeviceUdid,
    string BundleId,
    string? CatalogAppId,
    string AppName,
    string? AppVersion,
    string Lifecycle,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ActivatedAt,
    FamilyRegistrationRefreshDto Refresh,
    string Source = "live");

internal sealed record FamilyOperationTargetDto(
    string? DeviceUdid,
    string? BundleId,
    string? Kind,
    string? CatalogAppId,
    string? Version);

internal sealed record FamilyOperationIssueDto(string Code, string Message);

internal sealed record FamilyOperationLimitDto(
    string Code,
    string Label,
    int Used,
    int Limit,
    string Source);

internal sealed record FamilyOperationPreflightDto(
    bool Ready,
    string? PreflightId,
    string? PlanVersion,
    DateTimeOffset? ExpiresAt,
    FamilyOperationTargetDto Target,
    IReadOnlyList<FamilyOperationIssueDto> Blockers,
    IReadOnlyList<FamilyOperationIssueDto> Warnings,
    IReadOnlyList<string> PlannedEffects,
    IReadOnlyList<FamilyOperationLimitDto> Limits,
    bool RequiresConfirmation,
    bool OwnerActionRequired,
    string Source);

internal sealed record FamilyOperationStageDto(
    string Id,
    string Label,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

internal sealed record FamilyDeviceEnrollmentResultDto(
    string InventoryState,
    DateTimeOffset? AcceptedAt,
    string? Reason);

internal sealed record FamilyOperationResultDto(
    bool Success,
    string? BundleId,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? NextRefreshAt,
    string? Version,
    FamilyDeviceEnrollmentResultDto? DeviceEnrollment);

internal sealed record FamilyEnrollmentCandidateDto(
    string UdidSuffix,
    string Name,
    string? ProductType,
    string? OsVersion,
    string Connection);

internal sealed record FamilyOperationDto(
    string OperationId,
    string Type,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    int Attempt,
    FamilyOperationTargetDto Target,
    IReadOnlyList<FamilyOperationStageDto> Stages,
    FamilyOperationResultDto? Result,
    FamilyOperationIssueDto? Error,
    bool Cancelable,
    bool Retryable,
    bool Rerunnable,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<FamilyEnrollmentCandidateDto> CandidateDevices,
    string Source);

internal sealed record FamilyRenewalDto(
    string Id,
    string DeviceUdid,
    string BundleId,
    string? CatalogAppId,
    string AppName,
    string? AppVersion,
    string Risk,
    string Status,
    DateTimeOffset? ExpiresAt,
    FamilyOperationIssueDto? Blocker,
    string RefreshState,
    string Source);

internal sealed record FamilyDiagnosticAffectedDto(
    string? DeviceUdid,
    string? BundleId,
    string? CatalogAppId);

internal sealed record FamilyDiagnosticIssueDto(
    string IssueId,
    string Category,
    string Severity,
    string Status,
    FamilyDiagnosticAffectedDto Affected,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int OccurrenceCount,
    string Remediation,
    string Source);

internal static class FamilyResourceProjections
{
    private static readonly IReadOnlyDictionary<string, string> SafeIssueMessages =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["anisette-headers"] = "Sideport needs the home Owner to restore its Apple connection.",
            ["apple-account-profile-not-found"] = "Sideport needs the home Owner to reconnect the Apple account.",
            ["apple-authentication-stale"] = "Sideport needs the home Owner to reconnect the Apple account.",
            ["apple-credential-missing"] = "Sideport needs the home Owner to connect an Apple account.",
            ["apple-install-context-stale"] = "Sideport needs the home Owner to reconnect the Apple account.",
            ["apple-refresh-context-unavailable"] = "Sideport needs the home Owner to restore the Apple connection.",
            ["apple-refresh-lineage-mismatch"] = "Sideport needs the home Owner to review this app's signing setup.",
            ["apple-session-will-renew"] = "Sideport will renew its Apple connection before updating the app.",
            ["apple-team-required"] = "Sideport needs the home Owner to choose an Apple team.",
            ["bundle-mismatch"] = "The approved app no longer matches this installation.",
            ["catalog-app-not-found"] = "This approved app is no longer available.",
            ["catalog-app-not-ready"] = "This app is not ready to install yet.",
            ["catalog-app-selection-required"] = "Choose an approved app before continuing.",
            ["catalog-bundle-mismatch"] = "The approved app no longer matches this installation.",
            ["catalog-integrity-mismatch"] = "The home Owner needs to inspect this app again.",
            ["device-app-slot-limit"] = "This iPhone has reached its free Apple app limit. Ask the home Owner for help.",
            ["device-enrollment-disconnected"] = "Reconnect the iPhone with the cable and keep it unlocked.",
            ["device-enrollment-timeout"] = "Sideport did not find a ready iPhone in time. Reconnect it and try again.",
            ["device-locked"] = "Unlock the iPhone before continuing.",
            ["device-lockdown-untrusted"] = "Unlock the iPhone and tap Trust when it asks.",
            ["device-not-accepted"] = "Add this iPhone to Sideport before installing an app.",
            ["device-not-reachable"] = "Connect the iPhone or make sure it is on the home Wi-Fi.",
            ["device-not-trusted"] = "Unlock the iPhone and tap Trust when it asks.",
            ["device-operation-active"] = "Sideport is already working with this iPhone. Wait for it to finish.",
            ["device-operation-still-active"] = "The home Owner needs to review an earlier iPhone operation.",
            ["device-selection-required"] = "Choose the iPhone connected to Sideport.",
            ["device-trust-check-unavailable"] = "Sideport could not check the iPhone connection. Reconnect it and try again.",
            ["device-usb-required"] = "Connect the iPhone with the cable for this step.",
            ["idempotency-target-conflict"] = "This request was already used for a different action. Try again.",
            ["install-already-active"] = "Sideport is already installing this app.",
            ["install-confirmation-required"] = "Review the install summary before continuing.",
            ["install-failed"] = "Sideport could not install the app. Try again or ask the home Owner for help.",
            ["install-outcome-unknown"] = "The home Owner needs to check whether the app finished installing.",
            ["install-preflight-blocked"] = "Sideport is not ready to install this app yet.",
            ["install-preflight-required"] = "Check the install plan again before continuing.",
            ["install-preflight-stale"] = "Something changed. Review the updated install plan.",
            ["install-verification-failed"] = "Sideport could not verify the app on the iPhone.",
            ["install-verification-unavailable"] = "Keep the iPhone connected while Sideport checks the app.",
            ["ipa-inspection-failed"] = "The home Owner needs to inspect this app again.",
            ["ipa-missing"] = "The home Owner needs to restore this approved app.",
            ["operation-canceled"] = "The action was canceled before it changed the iPhone.",
            ["operation-not-cancelable"] = "This action has already started and cannot be canceled now.",
            ["operation-not-reconcilable"] = "The home Owner needs to review this action.",
            ["operation-not-rerunnable"] = "This action cannot be run again yet.",
            ["operation-not-retryable"] = "This action cannot be retried yet.",
            ["operation-reconciliation-evidence-missing"] = "The home Owner needs to check the iPhone before continuing.",
            ["owner-action-required"] = "Sideport needs the home Owner to review this action.",
            ["pending-registration-conflict"] = "A different app choice is already waiting for this iPhone.",
            ["pending-registration-missing"] = "Choose the approved app again before installing it.",
            ["refresh-failed"] = "Sideport could not update the app. Connect the cable and try again.",
            ["refresh-preflight-blocked"] = "Sideport is not ready to update this app yet.",
            ["registration-already-active"] = "This app is already installed through Sideport.",
            ["registration-artifact-lineage-changed"] = "The home Owner needs to inspect this app again.",
            ["registration-missing"] = "This app is not registered on the iPhone.",
            ["registration-verification-invalid"] = "The home Owner needs to verify this app again.",
            ["registration-verification-required"] = "Install and verify the app before updating it.",
            ["signing-cutover-required"] = "The home Owner needs to review the Apple signing setup.",
            ["signing-identity-unavailable"] = "The home Owner needs to restore Apple signing.",
            ["wifi-refresh-usb-fallback"] = "Sideport can try home Wi-Fi. Connect the cable if the update cannot finish.",
        };

    private static readonly HashSet<string> OwnerActionCodes = new(StringComparer.Ordinal)
    {
        "anisette-headers",
        "apple-account-profile-not-found",
        "apple-authentication-stale",
        "apple-credential-missing",
        "apple-install-context-stale",
        "apple-refresh-context-unavailable",
        "apple-refresh-lineage-mismatch",
        "apple-team-required",
        "catalog-app-not-ready",
        "catalog-integrity-mismatch",
        "device-app-slot-limit",
        "device-operation-still-active",
        "install-outcome-unknown",
        "ipa-inspection-failed",
        "ipa-missing",
        "operation-reconciliation-evidence-missing",
        "owner-action-required",
        "registration-artifact-lineage-changed",
        "registration-verification-invalid",
        "signing-cutover-required",
        "signing-identity-unavailable",
    };

    internal static FamilyDeviceDto Device(KnownDeviceDto device) => new(
        device.Udid,
        device.DisplayName,
        device.ProductType,
        device.OsVersion,
        device.Connection,
        device.FirstSeenAt,
        device.LastSeenAt,
        device.InventoryState,
        device.AcceptedAt,
        SafeTrustState(device.TrustState),
        device.UsableForInstall,
        device.SupportedForFirstInstall,
        device.Health,
        device.AppSlots,
        device.Source);

    internal static FamilyCatalogAppDto Catalog(CatalogAppV2Dto app) => new(
        app.Id,
        app.CatalogVersion,
        app.Name,
        app.Purpose,
        app.BundleId,
        app.Version,
        app.ShortVersion,
        app.Status,
        app.SizeBytes,
        app.Source,
        app.Icon);

    internal static FamilyRegistrationDto Registration(
        OwnedFamilyRegistration owned,
        RefreshState? refresh) =>
        new(
            owned.Registration.DeviceUdid,
            owned.Registration.BundleId,
            owned.CatalogApp?.Id ?? owned.Registration.CatalogAppId,
            owned.CatalogApp?.Name ?? owned.Registration.BundleId,
            owned.CatalogApp is null ? null : PreferredVersion(owned.CatalogApp),
            owned.Registration.Lifecycle,
            owned.Registration.CreatedAt,
            owned.Registration.ActivatedAt,
            new FamilyRegistrationRefreshDto(
                refresh is null
                    ? "not-run"
                    : refresh.LastSucceeded ? "succeeded" : "attention",
                refresh?.ExpiresAt,
                refresh?.LastAttemptUtc,
                refresh?.LastSucceededUtc));

    internal static FamilyOperationPreflightDto Preflight(OperationPreflightDto preflight)
    {
        FamilyOperationIssueDto[] blockers = preflight.Blockers.Select(Issue).ToArray();
        return new FamilyOperationPreflightDto(
            preflight.Ready,
            preflight.PreflightId,
            preflight.PlanVersion,
            preflight.ExpiresAt,
            Target(preflight.Target),
            blockers,
            preflight.Warnings.Select(Issue).ToArray(),
            PlannedEffects(preflight.Target.Kind),
            preflight.ScarceLimits.Select(limit => new FamilyOperationLimitDto(
                limit.Code,
                limit.Label,
                limit.Used,
                limit.Limit,
                limit.Source)).ToArray(),
            preflight.RequiresConfirmation,
            preflight.Blockers.Any(issue => OwnerActionCodes.Contains(issue.Code)),
            preflight.Source);
    }

    internal static FamilyOperationDto Operation(
        OperationRecordDto operation,
        bool actionsAllowed) => new(
        operation.OperationId,
        SafeOperationType(operation.Type),
        SafeStatus(operation.Status),
        operation.CreatedAt,
        operation.StartedAt,
        operation.UpdatedAt,
        operation.CompletedAt,
        operation.Attempt,
        Target(operation.Target),
        operation.Stages.Select(Stage).ToArray(),
        operation.Result is null ? null : Result(operation.Result),
        operation.Error is null ? null : Issue(operation.Error),
        actionsAllowed && operation.Cancelable,
        actionsAllowed && operation.Retryable,
        actionsAllowed && operation.Rerunnable,
        operation.ExpiresAt,
        operation.CandidateDevices?.Select(candidate => new FamilyEnrollmentCandidateDto(
            candidate.UdidSuffix,
            candidate.Name,
            candidate.ProductType,
            candidate.OsVersion,
            candidate.Connection)).ToArray() ?? [],
        operation.Source);

    internal static FamilyRenewalDto Renewal(
        RenewalItemDto renewal,
        OwnedFamilyRegistration owned,
        OperationRecordDto? latestOperation)
    {
        FamilyOperationIssueDto? blocker = latestOperation?.Error is null
            ? string.IsNullOrWhiteSpace(renewal.Blocker)
                ? null
                : new FamilyOperationIssueDto(
                    "refresh-attention-required",
                    "Sideport needs attention before it can update this app.")
            : Issue(latestOperation.Error);
        return new FamilyRenewalDto(
            renewal.Id,
            renewal.DeviceUdid,
            renewal.BundleId,
            owned.CatalogApp?.Id ?? owned.Registration.CatalogAppId,
            owned.CatalogApp?.Name ?? owned.Registration.BundleId,
            owned.CatalogApp is null ? null : PreferredVersion(owned.CatalogApp),
            SafeRisk(renewal.Risk),
            SafeStatus(renewal.Status),
            renewal.ExpiresAt,
            blocker,
            SafeStatus(renewal.Status),
            renewal.Source);
    }

    internal static FamilyDiagnosticIssueDto Diagnostic(
        DiagnosticIssueDto issue,
        OwnedFamilyRegistration? owned) =>
        new(
            issue.IssueId,
            SafeIssueCode(issue.Category),
            SafeSeverity(issue.Severity),
            SafeDiagnosticStatus(issue.Status),
            new FamilyDiagnosticAffectedDto(
                issue.Affected.DeviceUdid,
                issue.Affected.BundleId,
                owned?.CatalogApp?.Id ?? owned?.Registration.CatalogAppId),
            issue.FirstSeenAt,
            issue.LastSeenAt,
            issue.OccurrenceCount,
            SafeRemediation(issue.Category),
            issue.Source);

    internal static FamilyOperationIssueDto Issue(OperationIssueDto issue)
    {
        string code = SafeIssueCode(issue.Code);
        return new FamilyOperationIssueDto(
            code,
            SafeIssueMessages.GetValueOrDefault(
                code,
                "Sideport needs the home Owner to review this issue."));
    }

    private static FamilyOperationTargetDto Target(OperationTargetDto target) => new(
        target.DeviceUdid,
        target.BundleId,
        SafeTargetKind(target.Kind),
        target.CatalogAppId,
        target.Version);

    private static FamilyOperationStageDto Stage(OperationStageDto stage) => new(
        SafeStageId(stage.Id),
        SafeStageLabel(stage.Id),
        SafeStatus(stage.Status),
        stage.StartedAt,
        stage.CompletedAt);

    private static FamilyOperationResultDto Result(OperationResultDto result) => new(
        result.Success,
        result.BundleId,
        result.ExpiresAt,
        result.NextEvaluationAt,
        result.Version,
        result.DeviceEnrollment is null
            ? null
            : new FamilyDeviceEnrollmentResultDto(
                result.DeviceEnrollment.InventoryState,
                result.DeviceEnrollment.AcceptedAt,
                SafeEnrollmentReason(result.DeviceEnrollment.Reason)));

    private static IReadOnlyList<string> PlannedEffects(string? kind) =>
        string.Equals(kind, "device-enrollment", StringComparison.Ordinal)
            ? ["Find the connected iPhone", "Confirm Trust on the iPhone", "Add the iPhone to Sideport"]
            : ["Prepare the approved app", "Install it on your iPhone", "Verify the installation"];

    private static string SafeIssueCode(string? code) =>
        !string.IsNullOrWhiteSpace(code) && SafeIssueMessages.ContainsKey(code)
            ? code
            : "owner-review-required";

    private static string SafeRemediation(string? category) =>
        SafeIssueMessages.GetValueOrDefault(
            category ?? string.Empty,
            "Ask the home Owner to review this in Sideport.");

    private static string SafeStageId(string? id) => id switch
    {
        "wait-for-usb" or "request-pairing" or "await-user-trust" or "verify-lockdown" or
        "accept-device" or "preflight" or "install" or "refresh" or "verify" or
        "activate-registration" or "enable-scheduler" or "compute-next-evaluation" or
        "write-completion-receipt" => id,
        _ => "sideport-task",
    };

    private static string SafeStageLabel(string? id) => id switch
    {
        "wait-for-usb" => "Connect iPhone",
        "request-pairing" => "Prepare connection",
        "await-user-trust" => "Trust on iPhone",
        "verify-lockdown" => "Check connection",
        "accept-device" => "Add iPhone",
        "preflight" => "Check everything",
        "install" => "Install app",
        "refresh" => "Update app",
        "verify" => "Verify on iPhone",
        "activate-registration" => "Finish app setup",
        "enable-scheduler" => "Turn on automatic updates",
        "compute-next-evaluation" => "Schedule the next check",
        "write-completion-receipt" => "Finish setup",
        _ => "Sideport task",
    };

    private static string SafeOperationType(string? type) => type switch
    {
        "install" or "refresh" or "enroll-device" or "verify-existing-registration" or
        "reconcile" => type,
        _ => "sideport-task",
    };

    private static string? SafeTargetKind(string? kind) => kind switch
    {
        "catalog-app" or "app" or "device-enrollment" => kind,
        _ => null,
    };

    private static string SafeStatus(string? status) => status switch
    {
        "queued" or "waiting" or "running" or "succeeded" or "failed" or "blocked" or
        "canceled" or "canceling" or "unknown" or "recovery-required" or "idle" => status,
        _ => "unknown",
    };

    private static string SafeTrustState(string? state) => state switch
    {
        "trusted" or "untrusted" or "locked" or "error" => state,
        _ => "unknown",
    };

    private static string SafeRisk(string? risk) => risk switch
    {
        "healthy" or "upcoming" or "due-now" or "blocked" => risk,
        _ => "unknown",
    };

    private static string SafeSeverity(string? severity) => severity switch
    {
        "warning" or "error" or "fatal" => severity,
        _ => "error",
    };

    private static string SafeDiagnosticStatus(string? status) => status switch
    {
        "unresolved" or "investigating" or "resolved" or "ignored" => status,
        _ => "unresolved",
    };

    private static string? SafeEnrollmentReason(string? reason) => reason switch
    {
        null => null,
        "already-accepted" => "already-accepted",
        _ => "attention-required",
    };

    private static string? PreferredVersion(CatalogAppV2Dto app) =>
        string.IsNullOrWhiteSpace(app.ShortVersion) ? app.Version : app.ShortVersion;
}
