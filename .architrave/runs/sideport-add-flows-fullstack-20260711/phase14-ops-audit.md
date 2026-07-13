# Phase 14 Read-only Homelab Audit

Date: 2026-07-13

The audit used read-only SSH and Kubernetes queries. It did not read or print
`SIDEPORT_API_TOKEN`, restart a workload, publish an image, or mutate cluster
state.

## Current runtime

- SSH alias `homelab`: reachable.
- Deployment: `default/sideport`, `1/1` ready, strategy `Recreate`.
- Live image: `ghcr.io/dragoshont/sideport:0.1.12`.
- Pod: `2/2` running, zero restarts, scheduled on node `home`.
- USB socket mount: host `/var/run/netmuxd` → container
  `/var/run/usbmuxd`.
- Pairing-record custody: host `/var/lib/lockdown` → container
  `/var/lib/lockdown`, read-only.
- Durable state: PVC `sideport-state` → `/var/lib/sideport`.
- Container security: no privilege escalation, all capabilities dropped,
  read-only root filesystem.
- Explicit node selector: absent. The single current pod is on `home`; a
  multi-node future deployment would need pinning to the host that owns the
  usbmux socket and pairing records.

## Device availability

- USB devices from `idevice_id -l`: 1 after the owner connected the iPhone.
- Network devices from the ordinary upstream USB-only CLI socket: 0. Sideport's
  own `/api/devices` call through the mounted merged `netmuxd` socket returned
  the unplugged phone with connection enum `1` (Wi-Fi).

## Physical USB baseline

- Pairing validation: PASS over USB.
- Device identity read: PASS (device name, iOS version, and product type were
  returned; the raw UDID is retained only in the private run context).
- Direct installed-app inventory: PASS.
- Bounded live refresh of the existing small `ro.hont.certcountdown`
  registration through release `0.1.12`: HTTP 200 with `success=true` in
  approximately 3.9 seconds; device-side installation-proxy progress reached
  100 percent.
- Authoritative USB verification: PASS after inventory propagation;
  `ideviceinstaller -u ... -l` returned `ro.hont.certcountdown`, version 1,
  display name `CertClock`.
- Wireless lockdown configuration reports `EnableWifiConnections=true`, but no
  network device was advertised while USB remained connected.

## Physical paired-Wi-Fi baseline

- The owner unplugged the cable while leaving the iPhone on the home network.
- Host USB discovery became empty as expected.
- Sideport's live device endpoint continued to return the phone over Wi-Fi.
- A bounded refresh of `ro.hont.certcountdown` through live release `0.1.12`
  completed with HTTP 200 and `success=true` in approximately 4 seconds.
- Device-side installation-proxy progress reached 90 percent, then 100 percent,
  and reported completion.
- Sideport's managed installed-app endpoint completed in approximately 2
  seconds and returned 98 apps including `ro.hont.certcountdown`, display name
  `CertClock`, version `0.1.0`.
- The host `ideviceinstaller -n` path was not used as verification because the
  operational runbook documents false negatives. Verification used Sideport's
  managed device read over the active paired-Wi-Fi session.

## Acceptance implication

The normal USB and paired-Wi-Fi success paths are now physically proven. The
live pod still runs `0.1.12`, not this working-tree repair, and the phase ledger
prohibits image publication/homelab rollout before Phases 7–15 pass. Therefore
no physical claim is made for the new hard-abort behavior. The remaining open
gate is a deliberately stalled/dropped wireless transfer against an executable
containing the repaired owned-socket abort, followed by lease-release and USB
recovery proof without restarting Sideport.

Required later evidence remains:

1. Exercise a bounded wireless failure/stall against the repaired release;
   prove the operation becomes
   `unknown`, the transfer task terminates, and the in-process lease releases.
2. Reconnect USB and complete verify-only reconciliation or a proven-safe rerun;
   prove no pod restart is required.

No token may be printed during that matrix; use the repository `AGENTS.md`
runbook and bounded requests.
