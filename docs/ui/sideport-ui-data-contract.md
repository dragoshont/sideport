# Sideport UI Data Contract

Date: 2026-07-11
Status: UI read-model context; backend truth remains `docs/sideport-backend-contract.md`

This document records the original prototype-first UI read model. The authoritative
cross-tier backend contract is now `docs/sideport-backend-contract.md`. Keep this
file as UI-model context only; when API truth changes, update the backend
contract first.

## Source Labels

Every UI field should be tagged in fixtures, component docs, or implementation comments as one of:

- `live`: available from the current Sideport HTTP API.
- `derived`: computed from live data without extra backend support.
- `demo`: fixture-only for Storybook/prototype states.
- `planned`: requires a future backend endpoint or OpenTelemetry store.

Evidence origin is a separate axis and must never be encoded as a source label.
Onboarding evidence may originate from `system`, `apple`, `device`, `artifact`,
`operation`, or `operator`. For example, an operator acknowledging Developer
Mode is still a `live` workflow record with `evidenceOrigin=operator`; it is not
device-verified evidence.

## Current Live API

| Endpoint | UI use | Notes |
| --- | --- | --- |
| `GET /healthz` | Cheap liveness status | Public probe. |
| `GET /readyz` | Cheap process readiness | Shallow public probe; operational dependency truth comes from `/api/system/status`. |
| `GET /api/system/status` | Operational setup/install checks | Authoritative protected checks for durable state, work storage, anisette, signer, operation storage, and device transport. |
| `GET /api/scheduler/status` | Scheduler policy, eligibility, and next evaluation | Durable scheduler truth and bounded history. |
| `PUT /api/scheduler/settings` | Enable/update automatic due-only refresh | Protected mutation; first-run finalization enables the default hourly policy. |
| `GET /api/anisette/info` | Trusted anisette diagnostics | Requires API bearer token when configured. |
| `GET /api/apple-access/personal/status` | Redacted Apple account, team, and credential-entry capability | Never returns a password or reusable secret. |
| `POST /api/apple-access/personal/connect` | Store and validate the first managed Apple credential | Available only on a protected HTTPS or explicitly allowed loopback request. |
| `POST /api/apple-access/personal/2fa` / `PUT /api/apple-access/personal/team` | Complete 2FA and persist an Apple-returned team | Challenge and team selection remain server-authoritative. |
| `GET /api/devices` | Currently reachable devices | Reachability does not enroll or accept a device. |
| `GET/POST/PATCH/DELETE /api/devices/known` | Durable device inventory | Stores acceptance evidence and last-known observations without turning passive discovery into enrollment. |
| `POST /api/devices/enrollments` | Add iPhone session | Durable bounded wait/pair/Trust/verify/accept operation. |
| `GET /api/devices/{udid}/installed-apps` | Read-only installed-app matching | Sideport cannot copy an IPA back from the phone. |
| `GET /api/v2/catalog/apps` | Path-free managed app library | Durable inspected artifacts used by the approved picker. |
| `GET /api/v2/catalog/import-roots` | Configured storage choices | Returns IDs and labels, never host paths. |
| `POST /api/v2/catalog/apps/upload` / `POST /api/v2/catalog/apps/inspect` | Upload or configured-root import | Produces a managed, inspected, path-free catalog result. |
| `/api/v2/catalog/github/*` | Public/private selected-repository release discovery and import | Redacted source status, allowlisted release assets, ephemeral credentials, and managed inspection. |
| `GET /api/catalog/apps` | Server-side IPA catalog | Durable JSON catalog. |
| `POST /api/catalog/apps/inspect` | Inspect/store server-side IPA path | Advanced server-local import path. |
| `POST /api/catalog/apps/upload` | Upload, inspect, and persist an IPA | Live multipart browser import; size/media/conflict validation applies. |
| `GET /api/apps` | Registered apps and last refresh state | Durable active and pending-install registrations. |
| `POST /api/apps` | Save a registration | V2 catalog/account/device selection creates a durable `pending-install` registration; legacy manual input remains compatibility-only. |
| `DELETE /api/apps/{udid}/{bundleId}` | Remove registration | Does not uninstall from device. |
| `POST /api/apps/{udid}/{bundleId}/verify` | Migrate a legacy active registration | Queued read-only bundle/version/profile verification; never pairs, signs, or installs. |
| `POST /api/apps/{udid}/{bundleId}/refresh` | Manual refresh | Compatibility path; new UI should prefer operations. |
| `POST /api/operations/preflight` | Refresh or install readiness check | Install requests bind device, bundle, catalog artifact, account profile, and `finishOnboarding` into the expiring semantic plan. |
| `POST /api/operations/install` | Durable first or later install | Runs sign/install/verify; first run also performs ordered finalization. |
| `POST /api/operations/refresh` | Durable refresh operation | Preferred refresh path for UI. |
| `GET /api/operations` / `GET /api/operations/{id}` | Operation history and resume | Durable operation records, stages, recovery capabilities, and reload-safe polling. |
| `POST /api/operations/{id}/reconcile` | Verify an unknown install/refresh outcome | Creates a linked verify-only operation; never pairs, signs, or installs. |
| `POST /api/operations/{id}/retry` | Recovery-safe retry | Enrollment recovery verifies existing Trust before any new pairing request. |
| `GET /api/onboarding/status` | Six-step workflow and completion state | Returns selected app, active operations, workflow V2, current readiness, and immutable receipt. |
| `POST /api/onboarding/complete` | Resume verified finalization | Idempotently finishes activation/scheduler/receipt work without reinstalling. |
| `GET /api/renewals` | Renewal risk lanes | Derived from registrations, refresh state, and durable operation records. |
| `GET /api/diagnostics/issues` / `PATCH /api/diagnostics/issues/{id}` | Durable grouped failure evidence | Triage is live; OpenTelemetry trace/span enrichment remains a separate concern. |

