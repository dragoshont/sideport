# Sideport Onboarding Workflow Storyboard

Date: 2026-06-08
Status: implementation draft; catalog/inspection slice implemented locally

For the full empty-portal-to-signed-IPA and daily 4 AM / 7 PM refresh inventory,
see `docs/ui/sideport-end-to-end-install-refresh-plan.md`.

## Adversarial Review Response

The first storyboard was intentionally ambitious. The review found one central
risk: the UI could accidentally imply Sideport already has persisted users,
known-offline phones, a durable app catalog, IPA inspection, install pipelines,
queue positions, and operation timelines. The current live backend does not yet
provide all of that.

Accepted corrections:

- First implementation slice is a **catalog, inspection, and registration slice**, not the full product model.
- `App Catalog` starts with a seeded Cert Clock test app from the backend catalog endpoint; it is live-inspected when the IPA exists on the server and marked missing/invalid when it does not.
- `/api/apps` remains **registered installations**, not catalog entries.
- The first wizard saves an installation registration through `POST /api/apps`; it must not claim to sign, install, verify, or run the signing pipeline yet.
- `/api/devices` means **currently reachable iPhones** until a persisted known-phone endpoint exists.
- `/api/devices/{udid}/installed-apps` exposes installed user apps when the device backend can read them; the UI must keep installed apps separate from Sideport registrations.
- iPhone tasks are either `verified by Sideport` or `guided only`; Developer Mode/profile trust must not be marked complete without a real signal.
- Slot copy must say `Sideport registrations on this phone`, and must note that apps installed outside Sideport are not counted yet.
- Queue position, operation stages, and single-flight progress wait for real operation/queue endpoints.
- Raw diagnostics log tail stays advanced; highlights and redacted evidence remain the default troubleshooting path.

Approved first slice after this review:

1. Rename the top-level raw Add App route into `App Catalog`.
2. Show a backend catalog entry for Cert Clock with live inspection metadata when the seed IPA is present.
3. Persist catalog entries and app registrations in the API state directory.
4. Show real registered installations from `/api/apps` separately from installed phone apps.
5. Add a registration wizard that currently saves a durable registration only.
6. Keep implementation honest: no known-phone persistence, install verification, signing preflight, users/roles, or queue state until backend endpoints exist.

## Goal

Design Sideport around the real operating model:

> A Sideport owner manages known users, known phones, and a reusable app catalog.
> Each installation is one catalog app assigned to one phone slot, signed by one
> Apple team, refreshed by one single-flight signer, and explainable through one
> operation timeline.

The UI should start from registered users, phones, and apps, not from ad-hoc
forms. The first useful path should feel like a calm setup assistant, not a raw
API client.

## Design North Star

Sideport should feel like an Apple-device operations console: quiet, ordered,
direct, and precise. Borrow the clarity of Xcode signing/run flows and the
inventory discipline of Jamf/SimpleMDM/Tailscale, but avoid developer clutter and
generic dashboard decoration.

Core principles:

- Show what exists: users, phones, app catalog, installations, operations.
- Show what is missing: owner, Apple identity, anisette trust, phone trust, app registration.
- Separate catalog apps from installed/registered app slots.
- Keep iPhone actions explicit and human-readable.
- Never hide signing behind a spinner; show each stage.
- Treat free-tier limits as first-class UI, especially 3 app slots and single-flight signing.

## Mental Model

| Object | Meaning | First-class UI surface |
| --- | --- | --- |
| Sideport user | Local operator/owner/viewer. V1 may be reverse-proxy backed. | Users |
| Apple identity | Apple ID + trusted anisette identity + developer teams. | Setup, Settings |
| Apple team | Signing authority and certificate/profile owner. | Settings, app install wizard |
| Phone | Persisted known iPhone, whether online or offline. | Devices |
| Catalog app | Reusable IPA/app definition, independent of any phone. | App Catalog |
| Installation | Catalog app on one phone slot with expiry/refresh state. | Device detail, Renewals |
| Operation | Sign/install/refresh attempt with stages and evidence. | Diagnostics, Activity |

Current confusing behavior to fix in the product model: a phone can have apps
already installed, while Sideport has zero registered installations. The UI must
say that plainly. `0/3 registered` is not the same as `0 apps installed`.

