# Sideport End-to-End Install and Refresh Plan

Date: 2026-06-09
Status: research-backed implementation inventory

For Apple account connector options and pilot strategy, see
`docs/ui/sideport-apple-access-options.md`.

## Product Promise

Sideport should take a blank portal to one installed, signed IPA on a trusted iPhone, then keep that IPA refreshed automatically during two daily windows: 4:00 AM and 7:00 PM in the configured home timezone.

The portal must be unusually easy to use without becoming vague. Every fact that Sideport can detect automatically must be shown as detected. Every operator-entered value must stay clearly manual. Every Apple/iPhone action that cannot be verified yet must be marked guided.

## Research Inputs

- Apple HIG onboarding: teach through interaction, keep prerequisite onboarding brief, show context-specific tips near the task, postpone nonessential setup.
- Apple signing flow: App ID, development certificate, registered device, provisioning profile, then sign/run on device. Account Holder/Admin roles are required for key developer-account mutations.
- Xcode signing workflow: add Apple ID, choose team, Xcode manages App IDs/certs/profiles, run on device, export signing assets because private keys live locally.
- Apple platform deployment and MDM tools: device inventory, owner/assignment, last seen, status, repair actions, audit trails.
- Sideport current implementation: live readiness, reachable devices, installed-app snapshots, catalog IPA inspection, durable catalog entries, durable app registrations, synchronous refresh/install loop, logs.

## Current State

Implemented and usable:

- API readiness: `GET /readyz` verifies anisette reachability and signer path.
- Device discovery: `GET /api/devices` returns currently reachable iPhones.
- Installed app facts: `GET /api/devices/{udid}/installed-apps` returns app list and profile-derived expiry when readable.
- Catalog: `GET /api/catalog/apps` and `POST /api/catalog/apps/inspect` inspect server-side IPA paths and store catalog entries.
- Registration: `POST /api/apps` stores durable app registrations and validates IPA existence, inspected bundle ID, and 3-registration per-device cap.
- Refresh loop: `POST /api/apps/{udid}/{bundleId}/refresh` synchronously runs auth -> signing asset preparation -> sign -> install, returning success/error.
- Logs: `GET /api/logs` exposes API/runtime log tail.

Not complete for the product promise:

- No browser IPA upload/import, only server path inspection.
- No persisted known-phone inventory, only reachable devices.
- No Apple account setup UI, team picker, or 2FA flow exposed in the portal.
- No signing/install preflight endpoint.
- No staged operation timeline or durable operation history.
- No scheduler status/settings endpoint and no 4 AM/7 PM wall-clock policy.
- No safe cutover gate from AltServer or another signer.
- No structured diagnostics issues beyond logs and derived API failures.
- No users/roles beyond external auth/token posture. Sign in with Apple is optional portal identity only, not Apple Developer access.

## Source Labels

Use these labels consistently on fields and rows:

| Label | Meaning | Examples |
| --- | --- | --- |
| Live | Returned by an endpoint in the current snapshot | API ready, reachable device, installed apps |
| Stored | Durable Sideport data | catalog record, app registration, known phone later |
| Detected from IPA | Extracted from IPA inspection | bundle ID, display name, version, checksum, embedded profile |
| Detected from iPhone | Read from device services | UDID, product type, iOS version, installed app expiry |
| Apple Developer | Read from Apple developer services | teams, role, App ID, cert, profile, registered device |
| Operator | Entered by the portal user | friendly phone name, owner, schedule, notes |
| Derived | Computed from other facts | slot pressure, next refresh, readiness state |
| Guided | Physical/user action Sideport cannot verify yet | Developer Mode, profile trust, phone unlocked |
| Proposed | Designed UI waiting for backend | users, operation queue before endpoint exists |

Rules:

- Never show an automatic fact as operator-entered or vice versa.
- Never overwrite operator labels when detected values change; show a diff and ask whether to update the friendly label.
- Empty live results are still live, not planned. For example: `0 installed apps returned` is a live result if the endpoint succeeded.
- `Unknown` must include a reason: not checked, endpoint failed, device offline, permission missing, or backend not implemented.

