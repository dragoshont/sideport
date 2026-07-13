using Sideport.Api.AppleAccess;
using Sideport.Api.Catalog;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Operations;
using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.Onboarding;

public sealed record OnboardingWorkflowActionDto(string Action, string Label);

public sealed record OnboardingWorkflowEvidenceDto(
    string Id,
    string Label,
    string Detail,
    string Source,
    string EvidenceOrigin,
    DateTimeOffset CheckedAt);

public sealed record OnboardingWorkflowStepDto(
    string Id,
    string State,
    bool Required,
    string Source,
    string EvidenceOrigin,
    DateTimeOffset CheckedAt,
    string Reason,
    string? ActiveOperationId,
    OnboardingWorkflowActionDto? NextAction,
    IReadOnlyList<OnboardingWorkflowEvidenceDto> Evidence);

public sealed record OnboardingWorkflowNextActionDto(
    string StepId,
    string Action,
    string Label);

public sealed record OnboardingWorkflowDto(
    int SchemaVersion,
    string SetupState,
    bool ReadyNow,
    DateTimeOffset? CompletedAt,
    string? VerifiedOperationId,
    OnboardingWorkflowNextActionDto? NextAction,
    IReadOnlyList<OnboardingWorkflowStepDto> Steps);

public sealed record OnboardingWorkflowContext(
    SystemStatusDto System,
    PersonalAppleStatusDto Apple,
    IReadOnlyList<DeviceInfo> AcceptedReachableDevices,
    int AcceptedDeviceCount,
    IReadOnlyList<CatalogAppDto> CatalogApps,
    IReadOnlyList<AppRegistration> Registrations,
    IReadOnlyList<OperationRecordDto> Operations,
    OnboardingCompletionReceipt? Receipt,
    SchedulerSettingsState? Scheduler,
    DateTimeOffset CheckedAt,
    SigningIdentityInspection? SigningIdentity = null);

public static class OnboardingWorkflowBuilder
{
    public const int SchemaVersion = 2;

