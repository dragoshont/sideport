# Phase 13 Intake

## Objective

Complete the real mobile-first app library and one-cable journey: continuous
device enrollment flows directly into app discovery, Developer Mode guidance,
one confirmed install, device verification, automatic refresh, best-effort
completion chime, and the verified `Installed — you can unplug` receipt.

## Acceptance criteria

1. A newly accepted iPhone continues directly to approved app choice; there
   are no separate Pair, Trust-confirmation, or Add buttons.
2. Developer Mode/restart/reconnect remains explicit operator guidance and is
   never presented as device-verified.
3. Apps are searchable by name, purpose, version, bundle, and provenance.
4. Manual upload, configured server storage, public GitHub, and selected
   private GitHub imports reuse the existing exact-permission backend flows.
5. Only inspected, managed artifacts are selectable; installed-phone metadata
   remains read-only and unmatched apps say `IPA file needed`.
6. Trusted bounded PNG icons may be extracted from the managed IPA and served
   from a same-origin capability-scoped endpoint; initials remain fallback.
7. One install action uses the existing preflight/planVersion, durable stages,
   verification, registration activation, and automatic refresh behavior.
8. Chime and `Installed — you can unplug` appear only after a successful
   device-verified operation or immutable onboarding receipt.
9. Audio failure does not change success; Sideport never claims app launch,
   Developer Mode, profile trust, or audio delivery was verified.
10. Member capability boundaries remain enforced; app import and GitHub source
    management stay Owner-only.

## Boundaries

- Device transport fixes and physical USB/Wi-Fi acceptance belong to Phase 14.
- No arbitrary URLs, broad GitHub tokens, MDM, deployment, apply, commit, or
  push.
