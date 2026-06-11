# Sideport UI Data Contract

Date: 2026-06-07

This document defines the read models the Sideport admin UI may use during the mock-first phase. It deliberately separates current API truth from fixture-only product shape so the UI can be polished without pretending unsupported backend behavior exists.

## Source Labels

Every UI field should be tagged in fixtures, component docs, or implementation comments as one of:

- `live`: available from the current Sideport HTTP API.
- `derived`: computed from live data without extra backend support.
- `mock`: fixture-only for the first UI prototype.
- `planned`: requires a future backend endpoint or OpenTelemetry store.

## Current Live API

| Endpoint | UI use | Notes |
| --- | --- | --- |
| `GET /healthz` | Cheap liveness status | Public probe. |
| `GET /readyz` | System readiness | Reports anisette and signer checks. |
| `GET /api/anisette/info` | Trusted anisette diagnostics | Requires API bearer token when configured. |
| `GET /api/devices` | Currently reachable devices | No persisted last-seen or offline inventory yet. |
| `GET /api/apps` | Registered apps and last refresh state | In-memory registry/state today. |
| `POST /api/apps` | Register app | Server path/manual IDs only; no upload/inspection endpoint yet. |
| `DELETE /api/apps/{udid}/{bundleId}` | Remove registration | Does not uninstall from device. |
| `POST /api/apps/{udid}/{bundleId}/refresh` | Manual refresh | Mutating, bearer-protected, single-flight; keep disabled/mock in prototype. |

## View Models

### SystemStatus

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `api.ok` | boolean | live | `/healthz` result. |
| `ready.ready` | boolean | live | `/readyz` aggregate readiness. |
| `ready.checks.anisette.ok` | boolean | live | Anisette reachable and provisioned enough for client info. |
| `ready.checks.signer.ok` | boolean | live | Signer binary exists at configured path. |
| `apiAuth.configured` | boolean | mock/planned | Whether `SIDEPORT_API_TOKEN` is configured. Current UI can infer only from API behavior unless exposed. |
| `scheduler.enabled` | boolean | mock/planned | No status endpoint yet. |
| `observability.exporter` | string | mock/planned | OpenTelemetry exporter status. |

### DeviceSummary

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `udid` | string | live | Device UDID. Use fake UDIDs in stories/screenshots. |
| `name` | string | live | Device name from lockdown. |
| `productType` | string | live | Apple product type. |
| `osVersion` | string | live | iOS version. |
| `connection` | `usb | wifi` | live | Current connection. |
| `lastSeenAt` | datetime | mock/planned | Requires persistent device observation store. |
| `lastConnection` | `usb | wifi | offline` | mock/planned | Requires observation history. |
| `health` | `healthy | warning | blocked | failed | offline` | derived/mock | Derived from readiness, registration, expiry, and diagnostics. |
| `appSlotsUsed` | number | derived | Count registered apps for this UDID, capped visually at 3. |
| `nearestExpiryAt` | datetime | derived/mock | Minimum registered app expiry when available. |

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
| `displayName` | string | mock/planned | Requires IPA inspection or installed app snapshot endpoint. |
| `version` | string | mock/planned | Requires IPA inspection or installed app snapshot endpoint. |
| `iconUrl` | string? | mock/planned | Requires upload/library/IPA extraction support. |

### AppSlot

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `slotIndex` | `1 | 2 | 3` | derived | Sideport UI representation of Apple free-tier app capacity. |
| `state` | `empty | filled | expiring | failed` | derived/mock | Derived from registered apps and fixture states. |
| `app` | `RegisteredAppSummary?` | derived | Registered app assigned to this visual slot. |
| `blocker` | string? | mock/planned | Explains why adding/refreshing is blocked. |

### RenewalItem

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `id` | string | derived/mock | `deviceUdid:bundleId`. |
| `deviceUdid` | string | live | From registered app. |
| `bundleId` | string | live | From registered app. |
| `teamId` | string | live | From registered app. |
| `risk` | `blocked | due-now | upcoming | healthy | unknown` | derived/mock | Derived from expiry and last error. |
| `status` | `idle | running | queued | failed | blocked` | mock/planned | Durable queue endpoint does not exist yet. |
| `blocker` | string? | live/mock | Live from `lastError`, richer categories planned. |
| `operationId` | string? | mock/planned | Requires refresh event store/OpenTelemetry correlation. |

### DiagnosticIssue

| Field | Type | Source | Description |
| --- | --- | --- | --- |
| `id` | string | mock/planned | Future diagnostics/event store ID. |
| `category` | string | mock/planned | Examples: invalid signature, install failed, anisette unavailable, Apple rate limit. |
| `severity` | `info | warning | error | fatal` | mock/planned | Diagnostic grouping severity. |
| `status` | `unresolved | investigating | resolved | ignored` | mock/planned | Triage status. |
| `deviceUdid` | string? | mock/planned | Device involved in operation. |
| `bundleId` | string? | mock/planned | App involved in operation. |
| `firstSeenAt` | datetime | mock/planned | Requires event history. |
| `lastSeenAt` | datetime | mock/planned | Requires event history. |
| `operationId` | string | mock/planned | Stable Sideport operation ID. |
| `traceId` | string | mock/planned | OpenTelemetry trace ID. |
| `spanSummary` | array | mock/planned | Span timeline for auth/sign/install/ready checks. |
| `logSnippet` | string | mock/planned | Redacted structured log excerpt. |

## Mock Fixture Rules

- Use fake Apple IDs, fake UDIDs, and fake tokens in all fixtures and screenshots.
- Do not include real anisette identifiers, real device IDs, or real trace/log payloads.
- Mock refresh/install/sign actions must not call live endpoints.
- Any UI section that depends on a planned model should label the state as mocked in developer stories and avoid promising live behavior in user-facing copy.

## Backend Follow-Up Endpoints

These endpoints are candidates after the mock UI proves the shape:

- `GET /api/inventory/devices`: persistent device inventory with last-seen history.
- `GET /api/devices/{udid}/apps`: installed app snapshot and signature expiry per app.
- `GET /api/renewals`: renewal risk and single-flight queue status.
- `GET /api/operations`: refresh/sign/install operation history.
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