The general reconciliation endpoint and the legacy-registration verifier have
different scopes. Reconciliation checks an unknown mutating operation and is
offered only when the workflow returns that recovery action. Legacy verification
checks an active registration that lacks durable device evidence.

## View Models

### OnboardingWorkflowV2

The production admin UI consumes this live read model from
`GET /api/onboarding/status`. Phase 1 Storybook stories use the same shape as a
deterministic demo fixture and make no live calls. The authoritative endpoint,
error, persistence, and migration behavior is defined in the backend contract.

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `schemaVersion` | `2` | live/demo | Versioned workflow shape. |
| `setupState` | `in-progress | complete` | live/demo | Durable historical setup state. `complete` requires an immutable completion receipt. |
| `readyNow` | boolean | live/demo | Current operational readiness; it may regress without erasing completed setup history. |
| `completedAt` | datetime? | live/demo | Receipt timestamp after the full path completes; otherwise `null`. |
| `verifiedOperationId` | string? | live/demo | Durable verified install operation from the receipt; otherwise `null`. |
| `nextAction` | `{ stepId, action, label }?` | live/demo | At most one current action. It is `null` while the first incomplete step is automatically in progress and after completion. Preflight review and guided iPhone tasks remain details inside Install/Ready, not separate steps. |
| `steps` | `OnboardingWorkflowStep[]` | live/demo | Ordered workflow state with blockers and evidence. |

`OnboardingWorkflowStep.state` is `not-started | action-required | in-progress |
complete | blocked`. Step IDs are `server | apple-signer | device | app |
install | ready`. Scheduler enablement and receipt creation are finalization
states within `install`; `ready` is complete only after the immutable receipt
exists. Each step carries `source`, `checkedAt`, `reason`,
optional `evidenceOrigin`, evidence, optional `activeOperationId`, and at most
one capability-driven next action. `activeOperationId` lets the UI resume a
pre-UDID enrollment after reload through `GET /api/operations/{id}`. The UI maps these values onto
the existing neutral, warning, running, healthy, and blocked treatments; it
does not create a second status vocabulary.

Fresh-install fixtures contain no Apple account/team/signing identity, accepted
device, catalog artifact, registration, install operation, verification, or
completion receipt. Scheduler state is disabled. Successful empty responses are
still `demo` or `live`, never `planned`, and no count is shown before its source
has resolved.

