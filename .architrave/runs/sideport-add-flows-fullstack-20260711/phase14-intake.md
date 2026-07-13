# Phase 14 Intake — Device Transport and Physical Acceptance

## Grounding

- Canonical phase: Phase 14 in `phase-ledger.md`; it is the only in-progress
  phase.
- Backend contract: `docs/sideport-backend-contract.md`, especially the
  watchdog, reconciliation, and USB/Wi-Fi verification sections.
- Operational evidence: repository `AGENTS.md` issue-3 runbook. Paired Wi-Fi
  bulk upload can hang or drop, the process-wide lease then remains jammed, and
  a pod restart is currently required. USB is the reliable recovery path.
- Runtime path: `RefreshOrchestrator.InstallWithWatchdogAsync` →
  `NetimobiledeviceController.InstallAsync` →
  `NetimobiledeviceBackend.InstallAsync` → vendored
  `InstallationProxyService.Install`.

## Reproduced defect

Cancellation reached `NetworkStream.ReadAsync` / `WriteAsync`, but the active
AFC and installation-proxy service sockets were not owned by a cancellation
callback. A runtime/socket combination that did not promptly honor cooperative
cancellation left the managed transfer task alive. The orchestrator correctly
refused to overlap another mutation, but this held the signer/device lease until
process restart.

## Required invariants

1. A timed-out install is never inferred successful or safe to retry.
2. No automatic duplicate install follows an ambiguous Wi-Fi mutation.
3. The process-wide lease is released only after the actual managed transfer
   task terminates.
4. Sideport must actively close every transport it owns for the install leg so
   cancellation is a bounded hard abort rather than a request only.
5. An implementation that still fails to terminate keeps the lease and remains
   restart/reconciliation constrained.
6. USB continues to win when both transports are visible.
7. Physical USB and paired-Wi-Fi claims require device evidence; tests alone do
   not close the phase.

## Authorization boundary

This phase changes repository runtime code and tests only. It does not publish
an image, deploy, restart the homelab, apply Kubernetes/Authentik state, or read
or print secret material.