## Information Architecture

Primary navigation after this redesign:

1. Overview
2. Devices
3. App Catalog
4. Renewals
5. Diagnostics
6. Users
7. Settings

Setup should be a contextual `Setup Center`, visible while something is
incomplete. It should not remain the permanent first nav item once Sideport is
healthy.

Remove `Add App` as a permanent top-level page. Replace it with `Install app`
actions launched from:

- a catalog app,
- an empty phone slot,
- the device detail page,
- or the setup checklist when no app is installed yet.

## Storyboard

### Scene 1: First Open - Setup Center

Home server lane:

- API process
- API auth token
- signer binary
- anisette identity
- Apple identity
- Apple team
- first catalog app

User/iPhone lane:

- connect iPhone over USB
- unlock phone
- tap Trust This Computer
- enable Developer Mode
- trust developer profile after first install
- keep phone awake during install

Screen composition:

- Top status: `Sideport setup: 4 of 8 ready`.
- Left column: home server checks with live status.
- Right column: iPhone actions with short physical instructions.
- Bottom: primary action for the next unblocked step.

Good copy:

- `Anisette is trusted. Apple can recognize this server as an existing device identity.`
- `Connect an iPhone to register the first phone.`
- `Developer Mode is checked on the phone, not from Sideport.`

Bad copy to avoid:

- `Partial API`
- `Mock`
- `Unknown` without cause
- `Registration disabled` without the missing field or blocker

### Scene 2: Owner and Access

If Sideport is behind reverse-proxy auth:

- Show `Access is managed by your reverse proxy`.
- Record the local owner display name and role.
- Keep invite/user management visible as a future-capable surface.

If Sideport owns auth later:

- Owner creates first local account.
- Owner can invite Admin, Operator, Viewer.

Role model:

- Owner: settings, users, Apple identities, destructive actions.
- Admin: phones, catalog, installs, renewals.
- Operator: refresh and diagnostics.
- Viewer: read-only health.

### Scene 3: Apple Signing Setup

The flow should feel like Xcode Signing & Capabilities, simplified.

Steps:

1. Select or add Apple ID.
2. Verify anisette identity.
3. Authenticate with GrandSlam.
4. Handle 2FA if Apple requires it.
5. Choose Apple Developer team.
6. Show certificate/profile posture.

Borrow from Xcode:

- explicit team selection,
- bundle/team/profile warnings before signing,
- signing capability preflight,
- clear run/install pipeline.

Do not copy Xcode's dense developer layout. Sideport should translate those
concepts into plain admin language.

### Scene 4: Add Phone Wizard

Entry points:

- Setup Center: `Add first phone`
- Devices empty state: `Add phone`
- Devices toolbar: plus button

Wizard steps:

1. Detect phone
   - USB and Wi-Fi detected devices.
   - Show name, model, iOS version, UDID, connection.
2. Trust steps
   - `Unlock the iPhone.`
   - `Tap Trust This Computer.`
   - `Enter passcode if prompted.`
3. Register phone record
   - Owner/user assignment.
   - Friendly name.
   - Apple team default.
4. iPhone readiness
   - Developer Mode guidance.
   - Profile trust guidance after first app install.

Important states:

- no phone detected,
- phone detected but locked,
- phone seen over USB only,
- phone known but currently offline,
- phone reachable over Wi-Fi,
- phone has no Sideport registrations yet.

Device list should show known offline phones. Current `/api/devices` only shows
reachable phones; this needs a persisted known-phone store.

### Scene 5: App Catalog Seed

The App Catalog is separate from device slots.

Initial catalog should include Cert Clock as the first test app:

- Name: Cert Clock
- Purpose: test signing, install, expiry countdown, and refresh flow.
- Current known seed path: `/var/lib/altserver/ipa/CertCountdown.ipa` on the homelab host.
- Locally mirrored path: `homelab/ios-app/CertCountdown.ipa`.
- Verified seed metadata today: display `CertClock`, bundle `ro.hont.certcountdown`, version `0.1.0`, build `1`, platform `iphoneos`, minimum iOS `16.0`, no embedded profile.
- Source: server IPA path first; later upload/library.
- Version/checksum/profile state: comes from the backend IPA inspection endpoint when the IPA exists.
- Icon remains a generated tone/initial until IPA icon extraction exists.
- Status: `Available to install`, not `installed`.