The six-step Storybook workflow remains a demo fixture, while the production
runtime binds the live V2 workflow, one-action enrollment, durable pending app
selection, install, and ordered finalization endpoints. Demo stories must not
claim that they changed Apple, device, scheduler, or deployment state.

### DeviceEnrollmentOperation

The live `POST /api/devices/enrollments` contract is one authenticated,
user-started Add iPhone session. The same operation powers first-run setup and
**Devices → Add iPhone**; passive discovery never creates this model.

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `operationId` | string | live/demo | Durable enrollment operation; also exposed as the device step's `activeOperationId`. |
| `status` | `waiting | running | blocked | failed | succeeded | recovery-required` | live/demo | Current durable outcome. Waiting does not occupy the install/refresh worker. |
| `stage` | `wait-for-usb | request-pairing | await-user-trust | verify-lockdown | accept-device` | live/demo | Raw stage; plain UI maps it to Waiting, Trust on iPhone, Checking, Adding, or Ready. |
| `expiresAt` | datetime | live/demo | Five-minute enrollment-session bound. |
| `selectedDeviceUdid` | string? | live/demo | Null until one eligible USB candidate is selected. |
| `candidateDevices` | `{ udidSuffix, name, productType, osVersion, connection }[]` | live/demo | Safe candidate summaries only when `device-selection-required`; never include a full UDID in plain UI. |
| `reason` | string? | live/demo | Structured terminal reason such as Trust denial, lock, disconnect, timeout, or recovery required. |

Zero candidates waits. Multiple candidates terminate blocked before pairing;
the user's selection starts a new request with the chosen full UDID and a new
idempotency key. Intermediate stages have no next action. Trust is handled on
the iPhone, and Sideport verifies and accepts automatically. Developer Mode is
shown after acceptance on the same screen as guided-only steps: Settings →
Privacy & Security → Developer Mode → turn on → Restart → unlock → tap
Enable/Turn On → passcode → reconnect if needed.

If an attempt becomes `recovery-required` after pairing was requested, the UI
retains that operation and calls `POST /api/operations/{id}/retry`. The retry
checks existing Trust first and does not start a fresh pairing request.

### CatalogAppSummary

The app picker defaults to the live V2 Sideport library. Configured GitHub
sources and release candidates are live discovery metadata; an asset joins the
selectable library only after the server downloads, persists, and inspects it.
The current three-card Storybook fixture is demo deployment context, not a
hard-coded product count.

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `id` | string | live/demo | Stable catalog ID. |
| `name` | string | live/demo | Display name from inspected IPA metadata or reviewed catalog metadata. |
| `purpose` | string | live/demo | One short description shown in the picker. |
| `bundleId` | string? | live/demo | Authoritative only after IPA inspection. |
| `version` | string? | live/demo | IPA short version/build; GitHub release tags are provenance, not app versions. |
| `status` | `ready | missing | invalid | importing` | live/demo | Only `ready` artifacts are selectable for install. |
| `artifactSources` | `{ kind, label, repository?, releaseTag?, assetName? }[]` | live/demo | Separate provenance from contract truth; one app may exist on this server and in a configured GitHub release. |
| `icon` | string? | demo/planned | Trusted extracted catalog asset. Null renders a generated initial/tone; never load arbitrary remote SVG/HTML. |

The secondary **Already on this iPhone** list uses installed-app metadata only.
A matching bundle ID points back to a ready library item. An unmatched app is
read-only and says **IPA file needed** because Sideport cannot copy or sign an
installed app directly from the phone.

### OnboardingInstallIntent

The one **Install and finish** action submits an install preflight and operation
whose semantic plan and durable intent both include `finishOnboarding=true`.
The flag is not a client-only convenience and cannot be added after the plan is
confirmed.

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `catalogAppId` | string | live/demo | Exact inspected artifact selected by the operator; disambiguates catalog entries that share a bundle ID. |
| `accountProfileId` | string | live/demo | Redacted server-side Apple account profile bound to the pending registration and install plan. |
| `preflightId` | string | live/demo | Short-lived server preflight used inside the Install step; it is not a separate workflow stage. |
| `planVersion` | string | live/demo | Digest of target, limits, warnings/mutations, and `finishOnboarding`; any semantic change requires fresh confirmation in place. |
| `finishOnboarding` | `true` | live/demo | Persisted before side effects and authorizes only the fixed post-verification finalizer for this first-run operation. |
| `stage` | operation stage | live/demo | Real durable stage; after install it advances `verify → activate-registration → enable-scheduler → compute-next-evaluation → write-completion-receipt`. |
| `retryFinalization` | boolean | live/demo | Derived from workflow `nextAction=retry-finalization` only after device verification is durable; retry resumes the first unfinished idempotent boundary and cannot sign, upload, install, or re-verify. A durable operation may remain `waiting` while this operator action is required. |
| `completionReceipt` | object? | live/demo | Immutable, non-secret evidence written last. Until present, the UI remains on Install and must not show Ready. |