## Empty Portal to Signed IPA Flow

### 1. Setup Center

Goal: let the user progress by doing real tasks, not reading a tutorial. The UI should be a focused next/back setup wizard, with one decision surface visible at a time and the full checklist available as navigation.

Server lane:

1. API reachable.
2. API protected for mutation actions.
3. Anisette identity reachable.
4. Signer executable present and runnable.
5. State directory writable.
6. Work directory writable.
7. Scheduler disabled until cutover is acknowledged.

Apple lane:

1. Optional Sign in with Apple for Sideport identity, never required for signing.
2. Personal Apple ID connector selected for free-account signing.
3. Apple ID credential/session custody understood.
4. Apple authentication status known.
5. 2FA challenge state if needed.
6. Teams fetched from Apple when connector supports it.
7. Selected team stored.
8. Role/capability constraints displayed.
9. Existing signing identity reuse/mint posture shown.

Device lane:

1. Detect reachable iPhone.
2. Show physical trust guidance when no device is visible.
3. Register known-phone record.
4. Read installed-app snapshot if possible.
5. Mark Developer Mode/profile trust as guided unless detectable.

App lane:

1. Inspect server IPA path or upload IPA.
2. Save catalog entry.
3. Choose catalog app.
4. Create app registration for one known/reachable phone.
5. Run signing/install preflight.
6. Confirm sign and install.
7. Watch operation timeline.
8. Verify installed app and expiry from device snapshot.

Completion condition: first app is installed and verified with a signature expiry, and its refresh schedule has at least one upcoming window.

### 2. Apple Account Setup

Implementation rule: build the focused setup wizard directly, but do not wire real Apple login/developer discovery until the connector is validated. Sign in with Apple belongs to portal identity; Personal Apple ID/GrandSlam or App Store Connect JWT belongs to signing access.

Current feasible backend pieces:

- `SessionManager.SignInAsync` can start authentication using configured credentials.
- `SessionManager.CompleteTwoFactorAsync` can submit a 2FA code and retry authentication.
- `AppleDeveloperPortal.ListTeamsAsync` can list teams after authentication.
- Signing preparation can register device, ensure certificate, ensure App ID, and download profile.
- Personal Apple ID connector endpoints now expose status, sign-in, 2FA completion, and team listing without accepting Apple passwords in the browser.

Portal UX:

- Apple Account page shows account rows: Apple ID, auth status, last successful auth, 2FA required, teams, selected team, capability/role warning.
- Primary action: `Check Apple account`.
- Optional action: `Start sign-in` only if a credential source exists.
- 2FA dialog appears only after backend returns a challenge. It must never ask for passwords unless the backend has a secure storage design.
- Team picker uses Apple-returned teams. Manual Team ID stays available as advanced fallback and is marked Operator.

Security boundaries:

- Do not store Apple passwords in browser session storage.
- Do not expose raw secrets through API responses or logs.
- Mutating signing actions require API auth/reverse-proxy auth to be configured.
- Certificate-minting actions require a deliberate confirmation because free-tier certificate revocation can affect AltServer or other signers.

### 3. Known Phone Inventory

Why it is required:

Automatic refreshes need to explain phones that are offline, last seen, or not trusted. A reachable-only list cannot support a professional schedule or missed-run story.

Backend model:

- `KnownDevice`: UDID, management name, detected name, product type, iOS version, owner, default team, first seen, last seen, last connection, trust/pairing status, last installed-app snapshot time, notes.
- `DeviceSnapshot`: installed apps, signature expiries, capture time, capture result.

Endpoints:

- `GET /api/devices/known`
- `POST /api/devices/known`
- `PATCH /api/devices/known/{udid}`
- `GET /api/devices/{udid}`
- `POST /api/devices/{udid}/refresh-snapshot`

UI:

- Devices table shows known phones, with reachable state as one column.
- Device detail has Setup, Apps, Signing, Activity, Diagnostics tabs.
- Empty states guide USB connection, Trust This Computer, passcode unlock, and Wi-Fi pairing.

### 4. IPA Ingestion

Current V1 is server path only. For a truly empty portal, add upload/import.

Backend:

- `POST /api/catalog/apps/upload` with multipart IPA upload.
- Store artifact under state directory or configured IPA library.
- Inspect app bundle name, display name, bundle ID, version, build, min iOS, platform, entitlements, icon, checksum, size, embedded profile, signature expiry.
- Reject unsupported platforms early.

UI:

- App Catalog action: `Import IPA`.
- Segmented import mode: `Upload file` / `Server path`.
- After inspection, show a review screen with detected facts and optional friendly catalog name.
- Bundle ID is locked after registration unless re-import creates a new version.

### 5. Registration Preflight

Current registration validates IPA path, bundle match, and 3 Sideport registrations per device. It needs a full signing preflight before install.

Endpoint:

- `POST /api/installations/preflight`

Inputs:

- catalog app id or IPA path
- known/reachable device UDID
- Apple ID
- team ID
- requested schedule policy
- install now flag

Checks:

- API auth configured.
- IPA inspected and supported.
- Bundle ID matches catalog entry.
- Device reachable or queued-for-later is supported.
- Device trusted/paired if detectable.
- 3 Sideport registration slots available.
- Installed unmanaged app with same bundle ID detected.
- Apple session valid or 2FA required.
- Team exists and role permits required actions.
- Device registered with Apple team or can be registered.
- App ID exists or can be created.
- Capabilities requested by app are supported by membership/team.
- Persisted signing certificate/private key exists or minting would be required.
- Provisioning profile can be created/downloaded.
- Signer executable runs, not merely exists.
- Work/state directories are writable.
- Cutover gate acknowledged if cert mint/revoke may affect other signers.

Output:

- `ready`, `warnings`, `blockers`, `plannedMutations`, `dataSources`, `requiresConfirmation`.

UI:

- Preflight panel grouped by Server, Apple, iPhone, IPA, Limits, Schedule.
- Warnings are visible but not conflated with blockers.
- Planned Apple mutations are explicit: register device, create App ID, mint cert, create profile.

### 6. Sign and Install Operation

The backend has synchronous refresh logic, but the portal needs durable staged operations.

Endpoints:

- `POST /api/operations/install`
- `POST /api/operations/refresh`
- `GET /api/operations/{operationId}`
- `GET /api/operations?deviceUdid=&bundleId=`

Operation stages:

1. Inspect IPA.
2. Authenticate Apple ID.
3. Select Apple team.
4. Register device if needed.
5. Ensure App ID and capabilities.
6. Ensure/reuse/mint certificate.
7. Download provisioning profile.
8. Sign IPA.
9. Install IPA.
10. Verify installed app and expiry.
11. Record schedule/next refresh.

Each stage records:

- state: pending, running, succeeded, warning, failed, skipped
- start/end timestamps
- duration
- redacted message
- recovery action
- source label

UI:

- Confirmation screen uses plain summary: `Install Cert Clock on Dragoș's iPhone using team M62Z4M5EUY. This uses Sideport slot 1 of 3.`
- Timeline updates by polling until streaming exists.
- Finish lands on device detail with the app slot filled and next refresh shown.

### 7. Refresh Schedule at 4 AM and 7 PM

Current scheduler polls hourly with a lead-time rule. The product needs visible wall-clock windows.

Policy model:

- `enabled`: bool
- `timezone`: IANA timezone, default from server/home config
- `windows`: `04:00`, `19:00`
- `leadTime`: default two days before expiry
- `mode`: due-only by default, not force-refresh every app
- `catchUp`: run missed due-only check after downtime, with a quiet-hour guard
- `singleFlight`: always true
- `retry`: retry failed job once within the same window, then mark blocked

Endpoints:

- `GET /api/scheduler/status`
- `PUT /api/scheduler/settings`
- `POST /api/scheduler/run-now`

Status output:

- enabled
- timezone
- windows
- next window
- last window
- last run outcome
- due apps
- queued apps
- running operation id
- blocked count

UI:

- Renewals header shows `Automatic refresh: enabled`, `Next check: today 7:00 PM Europe/Bucharest`.
- Settings schedule panel uses time inputs/chips for 4:00 AM and 7:00 PM and a timezone selector.
- Per-app rows show next eligible refresh and reason if skipped.
- Scheduler disabled is a setup blocker after the first install, not a permanent OK state.

### 8. Cutover and Safety

The riskiest action is certificate minting/revocation. The portal must make this impossible to do casually.

Cutover gate:

- Show Apple ID and team.
- Show persisted Sideport identity status.
- Show whether minting is needed.
- Warn that minting can revoke existing iOS development certificates and may break AltServer for that Apple ID/team.
- Ask for explicit acknowledgement: `Use Sideport as signer for this Apple ID/team`.
- Store acknowledgement with timestamp and actor.

### 9. Diagnostics and Audit

Diagnostics should be operation-shaped, not log-shaped.

Structured issue categories:

- API auth missing
- anisette unavailable
- Apple 2FA required
- Apple auth throttled
- team role insufficient
- device unreachable
- device untrusted/locked
- Developer Mode guided
- slot limit reached
- App ID limit/capability unsupported
- certificate mint/revoke required
- signer failed
- install failed
- verify failed
- scheduler disabled
- missed refresh window

Audit events:

- catalog import
- registration create/delete
- Apple account check/login
- 2FA challenge submitted, no code logged
- cutover acknowledged
- scheduler enabled/disabled/settings changed
- install/refresh operation requested
- signing identity minted/reused

## Information Architecture

Recommended nav when this is complete:

1. Setup Center, visible until complete and then accessible from Settings.
2. Overview.
3. Devices.
4. App Catalog.
5. Installations.
6. Renewals.
7. Operations.
8. Diagnostics.
9. Apple Account.
10. Settings.

Users/Roles can remain hidden or disabled until real auth exists. Do not present fake team administration.

## Screen Inventory

### Setup Center

- Readiness score and next action.
- Server checks.
- Apple account checks.
- iPhone checks.
- App/catalog checks.
- Schedule checks after first install.

### Apple Account

- Apple IDs configured.
- Auth state and 2FA state.
- Teams and selected team.
- Role/capability warnings.
- Signing identity status.
- Cutover acknowledgement.

### Devices

- Known phones table.
- Reachable state.
- Owner/friendly name.
- Trust/pairing status.
- Installed app snapshot status.
- Sideport slot use.

### Device Detail

- Header with detected and operator labels.
- Three Sideport slots.
- Installed unmanaged apps.
- Setup tab.
- Signing tab.
- Activity/operations tab.
- Diagnostics tab.

### App Catalog

- Import IPA.
- Server path or upload.
- Inspection result.
- Version history later.
- Install on phone.
- Replace source.

### Installation Wizard

- Choose app.
- Choose phone.
- Choose Apple team.
- Preflight.
- Confirm Apple mutations.
- Sign/install timeline.
- Verify.
- Schedule.

### Renewals

- Schedule status.
- Blocked/due/upcoming/healthy lanes.
- Queue/single-flight state.
- Run now with preflight.

### Operations

- Operation list.
- Operation detail timeline.
- Redacted logs.
- Retry/recover action.

### Diagnostics

- Grouped issues.
- Evidence and recovery.
- Advanced log tail.

## Implementation Plan

### Phase 0: Truth Cleanup

- Rename UI copy from install to registration wherever the action only saves `/api/apps`.
- Remove or wire inert search/refresh controls.
- Fix scheduler status mapping so UI uses backend truth instead of hard-coded planned/disabled.
- Carry endpoint success separately from empty arrays to avoid `0 live facts` looking planned.

### Phase 1: Scheduler Contract

