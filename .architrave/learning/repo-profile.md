# Architrave Repo Profile

Concise, validated repository description for future Architrave runs. Keep this high-signal and cite evidence; move detailed rules into docs or path-scoped instructions.

## Purpose

Sideport is a self-hosted service that authenticates a server-custodied Personal
Apple ID, re-signs IPAs, installs them on physical iPhones through usbmux, and
keeps verified registrations refreshed.

## Surfaces And Lanes

- React/Vite admin: `src/Sideport.Admin`
- ASP.NET minimal API and durable operations: `src/Sideport.Api`
- Apple auth/developer/signing: `src/Sideport.DeveloperApi`
- Physical-device transport: `src/Sideport.Devices`
- Registration/sign/install orchestration: `src/Sideport.Orchestrator`
- Kubernetes/Compose packaging: `deploy`

## Source Of Truth

- Cross-tier contract: `docs/sideport-backend-contract.md`
- UI design source: `src/Sideport.Admin/.storybook` and
  `docs/ui/sideport-ui-design-spec.md`
- Architecture: `docs/architecture/adr-0001-roadmap-foundations.md`
- Architrave routing/gates: `architrave.config.json`, `gates/`
- Device install operational safety: `AGENTS.md`

## Build And Test

- Backend: `dotnet build Sideport.slnx`, `dotnet test Sideport.slnx`
- Admin: `npm --prefix src/Sideport.Admin run build`,
  `npm --prefix src/Sideport.Admin run test:ci` (lint plus Storybook
  interaction/a11y tests),
  `npm --prefix src/Sideport.Admin run test:screens`
- Aggregate gates: `gates/checks.sh`, `gates/backend-checks.sh`,
  `gates/reconcile.sh`
- Kubernetes is plan-only: render with the configured kustomize command and
  validate with kubeconform; never apply from an Architrave run.

## Architecture Map

- Single replica and process-wide single-flight signing are safety invariants.
- Durable domain state uses JSON files under `Sideport:State:Directory`, atomic
  temp-file replace, and process-local locks.
- Apple passwords remain in environment/SOPS or macOS Keychain custody; browser
  APIs may handle Apple ID identifiers and 2FA codes, never passwords/keys.
- The device controller uses managed Netimobiledevice over the host usbmux
  socket. First installs are operationally reliable over USB; Wi-Fi bulk upload
  is not yet an accepted path.
- The React UI must bind to live/derived contract evidence; Storybook owns hard
  mock states before runtime integration.

## Recurring Gotchas

- Preserve `/var/lib/sideport`, the anisette ADI volume, and signing identities
  across restarts. Losing them causes state loss, 2FA churn, and potentially
  destructive certificate replacement.
- `SIDEPORT_DEVICE_ID` is the stable `X-Mme-Device-Id` UUID, not an iPhone UDID.
- Device enumeration is not proof of lockdown trust or install usability.
- A new local identity currently reaches certificate revoke-before-mint code;
  signer cutover needs exact preflight/acknowledgement before production use.
- Check the built/deployed image version against source. A manifest pin can lag
  current APIs/UI.
- Current Architrave config has no `designMap` or `tokens` pointer; resolve that
  gate or reuse existing Storybook/CSS values without adding visual constants.
- Worktrees may be dirty with user-owned changes. Audit and record ownership
  before editing overlapping files.

## Validated Facts

| Fact | Evidence | Last Checked |
|---|---|---|
| Backend/UI contract and current roadmap are JSON-store/single-replica based. | `docs/sideport-backend-contract.md`; ADR 0001 | 2026-07-11 |
| Current source has Personal Apple sign-in, 2FA, and team listing. | `src/Sideport.Api/AppleAccess/PersonalAppleAccess.cs` | 2026-07-11 |
| Current source has known devices, IPA upload, queued operations, and diagnostics issues. | `src/Sideport.Api/Program.cs` and focused service folders | 2026-07-11 |
| Enumeration can currently survive lockdown failure while known-device mapping labels reachable devices trusted. | `NetimobiledeviceBackend.cs`; `KnownDeviceService.cs` | 2026-07-11 |
| Current certificate creation revokes development certificates before minting when no usable local identity exists. | `AppleDeveloperPortal.EnsureCertificateAsync` | 2026-07-11 |
| Phase 6 runtime onboarding persists a pending registration, runs the exact preflight/install operation, resumes reconciliation/finalization, and completes only from a durable receipt; scheduler settings are live. | `src/Sideport.Admin/src/App.tsx`; `src/Sideport.Admin/src/onboarding/RuntimeFirstRunOnboarding.tsx`; Phase 6 gate artifact | 2026-07-12 |
| USB is the accepted install path; Wi-Fi bulk transfer can hang. | `AGENTS.md` runbook; issue #3 | 2026-07-11 |
| Onboarding implementation plan passed backend, UI, and independent rubric review. | run `sideport-onboarding-plan-20260711`; plan hash `b0d9363a…` | 2026-07-11 |
| The Storybook suite passes 135 render, interaction, security, and accessibility tests, including 74 focused fresh-deployment onboarding tests; desktop/mobile Playwright passes 20/20. | `FirstRunOnboardingPrototype.stories.tsx`; `SideportAdmin.stories.tsx`; Phase 6 gate artifact | 2026-07-12 |

## Last Reviewed

2026-07-12 on the `codex/apple-like-add-flows` working tree based on
`697646b`; validate again after any deployment or later-phase implementation.