    public static OnboardingWorkflowDto Build(OnboardingWorkflowContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        DateTimeOffset checkedAt = context.CheckedAt;
        OperationRecordDto? enrollment = context.Operations
            .Where(operation =>
                string.Equals(operation.Type, DeviceEnrollmentService.OperationType, StringComparison.Ordinal) &&
                operation.Status is "queued" or "waiting" or "running")
            .OrderByDescending(operation => operation.CreatedAt)
            .ThenByDescending(operation => operation.OperationId, StringComparer.Ordinal)
            .FirstOrDefault();
        OperationRecordDto? latestInstall = context.Operations
            .Where(operation =>
                string.Equals(operation.Type, "install", StringComparison.Ordinal) &&
                operation.InstallIntent?.FinishOnboarding == true)
            .OrderByDescending(IsActiveOperation)
            .ThenByDescending(operation => operation.CreatedAt)
            .ThenByDescending(operation => operation.OperationId, StringComparer.Ordinal)
            .FirstOrDefault();
        OperationRecordDto? reconciliation = latestInstall is null
            ? null
            : context.Operations
                .Where(operation =>
                    string.Equals(operation.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) &&
                    string.Equals(operation.ParentOperationId, latestInstall.OperationId, StringComparison.Ordinal))
                .OrderByDescending(IsActiveOperation)
                .ThenByDescending(operation => operation.CreatedAt)
                .ThenByDescending(operation => operation.OperationId, StringComparer.Ordinal)
                .FirstOrDefault();
        AppRegistration? pendingRegistration = context.Registrations
            .Where(registration => registration.IsPendingInstall &&
                !context.Operations.Any(operation =>
                    string.Equals(operation.Type, "install", StringComparison.Ordinal) &&
                    operation.InstallIntent?.FinishOnboarding == false &&
                    string.Equals(operation.Target.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(operation.Target.BundleId, registration.BundleId, StringComparison.Ordinal)))
            .OrderByDescending(registration => registration.CreatedAt)
            .FirstOrDefault();
        bool hasSelectedApp = context.Receipt is not null || pendingRegistration is not null || latestInstall is not null;

        OnboardingWorkflowStepDto server = ServerStep(context.System, checkedAt);
        OnboardingWorkflowStepDto apple = AppleStep(context.Apple, checkedAt);
        OnboardingWorkflowStepDto device = DeviceStep(context, enrollment, checkedAt);
        OnboardingWorkflowStepDto app = AppStep(context, pendingRegistration, latestInstall, checkedAt);
        OnboardingWorkflowStepDto install = InstallStep(
            context,
            latestInstall,
            reconciliation,
            server.State == "complete" && apple.State == "complete" && device.State == "complete" && hasSelectedApp,
            checkedAt);
        OnboardingWorkflowStepDto ready = ReadyStep(context, checkedAt);
        OnboardingWorkflowStepDto[] steps = [server, apple, device, app, install, ready];

        OnboardingWorkflowStepDto? firstIncomplete = steps.FirstOrDefault(step => step.State != "complete");
        OnboardingWorkflowNextActionDto? nextAction = context.Receipt is null &&
            firstIncomplete?.NextAction is { } action
            ? new OnboardingWorkflowNextActionDto(firstIncomplete.Id, action.Action, action.Label)
            : null;
        bool readyNow = HasCurrentReceiptLineage(context);

        return new OnboardingWorkflowDto(
            SchemaVersion,
            context.Receipt is null ? "in-progress" : "complete",
            readyNow,
            context.Receipt?.CompletedAt,
            context.Receipt?.VerifiedOperationId,
            nextAction,
            steps);
    }

    private static bool HasCurrentReceiptLineage(OnboardingWorkflowContext context)
    {
        OnboardingCompletionReceipt? receipt = context.Receipt;
        if (receipt is null ||
            !context.System.Operational ||
            context.Scheduler is not { Enabled: true, NextEvaluationAt: not null } ||
            !string.Equals(context.Apple.State, "validated-recently", StringComparison.Ordinal) ||
            !string.Equals(context.Apple.AccountProfileId, receipt.AccountProfileId, StringComparison.Ordinal) ||
            !string.Equals(context.Apple.SelectedTeamId, receipt.TeamId, StringComparison.Ordinal) ||
            context.Apple.AuthValidatedAt is null ||
            context.Apple.TeamValidatedAt is null ||
            context.Apple.PendingChallengeId is not null ||
            context.SigningIdentity is not { State: "reusable", ExpiresAt: { } identityExpiry } ||
            identityExpiry <= context.CheckedAt)
        {
            return false;
        }

        if (!context.AcceptedReachableDevices.Any(device =>
                string.Equals(device.TrustState, "trusted", StringComparison.OrdinalIgnoreCase) &&
                device.UsableForInstall))
        {
            return false;
        }
        if (context.Operations.Any(operation =>
                OperationReconciliationEvidence.IsUnresolvedMutation(operation, context.Operations)))
        {
            return false;
        }

        CatalogAppDto? artifact = context.CatalogApps.FirstOrDefault(app =>
            string.Equals(app.Id, receipt.CatalogAppId, StringComparison.OrdinalIgnoreCase));
        if (artifact is null ||
            !string.Equals(artifact.Status, "ready", StringComparison.Ordinal) ||
            !string.Equals(artifact.BundleId, receipt.BundleId, StringComparison.Ordinal) ||
            artifact.CatalogVersion != receipt.CatalogVersion ||
            !string.Equals(artifact.Sha256, receipt.CatalogSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        AppRegistration? registration = context.Registrations.FirstOrDefault(candidate =>
            string.Equals(candidate.DeviceUdid, receipt.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.BundleId, receipt.BundleId, StringComparison.Ordinal));
        if (registration is null ||
            registration.IsPendingInstall ||
            !string.Equals(registration.TeamId, receipt.TeamId, StringComparison.Ordinal) ||
            !string.Equals(
                AppleAccountIdentity.ProfileIdFor(registration.AppleId),
                receipt.AccountProfileId,
                StringComparison.Ordinal) ||
            !string.Equals(registration.CatalogAppId, receipt.CatalogAppId, StringComparison.OrdinalIgnoreCase) ||
            registration.CatalogVersion != receipt.CatalogVersion ||
            !string.Equals(registration.CatalogSha256, receipt.CatalogSha256, StringComparison.OrdinalIgnoreCase) ||
            !HasCurrentRegistrationEvidence(context, receipt, registration))
        {
            return false;
        }

        // A current reusable identity and current Apple account/team validation
        // supersede transient inspection/authentication failures. A cutover
        // warning is different: keep it sticky until a later successful signer
        // use proves the Apple certificate inventory is reusable again.
        OperationRecordDto? latestCutoverBlocker = context.Operations
            .Where(operation =>
                operation.CreatedAt > receipt.CompletedAt &&
                string.Equals(operation.Target.AccountProfileId, receipt.AccountProfileId, StringComparison.Ordinal) &&
                string.Equals(operation.Target.TeamId, receipt.TeamId, StringComparison.Ordinal) &&
                string.Equals(operation.Error?.Code, "signing-cutover-required", StringComparison.Ordinal))
            .OrderByDescending(operation => operation.CreatedAt)
            .ThenByDescending(operation => operation.OperationId, StringComparer.Ordinal)
            .FirstOrDefault();
        OperationRecordDto? latestSuccessfulSignerUse = context.Operations
            .Where(operation =>
                operation.CreatedAt > receipt.CompletedAt &&
                operation.Type is "install" or "refresh" &&
                string.Equals(operation.Status, "succeeded", StringComparison.Ordinal) &&
                operation.Result?.Success == true &&
                string.Equals(operation.Target.AccountProfileId, receipt.AccountProfileId, StringComparison.Ordinal) &&
                string.Equals(operation.Target.TeamId, receipt.TeamId, StringComparison.Ordinal))
            .OrderByDescending(operation => operation.CreatedAt)
            .ThenByDescending(operation => operation.OperationId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (latestCutoverBlocker is not null &&
            (latestSuccessfulSignerUse is null || latestCutoverBlocker.CreatedAt > latestSuccessfulSignerUse.CreatedAt))
        {
            return false;
        }

        OperationRecordDto? verified = context.Operations.FirstOrDefault(operation =>
            string.Equals(operation.OperationId, receipt.VerifiedOperationId, StringComparison.Ordinal));
        if (verified is null ||
            !string.Equals(verified.Status, "succeeded", StringComparison.Ordinal) ||
            verified.Result is not { Success: true, ExpiresAt: not null, Version.Length: > 0 } result ||
            !string.Equals(result.BundleId, receipt.BundleId, StringComparison.Ordinal) ||
            !string.Equals(verified.Target.DeviceUdid, receipt.DeviceUdid, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(verified.Target.BundleId, receipt.BundleId, StringComparison.Ordinal) ||
            !verified.Stages.Any(stage =>
                string.Equals(stage.Id, "verify", StringComparison.Ordinal) &&
                string.Equals(stage.Status, "succeeded", StringComparison.Ordinal)))
        {
            return false;
        }

        OperationRecordDto? install = string.Equals(verified.Type, "install", StringComparison.Ordinal)
            ? verified
            : string.Equals(verified.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) &&
              !string.IsNullOrWhiteSpace(verified.ParentOperationId)
                ? context.Operations.FirstOrDefault(operation =>
                    string.Equals(operation.OperationId, verified.ParentOperationId, StringComparison.Ordinal) &&
                    string.Equals(operation.Type, "install", StringComparison.Ordinal))
                : null;
        InstallOperationIntentDto? intent = install?.InstallIntent;
        return intent is
            {
                FinishOnboarding: true,
                CatalogVersion: not null,
                CatalogSha256.Length: > 0,
            } &&
            string.Equals(intent.DeviceUdid, receipt.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(intent.BundleId, receipt.BundleId, StringComparison.Ordinal) &&
            string.Equals(intent.AccountProfileId, receipt.AccountProfileId, StringComparison.Ordinal) &&
            string.Equals(intent.CatalogAppId, receipt.CatalogAppId, StringComparison.OrdinalIgnoreCase) &&
            intent.CatalogVersion == receipt.CatalogVersion &&
            string.Equals(intent.CatalogSha256, receipt.CatalogSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCurrentRegistrationEvidence(
        OnboardingWorkflowContext context,
        OnboardingCompletionReceipt receipt,
        AppRegistration registration)
    {
        if (string.Equals(registration.LastVerifiedOperationId, receipt.VerifiedOperationId, StringComparison.Ordinal))
            return true;
        if (string.IsNullOrWhiteSpace(registration.LastVerifiedOperationId))
            return false;

        OperationRecordDto? operation = context.Operations.FirstOrDefault(candidate =>
            string.Equals(candidate.OperationId, registration.LastVerifiedOperationId, StringComparison.Ordinal));
        if (operation is null ||
            operation.Type is not ("install" or "refresh" or "verify-existing-registration" or "reconcile") ||
            !string.Equals(operation.Status, "succeeded", StringComparison.Ordinal) ||
            operation.Result is not { Success: true, ExpiresAt: not null, Version.Length: > 0 } result ||
            !string.Equals(result.BundleId, receipt.BundleId, StringComparison.Ordinal) ||
            !string.Equals(operation.Target.DeviceUdid, receipt.DeviceUdid, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(operation.Target.BundleId, receipt.BundleId, StringComparison.Ordinal) ||
            !string.Equals(operation.Target.AccountProfileId, receipt.AccountProfileId, StringComparison.Ordinal) ||
            !string.Equals(operation.Target.TeamId, receipt.TeamId, StringComparison.Ordinal) ||
            operation.Target.CatalogVersion != receipt.CatalogVersion ||
            !string.Equals(operation.Target.CatalogSha256, receipt.CatalogSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return operation.Stages.Any(stage =>
            (string.Equals(stage.Id, "verify", StringComparison.Ordinal) ||
             string.Equals(stage.Id, "refresh", StringComparison.Ordinal)) &&
            string.Equals(stage.Status, "succeeded", StringComparison.Ordinal));
    }

    private static OnboardingWorkflowStepDto ServerStep(SystemStatusDto status, DateTimeOffset checkedAt)
    {
        SystemStatusCheckDto[] serverChecks = status.Checks
            .Where(check => !string.Equals(check.Id, "device-transport", StringComparison.Ordinal))
            .ToArray();
        bool serverReady = status.Operational ||
            serverChecks.Length > 0 && serverChecks.All(check => !string.Equals(check.Status, "fail", StringComparison.Ordinal));
        string state = serverReady ? "complete" : "blocked";
        string reason = serverReady
            ? status.Operational
                ? "Sideport can safely continue setup."
                : "Sideport's core services are ready. The iPhone connection will be checked when you reach Connect iPhone."
            : serverChecks.FirstOrDefault(check => string.Equals(check.Status, "fail", StringComparison.Ordinal))?.Reason
              ?? "A required Sideport check failed.";
        OnboardingWorkflowEvidenceDto[] evidence = status.Checks
            .Select(check => Evidence(
                $"system:{check.Id}",
                check.Id,
                check.Reason,
                "system",
                check.CheckedAt))
            .ToArray();
        return Step(
            "server",
            state,
            "system",
            checkedAt,
            reason,
            nextAction: serverReady ? null : new("retry-checks", "Check again"),
            evidence: evidence);
    }

    private static OnboardingWorkflowStepDto AppleStep(PersonalAppleStatusDto apple, DateTimeOffset checkedAt)
    {
        bool authenticated = string.Equals(apple.State, "validated-recently", StringComparison.Ordinal);
        bool selectedTeam = authenticated &&
            !string.IsNullOrWhiteSpace(apple.SelectedTeamId) &&
            apple.TeamValidatedAt is not null;
        string state = selectedTeam ? "complete" : "action-required";
        OnboardingWorkflowActionDto action;
        string reason;
        if (string.Equals(apple.State, "two-factor-required", StringComparison.Ordinal))
        {
            action = new("complete-two-factor", "Enter verification code");
            reason = "Apple needs the verification code shown on a trusted device.";
        }
        else if (authenticated && apple.Teams.Count > 0 && !selectedTeam)
        {
            action = new("select-team", "Choose Apple team");
            reason = "Choose which Apple developer team Sideport should use.";
        }
        else if (apple.AccountProfileId is null)
        {
            action = new("connect-apple", "Connect Apple account");
            reason = "Connect the Apple account used to sign apps.";
        }
        else
        {
            action = new("start-sign-in", "Sign in to Apple");
            reason = "Sign in again so Sideport can validate the account and team.";
        }

        var evidence = new List<OnboardingWorkflowEvidenceDto>();
        if (apple.AuthValidatedAt is { } authenticatedAt)
            evidence.Add(Evidence("apple:authentication", "Apple account", "Authentication was validated.", "apple", authenticatedAt));
        if (selectedTeam && apple.TeamValidatedAt is { } teamAt)
            evidence.Add(Evidence("apple:team", "Apple team", "A team returned by Apple is selected.", "apple", teamAt));

        return Step(
            "apple-signer",
            state,
            "apple",
            checkedAt,
            selectedTeam ? "The Apple account and team are ready; certificate impact is reviewed before install." : reason,
            nextAction: selectedTeam ? null : action,
            evidence: evidence);
    }

    private static OnboardingWorkflowStepDto DeviceStep(
        OnboardingWorkflowContext context,
        OperationRecordDto? enrollment,
        DateTimeOffset checkedAt)
    {
        if (enrollment is not null)
        {
            return Step(
                "device",
                "in-progress",
                "device",
                checkedAt,
                enrollment.Stages.FirstOrDefault(stage => stage.Status == "running")?.Message
                    ?? "Sideport is waiting for the iPhone over USB.",
                activeOperationId: enrollment.OperationId,
                evidence: []);
        }

        DeviceInfo? usable = context.AcceptedReachableDevices.FirstOrDefault(device => device.UsableForInstall);
        if (usable is not null)
        {
            DateTimeOffset observedAt = usable.LockdownCheckedAt ?? checkedAt;
            return Step(
                "device",
                "complete",
                "device",
                checkedAt,
                usable.Connection == DeviceConnection.Usb
                    ? "An accepted iPhone is trusted and connected over USB."
                    : "An accepted iPhone is reachable over paired Wi-Fi; reconnect USB for the first install.",
                evidence:
                [
                    Evidence(
                        "device:accepted",
                        "Accepted iPhone",
                        $"Trust was verified over {usable.Connection.ToString().ToUpperInvariant()}.",
                        "device",
                        observedAt),
                ]);
        }

        string reason = context.AcceptedDeviceCount > 0
            ? "Reconnect an accepted iPhone over USB, unlock it, and keep it awake."
            : "Connect an iPhone over USB; Sideport will wait for Trust and add it automatically.";
        return Step(
            "device",
            "action-required",
            "device",
            checkedAt,
            reason,
            nextAction: new("start-enrollment", context.AcceptedDeviceCount > 0 ? "Reconnect iPhone" : "Add iPhone"),
            evidence: []);
    }

    private static OnboardingWorkflowStepDto AppStep(
        OnboardingWorkflowContext context,
        AppRegistration? pending,
        OperationRecordDto? install,
        DateTimeOffset checkedAt)
    {
        string? catalogAppId = context.Receipt?.CatalogAppId ?? pending?.CatalogAppId ?? install?.InstallIntent?.CatalogAppId;
        CatalogAppDto? selected = catalogAppId is null
            ? null
            : context.CatalogApps.FirstOrDefault(item => string.Equals(item.Id, catalogAppId, StringComparison.OrdinalIgnoreCase));
        if (catalogAppId is not null)
        {
            DateTimeOffset selectedAt = pending?.CreatedAt ?? install?.CreatedAt ?? context.Receipt?.CompletedAt ?? checkedAt;
            return Step(
                "app",
                "complete",
                "artifact",
                checkedAt,
                selected is null ? "The selected catalog app is saved." : $"{selected.Name} is selected from the Sideport library.",
                evidence:
                [
                    Evidence(
                        "artifact:selected",
                        "Selected app",
                        selected?.Name ?? catalogAppId,
                        "artifact",
                        selectedAt),
                ]);
        }

        bool hasReadyApp = context.CatalogApps.Any(item => string.Equals(item.Status, "ready", StringComparison.Ordinal));
        return Step(
            "app",
            "action-required",
            "artifact",
            checkedAt,
            hasReadyApp
                ? "Choose an app from the Sideport library."
                : "Import an IPA from this computer, configured storage, or GitHub.",
            nextAction: new("choose-app", hasReadyApp ? "Choose app" : "Add an app"),
            evidence: []);
    }

    private static OnboardingWorkflowStepDto InstallStep(
        OnboardingWorkflowContext context,
        OperationRecordDto? install,
        OperationRecordDto? reconciliation,
        bool prerequisitesComplete,
        DateTimeOffset checkedAt)
    {
        if (context.Receipt is not null)
        {
            return Step(
                "install",
                "complete",
                "operation",
                checkedAt,
                "The app was verified on the iPhone and automatic refresh was saved.",
                activeOperationId: context.Receipt.VerifiedOperationId,
                evidence:
                [
                    Evidence(
                        "operation:verified-install",
                        "Verified install",
                        "The bundle and signing expiry were read back from the iPhone.",
                        "operation",
                        context.Receipt.CompletedAt),
                ]);
        }

        if (install is not null)
        {
            if (reconciliation is not null)
            {
                bool verifiedReconciliation = HasVerifiedReconciliationEvidence(reconciliation);
                if (reconciliation.Status is "queued" or "running" ||
                    string.Equals(reconciliation.Status, "waiting", StringComparison.Ordinal) && !verifiedReconciliation)
                {
                    return Step(
                        "install",
                        "in-progress",
                        "operation",
                        checkedAt,
                        reconciliation.Stages.FirstOrDefault(stage => stage.Status == "running")?.Message
                            ?? "Sideport is checking the iPhone without repeating the install.",
                        activeOperationId: reconciliation.OperationId,
                        evidence: InstallEvidence(reconciliation));
                }

                if (verifiedReconciliation &&
                    reconciliation.Status is "running" or "waiting" or "recovery-required" or "succeeded")
                {
                    return Step(
                        "install",
                        "action-required",
                        "operation",
                        checkedAt,
                        reconciliation.Error?.Message
                            ?? "The iPhone evidence is saved; Sideport still needs to finish setup.",
                        activeOperationId: reconciliation.OperationId,
                        nextAction: new("retry-finalization", "Retry finishing setup"),
                        evidence: InstallEvidence(reconciliation));
                }

                if (string.Equals(reconciliation.Status, "succeeded", StringComparison.Ordinal) &&
                    reconciliation.Result?.SafeToRerun == true)
                {
                    return Step(
                        "install",
                        "action-required",
                        "operation",
                        checkedAt,
                        "The app is not on this iPhone and no transfer is active. Review and install it again.",
                        activeOperationId: install.OperationId,
                        nextAction: new("review-install", "Review install"),
                        evidence: InstallEvidence(reconciliation));
                }

                if (string.Equals(reconciliation.Status, "blocked", StringComparison.Ordinal))
                {
                    bool retryDeviceRead = reconciliation.Error?.Code is
                        "device-operation-still-active" or
                        "device-not-reachable" or
                        "device-usb-required" or
                        "device-not-trusted" or
                        "device-trust-check-unavailable" or
                        "reconciliation-device-read-unavailable";
                    return Step(
                        "install",
                        "blocked",
                        "operation",
                        checkedAt,
                        reconciliation.Error?.Message ?? "Sideport could not prove the iPhone state.",
                        activeOperationId: install.OperationId,
                        nextAction: retryDeviceRead
                            ? new("reconcile-install", "Check iPhone state again")
                            : new("review-install", "Review install"),
                        evidence: InstallEvidence(reconciliation));
                }
            }

            bool verified = HasVerifiedInstallEvidence(install);
            if (install.Status is "queued" or "running" ||
                string.Equals(install.Status, "waiting", StringComparison.Ordinal) && !verified)
            {
                return Step(
                    "install",
                    "in-progress",
                    "operation",
                    checkedAt,
                    install.Stages.FirstOrDefault(stage => stage.Status == "running")?.Message ?? "Sideport is installing the app.",
                    activeOperationId: install.OperationId,
                    evidence: InstallEvidence(install));
            }

            if (verified && install.Status is "running" or "waiting" or "recovery-required" or "succeeded")
            {
                return Step(
                    "install",
                    "action-required",
                    "operation",
                    checkedAt,
                    install.Error?.Message ?? "The app is verified; Sideport still needs to finish saved setup state.",
                    activeOperationId: install.OperationId,
                    nextAction: new("retry-finalization", "Retry finishing setup"),
                    evidence: InstallEvidence(install));
            }

            if (install.Status is "failed" or "blocked" or "unknown" or "recovery-required" or "succeeded")
            {
                bool unknown = string.Equals(install.Status, "unknown", StringComparison.Ordinal);
                return Step(
                    "install",
                    "blocked",
                    "operation",
                    checkedAt,
                    install.Error?.Message ?? "The install needs attention.",
                    activeOperationId: install.OperationId,
                    nextAction: new(unknown ? "reconcile-install" : "review-install", unknown ? "Check iPhone state" : "Review install"),
                    evidence: InstallEvidence(install));
            }
        }

        return Step(
            "install",
            prerequisitesComplete ? "action-required" : "not-started",
            "operation",
            checkedAt,
            prerequisitesComplete
                ? "Review the server-approved plan, then install and finish in one action."
                : "Complete the earlier setup items before installation.",
            nextAction: prerequisitesComplete ? new("review-install", "Review install") : null,
            evidence: []);
    }

    private static OnboardingWorkflowStepDto ReadyStep(OnboardingWorkflowContext context, DateTimeOffset checkedAt)
    {
        if (context.Receipt is null)
        {
            return Step(
                "ready",
                "not-started",
                "operation",
                checkedAt,
                "Ready appears only after the verified install and immutable completion receipt.",
                evidence: []);
        }

        return Step(
            "ready",
            "complete",
            "operation",
            checkedAt,
            context.Scheduler?.Enabled == true
                ? "Sideport is ready and automatic due-only refresh is enabled."
                : "Setup completed previously, but automatic refresh is currently disabled.",
            evidence:
            [
                Evidence(
                    "receipt:onboarding-complete",
                    "Setup receipt",
                    "Verified setup completion is saved.",
                    "operation",
                    context.Receipt.CompletedAt),
            ]);
    }

    private static OnboardingWorkflowEvidenceDto[] InstallEvidence(OperationRecordDto operation) =>
        operation.Stages
            .Where(stage => stage.StartedAt is not null)
            .Select(stage => Evidence(
                $"operation:{stage.Id}",
                stage.Label,
                stage.Message,
                "operation",
                stage.CompletedAt ?? stage.StartedAt ?? operation.UpdatedAt))
            .ToArray();

    private static bool IsActiveOperation(OperationRecordDto operation) =>
        operation.Status is "queued" or "waiting" or "running";

    private static bool HasVerifiedInstallEvidence(OperationRecordDto operation) =>
        operation.InstallIntent is { } intent &&
        operation.Result is { Success: true, BundleId: not null, ExpiresAt: not null, Version.Length: > 0 } result &&
        operation.Stages.Any(stage =>
            string.Equals(stage.Id, "install", StringComparison.Ordinal) &&
            string.Equals(stage.Status, "succeeded", StringComparison.Ordinal)) &&
        operation.Stages.Any(stage =>
            string.Equals(stage.Id, "verify", StringComparison.Ordinal) &&
            string.Equals(stage.Status, "succeeded", StringComparison.Ordinal)) &&
        string.Equals(result.BundleId, intent.BundleId, StringComparison.Ordinal) &&
        string.Equals(operation.Target.BundleId, intent.BundleId, StringComparison.Ordinal) &&
        string.Equals(operation.Target.DeviceUdid, intent.DeviceUdid, StringComparison.OrdinalIgnoreCase);

    private static bool HasVerifiedReconciliationEvidence(OperationRecordDto operation) =>
        string.Equals(operation.Type, OperationReconciliationEvidence.OperationType, StringComparison.Ordinal) &&
        operation.Result is
        {
            Success: true,
            ExpiresAt: not null,
            Version.Length: > 0,
            ReconciledOperationId.Length: > 0,
        } result &&
        string.Equals(result.BundleId, operation.Target.BundleId, StringComparison.Ordinal) &&
        string.Equals(result.ReconciledOperationId, operation.ParentOperationId, StringComparison.Ordinal) &&
        operation.Stages.Any(stage =>
            string.Equals(stage.Id, "verify", StringComparison.Ordinal) &&
            string.Equals(stage.Status, "succeeded", StringComparison.Ordinal));

    private static OnboardingWorkflowStepDto Step(
        string id,
        string state,
        string evidenceOrigin,
        DateTimeOffset checkedAt,
        string reason,
        string? activeOperationId = null,
        OnboardingWorkflowActionDto? nextAction = null,
        IReadOnlyList<OnboardingWorkflowEvidenceDto>? evidence = null) =>
        new(
            id,
            state,
            Required: true,
            Source: "live",
            evidenceOrigin,
            checkedAt,
            reason,
            activeOperationId,
            nextAction,
            evidence ?? []);

    private static OnboardingWorkflowEvidenceDto Evidence(
        string id,
        string label,
        string detail,
        string origin,
        DateTimeOffset checkedAt) =>
        new(id, label, detail, "live", origin, checkedAt);
}