- Add `SchedulerOptions` with timezone, windows `04:00` and `19:00`, due-only mode, catch-up, retry policy.
- Add scheduler status/settings endpoints.
- Update Renewals and Settings with real schedule status.
- Add tests for next window, DST, missed windows, disabled state, and due-only selection.

### Phase 2: Apple Account Read Model

- Run the App Store Connect JWT read-only probe first; prefer official API-key access when it covers the needed provisioning resources.
- Add auth status endpoint.
- Expose sign-in start and 2FA completion without storing browser secrets.
- Add teams endpoint and selected-team persistence.
- Add signing identity status endpoint: existing p12, certificate expiry, mint required, cutover status.
- Add UI: Apple Account page and onboarding step.

### Phase 3: Known Devices

- Add file-backed known-device store.
- Upsert reachable devices into known devices.
- Persist last seen and last installed-app snapshot.
- Add device detail endpoint and UI known/offline states.

### Phase 4: Preflight

- Add `POST /api/installations/preflight`.
- Return blockers, warnings, planned Apple mutations, and confirmation requirements.
- Add UI preflight screen and Storybook states for every blocker.

### Phase 5: Operations

- Convert synchronous refresh/install into durable operation runner with operation IDs.
- Add operation store and timeline endpoint.
- Make install/refresh endpoints return operation ID quickly.
- Poll operation detail in UI.
- Verify install by rereading installed apps after device install.

### Phase 6: IPA Upload and Catalog Polish

- Add browser upload/import.
- Extract icon, min OS, platform, entitlements.
- Add version history and replace-source workflow.

### Phase 7: Automatic Refresh Completion

- Wire scheduler to operation queue.
- Show next 4 AM/7 PM windows and due apps.
- Add run-now action with preflight.
- Add failure notification hooks later.

### Phase 8: Security and Governance

- Make API auth/proxy auth a hard blocker for real signing/install mutations.
- Add audit log events.
- Add user/role read model only when auth source is real.
- Add redaction tests.

## Storybook and Playwright Matrix

Storybook states:

- Empty portal with no API.
- API ready, no Apple account.
- Apple 2FA required.
- Team role insufficient.
- No reachable device.
- Known device offline.
- Device untrusted/locked.
- Catalog import success.
- Catalog import unsupported platform.
- Slot full.
- App ID/capability unsupported.
- Certificate mint requires cutover.
- Preflight ready.
- Install running.
- Install failed at each major stage.
- Verify succeeded with expiry.
- Scheduler disabled.
- Scheduler enabled, next 4 AM.
- Scheduler enabled, next 7 PM.
- Missed window/catch-up.

Playwright:

- Desktop and mobile screenshots for Setup Center, Apple Account, Devices, App Catalog, Wizard Preflight, Operation Timeline, Renewals Schedule, Diagnostics.
- Keyboard path through install wizard.
- Assertions that source labels are present on detected/operator/guided/proposed facts.
- Assertions that no sign/install CTA is enabled before preflight is ready.

## Open Product Decisions

1. Timezone: use `Europe/Bucharest` by default for this homelab, or detect server timezone and let the user confirm?
2. 4 AM/7 PM semantics: due-only checks or force refresh every registered app? Recommendation: due-only checks.
3. V1 IPA ingestion: is server path acceptable for first real use, or is upload required before declaring empty-portal complete?
4. Apple credential source: environment/SOPS only, or add encrypted local secret store?
5. AltServer coexistence: require explicit hard cutover per Apple ID/team before any certificate minting?
6. Notifications: email/webhook/ntfy later, or out of scope for first complete flow?

## Recommended Next Slice

Build Phase 0 and Phase 1 first.

Reason: the requested 4 AM/7 PM promise needs scheduler truth immediately, and the current UI has a known falsehood around scheduler state. This slice is bounded, testable, and improves honesty before exposing more destructive Apple-account actions.

Then build Apple Account + Preflight before any new install/sign UI. The portal should not become easier to click than it is safe to operate.