Automatic refresh uses the hourly due-only policy by default. A saved pairing
may support later refresh over the same Wi-Fi network, but current wireless bulk
upload is unreliable; USB is required for pairing/first install and is the
immediate fallback for a stalled or ambiguous Wi-Fi refresh.

### SystemStatus

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `api.ok` | boolean | live | `/healthz` result. |
| `ready.ready` | boolean | live | Shallow `/readyz` process readiness; never substitute it for operational checks. |
| `operational` | boolean | live | Aggregate from `/api/system/status`; true only when all required runtime checks pass. |
| `checks` | `{ id, status, checkedAt, scope, affectedResources, reason, nextAction? }[]` | live | Operational truth for durable state/work storage, anisette, signer, operation storage, and device transport. |
| `apiAuth.configured` | boolean | live/derived | Server workspace/mutation authority plus current authenticated client posture. |
| `scheduler.enabled` | boolean | live | Durable value from scheduler/onboarding status; detailed policy and next evaluation come from `/api/scheduler/status`. |
| `observability.exporter` | string | live/planned | API log ring buffer is live; OTLP/exporter health is planned. |

### DeviceSummary

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `udid` | string | live | Device UDID. Use fake UDIDs in stories/screenshots. |
| `name` | string | live | Device name from lockdown. |
| `productType` | string | live | Apple product type. |
| `osVersion` | string | live | iOS version. |
| `connection` | `usb | wifi` | live | Current connection. |
| `inventoryState` | `discovered | legacy-unverified | accepted` | live/demo | Durable acceptance posture from known-device inventory. Manual records and migration never imply acceptance. |
| `acceptedAt` | datetime? | live/demo | Written only by a successful enrollment operation. |
| `acceptedBy` | string? | live/demo | Redacted actor reference for the successful enrollment. |
| `enrollmentOperationId` | string? | live/demo | Durable evidence linking acceptance to the enrollment operation. |
| `trustState` | `trusted | untrusted | locked | error | unknown` | live/demo | Current lockdown observation; enumeration alone is never trust. |
| `trustReason` | string? | live/demo | Plain or technical reason for the current trust state. |
| `lockdownCheckedAt` | datetime? | live/demo | Time of the last non-pairing trust check. |
| `usableForInstall` | boolean | live/derived/demo | Current trusted transport is usable for an install. |
| `supportedForFirstInstall` | boolean | live/derived/demo | True only for a current trusted USB connection. |
| `lastSeenAt` | datetime | live/derived/demo | Durable known-device observation when present; otherwise the current reachability poll is clearly identified as such. |
| `lastConnection` | `usb | wifi | offline` | demo/planned | Requires observation history. |
| `health` | `healthy | warning | blocked | failed | offline` | derived/demo | Derived from readiness, registration, expiry, and diagnostics. |
| `appSlotsUsed` | number | derived | Count registered apps for this UDID, capped visually at 3. |
| `nearestExpiryAt` | datetime | derived/demo | Minimum registered app expiry when available. |

### RegisteredAppSummary

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `bundleId` | string | live | Registered bundle ID. |
| `deviceUdid` | string | live | Device registration target. |
| `appleId` | string | live | Redact in screenshots/logs. |
| `teamId` | string | live | Apple Developer Team ID. |
| `expiresAt` | datetime? | live | Current last-known profile/cert expiry from refresh state. |
| `timeUntilExpiry` | duration? | live | Current backend-computed remaining time. |
| `lastSucceeded` | boolean? | live | Last refresh result if known. |
| `lastError` | string? | live | Last refresh/install/signing error if known. |
| `displayName` | string | derived/live | Derived from catalog/IPA metadata when available, otherwise bundle ID fallback. |
| `version` | string | derived/live | Derived from catalog/IPA metadata when available, otherwise unknown fallback. |
| `iconUrl` | string? | demo/planned | Requires upload/library/IPA extraction support. |