How apps enter Sideport:

1. V1 seed: Cert Clock is configured as the first server-side catalog seed.
2. Current live path: operator supplies a server IPA path to `/api/catalog/apps/inspect`.
3. Inspection returns app name, bundle ID, version, checksum, and embedded-profile state.
4. Catalog store persists inspected app definitions, then assigns them to phones as registrations.

Catalog card states:

- Available
- Installed on N phones
- Update source missing
- IPA inspection failed
- Bundle ID mismatch
- Unsupported platform

App Catalog actions:

- `Install on phone`
- `Inspect IPA`
- `View installations`
- `Replace IPA source`

### Scene 6: Install App Wizard

This replaces the current raw Add App form.

Step 1: Choose app

- Pick from App Catalog, with Cert Clock first.
- Or register a new IPA source.
- Show app name, icon, bundle ID, version, checksum.

Step 2: Choose phone

- Show known phones, online first.
- Show slot use: `0/3 registered`, `2/3 registered`, `3/3 full`.
- Offline phones can be selected only if install is queued for later, if supported.

Step 3: Choose Apple team

- Default to phone/team association.
- Explain if no team is available.
- Show Apple free-tier constraints before confirm.

Step 4: Preflight

Checklist:

- Phone reachable
- Phone trusted
- Developer Mode likely needed/known
- Apple team selected
- Device registered with Apple team or will be registered
- App ID exists or will be created
- Certificate exists/reused or will be minted
- Profile will be created/downloaded
- Signing slot available
- Signer ready
- Anisette trusted
- Single-flight signer idle or queue position shown

Step 5: Confirm

Clear operation summary:

`Install Cert Clock on Dragos iPhone using team M62Z4M5EUY. This will use slot 1 of 3.`

Step 6: Pipeline

Stages:

1. Inspect IPA
2. Authenticate Apple ID
3. Ensure device registration
4. Ensure App ID
5. Ensure certificate
6. Download provisioning profile
7. Sign IPA
8. Install on iPhone
9. Verify install state

Each stage has:

- pending/running/succeeded/failed,
- elapsed time,
- last log line,
- retry or next action if failed.

Step 7: Finish

Land on device detail, slot filled:

- Cert Clock
- expires in N days
- last installed now
- next refresh scheduled
- refresh manually button if safe

### Scene 7: Steady State Overview

Overview should start from registered reality:

- `2 users`
- `1 Apple identity`
- `1 Apple team`
- `1 phone known, 1 reachable`
- `1 catalog app`
- `1 installation, expires in 6 days`
- `Signer idle`
- `Next refresh in 4 days`

The dashboard should not show decorative cards. Every number links to the object
or action behind it.

### Scene 8: Device Detail

Header:

- phone name,
- model/iOS,
- owner,
- connection,
- last seen,
- setup state.

Tabs:

1. Apps
2. Setup
3. Signing
4. Activity
5. Diagnostics

Apps tab:

- Exactly three slots.
- Empty slot: `Install catalog app`.
- Filled slot: app icon/name/bundle/version, expiry, last refresh, last install.
- Full slots: explain free-tier limit.

Signing tab:

- Apple team.
- Certificate identity and expiry.
- App IDs used by this phone.
- Profiles per installation.
- Single-flight signer status.

Setup tab:

- phone trust state,
- Developer Mode state if known,
- profile trust prompt state if known,
- Wi-Fi pairing history,
- suggested next physical action.

### Scene 9: Renewals

Renewals should be installation-centric.

Lanes:

- Blocked
- Due now
- Upcoming
- Healthy

Each item:

- app + phone,
- expires in,
- team,
- queue status,
- last attempt,
- blocker,
- action.

Show single-flight signer state:

- `Signer idle`
- `Signing Cert Clock on Dragos iPhone`
- `2 queued behind current signing job`

### Scene 10: Diagnostics

Diagnostics should have two levels:

1. Highlights: grouped actionable issues.
2. Advanced log tail: raw evidence for operators.

