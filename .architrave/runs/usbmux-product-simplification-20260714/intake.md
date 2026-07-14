# Intake — Transport Hardening and Product Simplification

## Problem

Sideport's physical iPhone path is still vulnerable to transport churn and
pairing ownership ambiguity. The current UI remains more technical and less
actionable than the intended multi-user product: cards are often inert, detail
URLs are absent, recurring jobs overlap, and healthy system evidence competes
with apps, people, and devices.

## Approved outcome

- Exactly one pairing owner per deployment; never automatic fallback between
  host and Sideport pairing.
- Bounded automatic recovery uses fresh passive probes and never repeats a
  pairing mutation after it begins.
- Device transport failures become typed, observable dispositions instead of
  parsing user-facing strings.
- A mobile-first Storybook shell exposes Home, Apps, Devices, and People as the
  primary destinations; Activity and Settings are secondary surfaces.
- Lists and attention items drill into canonical entity details. Inert cards
  and dead controls are removed.
- Apps is both the managed library and browse/store surface; owner-only sources
  remain secondary.

## Grounding

- Production operation and host usbmuxd logs from 2026-07-14.
- `docs/sideport-backend-contract.md`.
- `docs/ui/sideport-ui-design-spec.md` and Storybook.
- Current runtime and canonical React implementations.
- libimobiledevice usbmuxd/lockdown sources and vendored Netimobiledevice.
- Mobbin references for Notion-like navigation/inbox, device and member lists,
  app browsing, and calm connection assistants.

## Constraints

- Preserve the dirty `/Users/dragoshont/Repo/sideport` worktree untouched.
- No Netimobiledevice major upgrade in the first implementation slice.
- No new dependency unless the current slice proves it necessary.
- No production device-layer deployment without deterministic gates and a
  physical iPhone safety plan.
