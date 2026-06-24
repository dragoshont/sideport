# Sideport UI Data Contract

Date: 2026-06-07
Status: superseded for backend truth by `docs/sideport-backend-contract.md`

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

## Current Live API

| Endpoint | UI use | Notes |
| --- | --- | --- |
| `GET /healthz` | Cheap liveness status | Public probe. |
| `GET /readyz` | System readiness | Reports anisette and signer checks. |
| `GET /api/anisette/info` | Trusted anisette diagnostics | Requires API bearer token when configured. |
| `GET /api/devices` | Currently reachable devices | No persisted last-seen or offline inventory yet. |
| `GET /api/catalog/apps` | Server-side IPA catalog | Durable JSON catalog. |
| `POST /api/catalog/apps/inspect` | Inspect/store server-side IPA path | No browser upload yet. |
| `GET /api/apps` | Registered apps and last refresh state | In-memory registry/state today. |
| `POST /api/apps` | Register app | Server path/manual IDs only; registration does not install. |
| `DELETE /api/apps/{udid}/{bundleId}` | Remove registration | Does not uninstall from device. |
| `POST /api/apps/{udid}/{bundleId}/refresh` | Manual refresh | Compatibility path; new UI should prefer operations. |
| `POST /api/operations/preflight` | Refresh readiness check | Shows blockers, warnings, scarce limits, and planned mutations. |
| `POST /api/operations/refresh` | Durable refresh operation | Preferred refresh path for UI. |
| `GET /api/operations` | Operation history | Durable operation records and stages. |
| `GET /api/renewals` | Renewal risk lanes | Derived from registrations, refresh state, and durable operation records. |

## View Models

### SystemStatus

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `api.ok` | boolean | live | `/healthz` result. |
| `ready.ready` | boolean | live | `/readyz` aggregate readiness. |
| `ready.checks.anisette.ok` | boolean | live | Anisette reachable and provisioned enough for client info. |
| `ready.checks.signer.ok` | boolean | live | Signer binary exists at configured path. |
| `apiAuth.configured` | boolean | derived | Inferred from API behavior and client token configuration. |
| `scheduler.enabled` | boolean | planned | No status endpoint yet. |
| `observability.exporter` | string | live/planned | API log ring buffer is live; OTLP/exporter health is planned. |

### DeviceSummary

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `udid` | string | live | Device UDID. Use fake UDIDs in stories/screenshots. |
| `name` | string | live | Device name from lockdown. |
| `productType` | string | live | Apple product type. |
| `osVersion` | string | live | iOS version. |
| `connection` | `usb | wifi` | live | Current connection. |
| `lastSeenAt` | datetime | derived/planned | Current admin UI stores the fetch time as current-poll presence. Persistent last-seen history requires known-device inventory. |
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

## Backend Follow-Up Endpoints

These endpoints are candidates after the prototype UI proves the shape:

- `GET /api/inventory/devices`: persistent device inventory with last-seen history.
- `GET /api/devices/{udid}/apps`: installed app snapshot and signature expiry per app.
- `GET /api/diagnostics/issues`: grouped failures backed by event history and OpenTelemetry IDs.
- `GET /api/teams`: Apple Developer team read model.
- `GET /api/settings/status`: read-only admin settings/status for UI display.

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