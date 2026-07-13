# Phase 14 Deterministic Gates

## Focused transport regressions

- `dotnet test tests/Sideport.Devices.Tests/Sideport.Devices.Tests.csproj --no-restore`
  - PASS: 65/65.
  - Includes a real loopback-backed vendored installation-proxy/AFC stall where
    cancellation closes the transport and the managed install task terminates.
  - Existing duplicate-UDID tests continue to prove USB wins over Wi-Fi.
- `dotnet test tests/Sideport.Orchestrator.Tests/Sideport.Orchestrator.Tests.csproj --no-restore`
  - PASS: 55/55.
  - Includes hard-aborted timeout → `install-outcome-unknown` → released lease →
    later operation succeeds without process restart.
  - Existing cancellation-ignoring transfer test continues to prove the lease
    remains held until the actual transfer task terminates.

## Full deterministic gates

- `./gates/backend-checks.sh`: PASS.
  - Build: zero warnings, zero errors.
  - API: 479/479.
  - Orchestrator: 55/55.
  - Developer API: 102/102.
  - Devices: 65/65.
  - GrandSlam: 50/50.
  - Kubernetes plan/policy: 6/6 valid resources.
  - Deploy secret scan: PASS.
- `./gates/checks.sh`: PASS.
  - Production UI build: PASS.
  - Storybook interaction/accessibility: 86/86.
- `./gates/reconcile.sh`: PASS by transparent not-configured skip; this repo has
  no `tokens`/`tokenBuild` mapping in `architrave.config.json`.
- `git diff --check`: PASS.

## Physical acceptance

Not yet recorded. These deterministic gates do not claim a physical USB pair,
physical USB install/readback, or paired-Wi-Fi refresh. Phase 14 remains
in-progress until the device matrix is executed or the physical access blocker
is explicitly handed off.