### AppSlot

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `slotIndex` | `1 | 2 | 3` | derived | Sideport UI representation of Apple free-tier app capacity. |
| `state` | `empty | filled | expiring | failed` | derived/demo | Derived from registered apps and fixture states. |
| `app` | `RegisteredAppSummary?` | derived | Registered app assigned to this visual slot. |
| `blocker` | string? | derived/planned | Explains why adding/refreshing is blocked. |

### RenewalItem

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `id` | string | live | `deviceUdid:bundleId` from `/api/renewals`. |
| `deviceUdid` | string | live | From registered app. |
| `bundleId` | string | live | From registered app. |
| `teamId` | string | live | From registered app. |
| `risk` | `blocked | due-now | upcoming | healthy | unknown` | live/derived | API-derived from expiry, refresh state, and latest durable operation. |
| `status` | `idle | running | queued | failed | blocked` | live | `queued` is reserved for a future background worker; do not invent queued rows. |
| `blocker` | string? | live/derived | Live from operation error/refresh state; richer categories planned. |
| `operationId` | string? | live | Latest durable operation ID when available. |

### DiagnosticIssue

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `id` | string | derived/planned | Admin-synthesized issue ID today; durable issue-store ID later. |
| `category` | string | derived/planned | Derived from API failures/app errors today; richer diagnostics later. |
| `severity` | `info | warning | error | fatal` | derived/planned | Derived severity today; durable triage later. |
| `status` | `unresolved | investigating | resolved | ignored` | derived/planned | Derived unresolved state today; durable triage later. |
| `deviceUdid` | string? | derived/planned | Device involved in operation when known. |
| `bundleId` | string? | derived/planned | App involved in operation when known. |
| `firstSeenAt` | datetime | derived/planned | Current snapshot time today; event history later. |
| `lastSeenAt` | datetime | derived/planned | Current snapshot time today; event history later. |
| `operationId` | string | derived/live/planned | Latest operation when available; otherwise a derived placeholder until trace-backed diagnostics exist. |
| `traceId` | string | planned | OpenTelemetry trace ID later. |
| `spanSummary` | array | derived/planned | Placeholder span summary today; real spans later. |
| `logSnippet` | string | derived/planned | Redacted API/app error text today; structured logs later. |

## Demo Fixture Rules

- Use fake Apple IDs, fake UDIDs, and fake tokens in all fixtures and screenshots.
- Do not include real anisette identifiers, real device IDs, or real trace/log payloads.
- Demo refresh/install/sign actions must not call live endpoints.
- Any UI section that depends on a planned model should label the state as demo
  or planned in developer stories and avoid promising live behavior in
  user-facing copy.

## Remaining Backend Follow-Up Endpoints

The Phase 6 onboarding, managed Apple credential, device enrollment/inventory,
V2 catalog/GitHub, install/finalization, system-status, scheduler, and
diagnostics endpoints above are live. Remaining UI-model candidates include:

- `GET /api/teams`: consolidated Apple Developer team read model beyond the
  selected Personal Apple account status.
- `GET /api/settings/status`: consolidated read-only admin settings view beyond
  the existing system, scheduler, workspace, and connector status endpoints.
- Trace/span query support for durable diagnostic evidence; do not invent trace
  IDs until an actual OpenTelemetry store exposes them.

## OpenTelemetry Shape

Every refresh-related operation should eventually create one trace with spans for:

1. `sideport.refresh.request`
2. `sideport.auth.grandslam`
3. `sideport.anisette.headers`
4. `sideport.developer-services.profile`
5. `sideport.signer.prepare-identity`
6. `sideport.signer.process`
7. `sideport.device.install`
8. `sideport.refresh.record-state`

The UI should display trace IDs as evidence links, not as decorative developer noise. A trace belongs in the UI only when it helps explain a blocker or failure.
