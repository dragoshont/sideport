# Phase 3 — Explicit Device Enrollment Gates

## Implemented boundary

- All device enumeration, diagnostics, installed-app reads, and trust probes open
  lockdown passively; none requests pairing.
- `POST /api/devices/enrollments` is the sole first-pairing mutation. It is
  protected by the existing `/api/*` authentication gate and creates a durable,
  actor-scoped, idempotent `enroll-device` operation.
- Enrollment waits on a dedicated worker, selects one unaccepted USB device,
  requests Trust only when a passive probe proves it is untrusted, verifies a
  fresh USB lockdown session, and then persists acceptance evidence. There is no
  second Add confirmation.
- More than one candidate blocks before pairing and returns only safe summaries.
  Wi-Fi can use an existing pairing record but cannot initiate first pairing.
- Post-pair ambiguity, timeout, restart, or expiry becomes
  `recovery-required`; recovery probes passively and never repeats pairing.
- Legacy/manual inventory remains `legacy-unverified` or `discovered`. It cannot
  satisfy onboarding acceptance.
- The refresh worker filters `type=refresh`, so it cannot consume enrollment
  records from the shared operation store.

## Deterministic evidence

1. `dotnet test tests/Sideport.Devices.Tests/Sideport.Devices.Tests.csproj --no-restore -m:1 /nodeReuse:false`
   - PASS: 63 passed, 0 failed.
   - Includes 11 vendored first-pairing regression tests.
2. `dotnet test tests/Sideport.Api.Tests/Sideport.Api.Tests.csproj --no-restore -m:1 /nodeReuse:false`
   - PASS: 83 passed, 0 failed.
   - Includes service and HTTP enrollment coverage for waiting, idempotent
     replay, one-active conflict, automatic acceptance, safe selection, lock,
     denial, Wi-Fi ineligibility, timeout, and restart recovery.
3. Focused enrollment filter
   - PASS: 20 passed, 0 failed.
   - Includes open-mode mutation rejection, mismatched-target idempotency,
     passive retry without a second pairing call, post-pair timeout/transport
     ambiguity, stale restart recovery, synchronous-open deadline enforcement,
     and transient worker/store recovery.
4. `dotnet test Sideport.slnx --no-restore -m:1 /nodeReuse:false`
   - PASS: API 83, DeveloperApi 78, Devices 63, GrandSlam 50, Orchestrator 45.
   - Total: 319 passed, 0 failed.
5. `git diff --check`
   - PASS before the semantic review request.

## Physical acceptance boundary

The code and simulated seams are green, but this environment does not contain a
physical iPhone/host usbmux setup. First Trust prompt, host pair-record
persistence across restart, first USB install, and subsequent Wi-Fi refresh are
not claimed as physically accepted. They remain release evidence for the
documented USB runbook and issue #3 gate.

## Semantic gate

The first independent review returned FAIL on open-mode mutation safety,
semantic-target idempotency, executable enrollment recovery, stale-record
ownership, bounded synchronous transport calls, worker recovery, and ambiguous
post-pair outcomes. Those findings were fixed and covered by the focused tests
above. The fresh reviews returned PASS with no remaining Phase 3 blockers.
