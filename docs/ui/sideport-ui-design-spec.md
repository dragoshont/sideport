# Sideport UI Design Spec

## Audience

Primary user: a technical owner who manages a small fleet of personal or team iPhones and sideloaded IPAs.

Secondary users:

- A teammate who can check device/app health.
- An operator who can trigger refreshes but should not change Apple credentials.
- A viewer who can inspect status and diagnostics.

## Information Architecture

Primary navigation:

- Overview
- Devices
- Apps
- Renewals
- Diagnostics
- Teams
- Users
- Settings

Secondary surfaces:

- Global command menu: search devices, apps, teams, diagnostics.
- Activity drawer: recent signs, installs, errors, auth events.
- System status popover: readiness, anisette, signer, scheduler, API auth.

## Overview

Purpose: answer "Is Sideport healthy?" in under five seconds.

Content:

- Fleet status strip: reachable devices, apps healthy, due soon, blocked.
- Renewal queue preview: next 5 items, sorted by risk.
- Device health list: connection, last seen, app slots, nearest expiry.
- Recent activity: sign/install/auth/device events.
- System checks: API, anisette, signer, scheduler.

Avoid:

- Decorative analytics cards.
- Big charts for tiny counts.
- Hero sections.

## First-Run Onboarding Flow

Flow steps:

1. System readiness: API, signer, anisette.
2. Apple identity: Apple ID, trusted anisette identity, 2FA state.
3. Team selection: list Apple Developer teams.
4. Device discovery: USB/Wi-Fi devices, trust state, last seen.
5. First app: select or enter IPA path, inspect IPA, choose device, choose team.
6. Sign/install/verify: pipeline stage view.
7. Finish: land on the device detail page.

Important states:

- Anisette unprovisioned.
- Trusted-device 2FA required.
- Apple rate limit / 502 throttle.
- No devices reachable.
- Device reachable but untrusted/unpaired.
- App slots full.
- Signer binary missing.
- Install failed.

Preferred pattern: a left checklist with persistent context and a right detail panel. Do not hide the pipeline behind a spinner.

## Devices

Desktop layout:

- Data table with sticky header.
- Search by name, UDID, bundle ID, team.
- Facets: connection, health, team, app slots, expiry risk.
- Sort: last seen, nearest expiry, app count, device name.

Columns:

- Device: name, product type, OS.
- Connection: USB, Wi-Fi, offline.
- Last seen.
- Apps: `0/3`, `1/3`, `2/3`, `3/3`.
- Nearest expiry.
- Health: healthy, warning, blocked, failed.
- Team.
- Last refresh.

Mobile layout:

- Search and filter button.
- Device cards grouped by health.
- Each card shows name, connection, last seen, app slots, nearest expiry, primary action.

Device empty states:

- No devices known yet: show setup action.
- Devices known but none reachable: show last known devices and troubleshooting.
- Device reachable over USB only: explain Wi-Fi pairing if relevant.

## Device Detail

Header:

- Device name, model, iOS version, connection status, last seen.
- Primary action: refresh due apps.
- Secondary actions: add app, diagnostics.

Tabs:

- Apps
- Signing
- Network
- Diagnostics
- Activity

Apps tab:

- Three explicit slots.
- Empty slot action: add app.
- Filled slot: icon/name/bundle/version, signature expiry, last refresh, status.
- Full slots state: explain the free-tier limit before the user tries to add another app.

Signing tab:

- Team.
- Certificate expiry.
- Profile expiry per app.
- Last signing identity reuse/mint.
- Single-signer queue state.

Network tab:

- Current connection type.
- Last USB seen.
- Last Wi-Fi seen.
- Pairing/trust state when available.
- Suggested actions.

Diagnostics tab:

- Last install result.
- Last launch check.
- Recent invalid-signature, install, launch-check, or OpenTelemetry-correlated failure events.
- Link to raw logs only as a secondary escape hatch.

Activity tab:

- Timeline: device discovered, app registered, cert/profile ensured, signed, installed, failed, user action.

## Apps

Purpose: manage app definitions independent of a device.

Views:

- All registered apps.
- By device.
- By team.
- Due soon.
- Failed refresh.

App detail:

- IPA metadata.
- Installed devices.
- Bundle ID.
- Version.
- Team/App ID/Profile.
- Refresh history.
- Diagnostics.

## Add App Flow

Steps:

1. Select target device.
2. Choose IPA source: server path first, later upload/library.
3. Inspect IPA: app name, icon, bundle ID, version, embedded profile if present.
4. Choose Apple team.
5. Preflight constraints:
   - Device registered or will register.
   - App ID exists or will create.
   - Certificate exists or will mint/reuse.
   - Slot available.
   - Signer ready.
6. Confirm.
7. Pipeline progress.
8. Verify installed and launch/diagnostic state.

Preflight should say what will happen before the user commits. This is especially important because Apple free-tier cert and app limits are scarce.

