# Phase 6 Implementation Map

Sideport uses ASP.NET minimal API routes in `Program.cs`; it intentionally does
not have `*Endpoints.cs` controller files. This map lets semantic reviewers
spot-check the critical Phase 6 seams without guessing repository structure.

## Managed Apple credential and browser clearing

- UI credential/2FA/team component:
  `src/Sideport.Admin/src/App.tsx:1749`. The password is copied only into the
  immediate request, cleared from React state before awaiting the request, the
  local variable is overwritten immediately after request creation, and state
  is cleared again in `finally` (`:1785-1805`). 2FA state clears on success and
  error (`:1761-1773`).
- Client request boundary:
  `src/Sideport.Admin/src/api/sideportApi.ts:1186`.
- Protected no-store minimal API route and redacted response:
  `src/Sideport.Api/Program.cs:899-950`.
- Custody/authentication implementation:
  `src/Sideport.Api/AppleAccess/PersonalAppleAccess.cs` and
  `ManagedAppleCredentialStore.cs`.
- Team selection validates the account profile and an Apple-returned team:
  `src/Sideport.Api/AppleAccess/PersonalAppleAccess.cs` (team selection path)
  and its route in `src/Sideport.Api/Program.cs`.
- Security regressions:
  `tests/Sideport.Api.Tests/ManagedAppleAccessTests.cs` and the credential/
  2FA/security stories in
  `src/Sideport.Admin/src/FirstRunOnboardingPrototype.stories.tsx`.

## Explicit device enrollment and pairing consent

- Minimal API route: `src/Sideport.Api/Program.cs:1150-1176`.
- Durable/idempotent bounded enrollment:
  `src/Sideport.Api/DeviceInventory/DeviceEnrollmentService.cs`.
  Candidate discovery filters to unaccepted USB devices (`:913-922`), passive
  work calls `ListDevicesAsync`/`ProbeTrustAsync`, and only the explicit
  operation calls `PairAsync` (`:493-505`). A saved pairing request enters
  recovery and re-probes Trust rather than repeating pairing (`:588-674`).
  Acceptance follows a fresh verified USB trust probe (`:677-804`).
- Worker rehydration:
  `src/Sideport.Api/DeviceInventory/DeviceEnrollmentWorker.cs`.
- Controller separation between passive probe and pairing mutation:
  `src/Sideport.Devices/NetimobiledeviceController.cs:41-137`.
- Regressions:
  `tests/Sideport.Api.Tests/DeviceEnrollmentTests.cs` and
  `tests/Sideport.Devices.Tests/VendoredFirstPairingTests.cs`.

## Pending registration and exact lineage

- Durable lifecycle model:
  `src/Sideport.Orchestrator/AppRegistration.cs:10-34`.
- Catalog/account/team/device binding and pending-only creation:
  `src/Sideport.Api/Operations/PendingRegistrationService.cs:40-171`.
- Minimal API DTO output and mutation:
  `src/Sideport.Api/Program.cs:1955-2015`.
- Install preflight/submission and lifecycle enforcement:
  `src/Sideport.Api/Operations/OperationService.cs` (install preflight starts
  near `:350`; pending refresh rejection near `:80`; install execution and
  lineage checks continue through the operation service).
- UI always registers `lifecycle: pending-install` before preflight:
  `src/Sideport.Admin/src/App.tsx:340-430` and `:1230-1280`.
- HTTP and failure regressions:
  `tests/Sideport.Api.Tests/ApiSmokeTests.cs:1631` and `:2103`.

## Crash-safe finalization, reconciliation, and onboarding completion

- Exact post-verification finalizer:
  `src/Sideport.Api/Operations/OperationService.cs:3413-3655`. It validates
  saved artifact/device/account/team evidence, activates the registration,
  persists scheduler state and the immutable receipt, and only then allows the
  enclosing operation to become terminal. Re-entry recognizes already-written
  durable stages/evidence.
- Immutable receipt store:
  `src/Sideport.Api/Onboarding/OnboardingCompletionStore.cs`.
- Workflow recovery actions and completion truth:
  `src/Sideport.Api/Onboarding/OnboardingWorkflow.cs:480-650`.
- Reconciliation submission/execution/finalization:
  `src/Sideport.Api/Operations/OperationService.cs:950-1100` and
  `:3000-3412`; HTTP mapping is in `src/Sideport.Api/Program.cs`.
- Runtime polling, linked-child reconciliation, completion capability gate,
  scheduler update, and reload resume:
  `src/Sideport.Admin/src/App.tsx:268-646` and
  `src/Sideport.Admin/src/onboarding/RuntimeFirstRunOnboarding.tsx:145-420`.
- Adversarial/recovery regressions:
  `tests/Sideport.Api.Tests/ApiSmokeTests.cs:515-835`, `:1160-1490`, and
  `:1631`; stable real-HTTP OIDC actor coverage is in
  `tests/Sideport.Api.Tests/OperationHttpActorTests.cs`.

## Live scheduler and pending UI truth

- API status/settings:
  `src/Sideport.Api/Operations/SchedulerStatusService.cs`,
  `SchedulerSettingsStore.cs`, and routes in `src/Sideport.Api/Program.cs`.
- Runtime client/types:
  `src/Sideport.Admin/src/api/sideportApi.ts` and
  `src/Sideport.Admin/src/data/sideportTypes.ts`.
- Pending registration is labelled “Awaiting verified install” and never shown
  healthy: `src/Sideport.Admin/src/App.tsx:1083`, `:1451-1476`, and `:2399`.
- Storybook live-state evidence:
  `src/Sideport.Admin/src/SideportAdmin.stories.tsx`.
