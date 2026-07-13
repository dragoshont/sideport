# Phase 14 Adversarial Review Prompt

Review only the Phase 14 transport delta and its interaction with the already
implemented operation/reconciliation contract.

## Required behavior

- A timed-out/stalled AFC upload or installation-proxy wait has an owned-socket
  hard-abort path.
- A deadline remains `install-outcome-unknown`; Sideport never infers success or
  automatically retries over USB.
- The process-wide signer/device lease is released only after the real managed
  transfer task terminates.
- If cancellation/close still does not terminate the task, the old held-lease
  observer behavior remains.
- A later operation can proceed without process restart only after confirmed
  transfer termination.
- USB preference over duplicate Wi-Fi visibility remains intact.
- Physical acceptance is not claimed by unit/integration tests.

## Primary files

- `src/Sideport.Devices/vendor/Netimobiledevice/InstallationProxy/InstallationProxyService.cs`
- `src/Sideport.Orchestrator/RefreshOrchestrator.cs`
- `src/Sideport.Orchestrator/OrchestratorOptions.cs`
- `tests/Sideport.Devices.Tests/VendoredFirstPairingTests.cs`
- `tests/Sideport.Orchestrator.Tests/RefreshOrchestratorTests.cs`
- `docs/sideport-backend-contract.md`
- Phase 14 intake/options/gates artifacts in this run directory.

## Review questions

1. Can any cancellation path release the lease while a device mutation task is
   still active?
2. Can a hard-aborted timeout be mislabeled as a clean failure or retried
   without reconciliation?
3. Are both bulk-upload and post-command response sockets covered?
4. Can cancellation callback exceptions replace the primary operation result?
5. Do tests exercise the real vendored socket-close behavior rather than only a
   fake cancellation token?
6. Is the remaining physical evidence gap stated honestly?

Return PASS / REVISE / FAIL with findings ordered by severity. Treat any
overlapping-mutation path, automatic ambiguous retry, false success, or physical
capability overclaim as a blocker.