## Renewals

Purpose: the queue and risk surface.

Sections:

- Blocked: cannot refresh without intervention.
- Due now: expires inside configured lead time.
- Upcoming: sorted by expiry.
- Healthy: no action needed.

Queue item fields:

- App/device.
- Expires in.
- Team.
- Last attempt.
- Blocker/error.
- Action.

Single-flight behavior:

- If a refresh is running, show the current operation from the backend operation
   record.
- Do not allow parallel manual refresh without explaining serialization.
- Do not invent queued items. Show cancel/rerun only after the backend exposes a
   safe background operation boundary and capability flags.

## Teams

Two separate concepts must remain visually separate:

- Apple Developer Team: source of certificates/profiles.
- Sideport Workspace Team: local product users/roles.

Apple team view:

- Team ID/name/type.
- Apps using it.
- Devices registered through it.
- Certificate status.

## Users And Roles

Roles:

- Owner: credentials, settings, users, refresh, destructive actions.
- Admin: teams/devices/apps, refresh, diagnostics.
- Operator: refresh, diagnostics, device/app view.
- Viewer: read-only status.

Flows:

- Invite user.
- Assign role.
- Pending invite.
- Revoke access.
- View audit trail.

V1 may delegate authentication to the reverse proxy. The UI spec should still model roles so the product can grow cleanly.

## Diagnostics

Purpose: answer "why did this app not work?"

Issue list backed by OpenTelemetry and Sideport's own event history:

- Grouped by app + device + failure type.
- Last seen / first seen.
- Severity: info, warning, error, fatal.
- Status: unresolved, investigating, resolved, ignored.
- Evidence: refresh result, install result, launch check, trace ID, operation ID, span timeline, structured log snippet.

Issue categories:

- Invalid signature / Code=85.
- Provisioning profile expired.
- Device unreachable.
- Install failed.
- Launch failed.
- Anisette unavailable.
- 2FA required.
- Apple rate limit.
- Apple Developer Services error.
- Signer process failed or timed out.
- Device bridge/usbmuxd unavailable.
- App crash or Jetsam/watchdog later, only after a real app/log source exists.

Agent-assisted panel:

- "Explain this failure"
- "What should I try next?"
- "Create an investigation note"

Never upload logs, traces, crash logs, or device logs to external AI services without an explicit user decision.

## Settings

Sections:

- API auth / bearer token status.
- Apple identity and anisette trust.
- Scheduler.
- Signer binary.
- Device bridge/usbmuxd.
- Data retention.
- Observability: OpenTelemetry exporter, collector endpoint, trace/log retention, dashboard links.
- Integrations: Storybook/Chromatic; Sentry/Firebase Crashlytics only as future optional app-SDK integrations.

## Visual Direction

Tone:

- Quiet, precise, confident.
- Apple-like hierarchy without imitating macOS chrome.
- Operational, not decorative.

Theme:

- System font stack with Inter fallback.
- Light mode first, dark mode later.
- One primary accent: blue.
- Semantic status colors used sparingly.
- 8px spacing rhythm.
- Border radius 8-14px for controls/surfaces.
- Hairline separators and soft shadows, not glass blobs.

Components:

- App shell with sidebar on desktop, bottom/tab or drawer navigation on mobile.
- Tables on desktop, cards/lists on mobile.
- Badges for statuses.
- Progress stage component for signing pipeline.
- Timeline component for activity.
- Empty-state component with one primary next action.
- Confirmation dialog for refresh/sign/install actions.

## Copy Rules

- Say "refresh" for renewing a signed app before expiry.
- Say "sign" only inside the pipeline or technical details.
- Say "Apple Developer Team" for Apple teams.
- Say "Workspace users" for Sideport users.
- Prefer exact blockers over generic failures.

Examples:

- Good: "2FA is required before Sideport can refresh apps for this Apple ID."
- Bad: "Authentication failed."
- Good: "This device has 3 of 3 app slots in use. Remove an app registration before adding another."
- Bad: "Limit exceeded."

## Storybook State Matrix

Minimum stories:

- Overview: healthy, due soon, blocked, no devices.
- Device table: many devices, no devices, offline devices, filtering.
- Device card: USB, Wi-Fi, offline, blocked, full slots.
- Device detail: one app, three apps, failed app, no apps.
- Add app: preflight healthy, app slots full, signer missing, device offline.
- Renewal queue: empty, running, queued, failed, 2FA required.
- Diagnostics issue: invalid signature, install failure, anisette failure, developer-services failure, signer timeout, resolved.
- Settings: token configured, token missing, anisette unprovisioned.

## Implementation Guardrails

- Do not call refresh automatically from route load.
- Do not hide destructive or scarce-limit actions behind hover-only controls.
- Do not expose `/api/*` in dev without making auth state visible.
- Do not create frontend-only status semantics that the API cannot eventually support.
- Do not add a chart unless it helps choose an action.