Highlight examples:

- Anisette identity untrusted
- Apple 2FA required
- Device unreachable
- Slot full
- Certificate cap reached
- Signing failed
- Install failed
- App launch check failed

Each issue opens detail:

- affected user/phone/app,
- operation timeline,
- suggested next action,
- linked logs,
- trace ID when available.

## Page-Level Wireflow

### Setup Center

Desktop:

- Header: compact readiness strip.
- Left: home server checklist.
- Right: iPhone/user checklist.
- Footer: primary next action.

Mobile:

- Segmented control: `Server` / `iPhone`.
- Sticky bottom action.

### Devices

Desktop table columns:

- Phone
- Owner
- Connection
- Setup
- Registered apps
- Nearest registered expiry
- Last seen
- Health

Mobile cards:

- grouped by blocker,
- primary action visible,
- no horizontal table.

### App Catalog

Desktop table/card hybrid:

- App
- Bundle ID
- Version
- Source
- Installed on
- Last inspected
- Actions

First empty state:

`Cert Clock is ready as a test app. Install it on your first phone to validate signing end to end.`

### Install Wizard

Keep a left step rail on desktop:

1. App
2. Phone
3. Team
4. Preflight
5. Install
6. Verify

On mobile, use full-width steps and a sticky footer.

## Backend/API Implications

Current API is enough to show live reachability, installed-app snapshots when a
device is readable, catalog IPA inspection, and durable registered apps. It is
not enough for the full known-phone/user/install-operation workflow.

Needed endpoints:

| Need | Endpoint sketch |
| --- | --- |
| persisted known phones | `GET/POST /api/devices/known` |
| phone detail | `GET /api/devices/{udid}` |
| installed app snapshot | `GET /api/devices/{udid}/installed-apps` implemented |
| app catalog | `GET /api/catalog/apps` implemented |
| IPA inspection | `POST /api/catalog/apps/inspect` implemented |
| catalog seed | Configured Cert Clock seed path implemented |
| installations | `GET/POST /api/installations` |
| install preflight | `POST /api/installations/preflight` |
| operation timeline | `GET /api/operations/{operationId}` |
| renewal queue | `GET /api/renewals` |
| Apple teams | `GET /api/apple/teams` |
| users/roles | `GET /api/me`, `GET/POST /api/users` |
| diagnostics issues | `GET /api/diagnostics/issues` |

Recommended read model split:

- CatalogApp: app definition and IPA source.
- Device: persisted phone record.
- Installation: app-on-phone slot record.
- Operation: one sign/install/refresh attempt.

## Validation Prototype Stories

Before implementation, create Storybook stories for:

1. Setup Center: new server, no owner/phone/app.
2. Setup Center: server ready, iPhone steps pending.
3. Devices: known offline phone.
4. Devices: one reachable phone, zero registered apps.
5. App Catalog: Cert Clock available, not installed.
6. Install wizard: Cert Clock -> reachable iPhone -> team -> preflight ready.
7. Install wizard: blocked by phone locked.
8. Install wizard: blocked by slot full.
9. Pipeline: signing/install running.
10. Pipeline: waiting for iPhone profile trust.
11. Device detail: Cert Clock installed in slot 1.
12. Renewals: due soon and queued behind single-flight signer.
13. Diagnostics: install failed highlight plus advanced log tail.

## Implementation Slice After Validation

Do not implement the whole redesign at once. First slice status:

1. Rename navigation: `Add App` -> App Catalog/Install entry points. Done.
2. Add App Catalog page with Cert Clock seed from the live catalog endpoint. Done.
3. Add backend IPA inspection and durable catalog store. Done.
4. Add registration wizard backed by durable `/api/apps` writes and server-side slot/bundle validation. Done.
5. Add installed-app-vs-registered-app distinction. Done for reachable devices.
6. Persisted known-phone empty/known-offline states remain deferred until `GET/POST /api/devices/known` exists.
7. Signing preflight, install verification, operation timelines, renewal queue, and users/roles remain deferred until their endpoints exist.

The next approval boundary is signing/install: runtime UI should not claim to
install, verify, queue, or refresh until the backend exposes preflight and
operation timeline endpoints for those actions.