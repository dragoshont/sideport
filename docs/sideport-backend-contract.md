# Sideport Backend Contract

Date: 2026-06-23
Status: authoritative for backend/UI implementation

This is the cross-tier contract for Sideport's admin UI and .NET API. It
captures what is live now, what is intentionally derived by the UI, and the next
operation/preflight endpoints that make signing and refresh work observable.

The remaining roadmap foundation is governed by
`docs/architecture/adr-0001-roadmap-foundations.md`: JSON-backed stores,
single-replica/single-flight execution, role/capability vocabulary before
operation controls, and no database/broker until multi-replica execution is a
current requirement.

## Contract Rules

- Every `/api/*` endpoint is protected by bearer token or authenticated OIDC
  session when either auth mode is configured. `/healthz` and `/readyz` stay open
  for probes.
- Secret values never cross the API boundary. The browser may send an Apple ID
  identifier and a 2FA code; passwords, API keys, private keys, and anisette
  identity material stay in server-side custody.
- Mutating endpoints must return structured failure reasons. A successful HTTP
  response means the requested state transition or operation record was accepted,
  not that a device install necessarily completed unless the endpoint says so.
- UI source labels describe provenance only: `live`, `derived`, `demo`, or
  `planned`. Empty, blocked, failed, stale, unsupported, and permission states
  are availability/status, not source labels.
- Refresh/sign/install is serialized. Sideport may expose pending/running work,
  but it must not claim parallel signing support.
- Operation records include actor/audit metadata. The actor is derived from the
  authenticated OIDC user when present, from bearer-token access as
  `api-token-client`, or from internal scheduler work as `system:scheduler` in a
  later slice.
- Operation storage is durable JSON under `Sideport:State:Directory`, written by
  atomic temp-file replace behind a process-local lock. Corrupt operation JSON is
  a readiness/diagnostic problem; the API must fail the operation-history request
  with a structured error rather than silently discarding history.
- If the API restarts or loses storage after recording a `running` operation but
  before saving its terminal state, operation-history reads reconcile running
  records older than 30 minutes to `failed` with
  `operation-terminal-state-unknown`. Operators must inspect device state before
  retrying.

## Live Endpoints

| Method | Path | Purpose | Source | Notes |
| --- | --- | --- | --- | --- |
| `GET` | `/healthz` | Process liveness | live | Open probe. |
| `GET` | `/readyz` | Anisette + signer readiness | live | Open probe. |
| `GET` | `/api/about` | Service metadata | live | Protected like other `/api/*`. |
| `GET` | `/api/me` | Current API identity mode | live | OIDC user or bearer-token client. |
| `GET` | `/api/anisette/info` | Anisette client info probe | live | No raw anisette secrets. |
| `GET` | `/api/logs?limit=` | In-process API log tail | live | Ring buffer, not durable operation history. |
| `GET` | `/api/apple-access/status` | App Store Connect read-only probe | live | Optional paid-team path. |
| `GET` | `/api/apple-access/personal/status` | Personal Apple ID connector status | live | Host-side credential custody. |
| `POST` | `/api/apple-access/personal/sign-in` | Start Personal Apple sign-in | live | Apple ID only; password from custody. |
| `POST` | `/api/apple-access/personal/2fa` | Complete pending 2FA challenge | live | Code only. |
| `GET` | `/api/devices` | Reachable device snapshot | live | Not persistent known-device inventory. |
| `GET` | `/api/devices/diagnostics` | Device transport self-test | live | Human-readable remediation. |
| `GET` | `/api/devices/{udid}/installed-apps` | Installed app snapshot | live | Only when device is reachable. |
| `GET` | `/api/onboarding/status` | First-run prerequisite checklist | live | Includes portal and guided iPhone tasks. |
| `GET` | `/api/workspace` | Workspace auth mode, current member, roles, and capabilities | live | Read-only contract; user administration is not live. |
| `GET` | `/api/catalog/apps` | Server-side IPA catalog | live | Durable JSON catalog. |
| `POST` | `/api/catalog/apps/inspect` | Inspect/store server-side IPA path | live | No browser upload yet. |
| `GET` | `/api/apps` | Registered apps + last refresh state | live | Durable registrations, process-local refresh state. |
| `POST` | `/api/apps` | Save app registration | live | Validates IPA path, bundle ID, and 3-slot limit. |
| `DELETE` | `/api/apps/{udid}/{bundleId}` | Remove registration | live | Does not uninstall from the device. |
| `POST` | `/api/apps/{udid}/{bundleId}/refresh` | Synchronous legacy refresh | live | Kept for compatibility; new UI should prefer operations. |

## Operation/Preflight Endpoints

These endpoints are the current SDD implementation target. They add operation
history and preflight around the existing synchronous refresh loop without
claiming background queue/cancel support yet. The legacy refresh endpoint remains
available, but it is explicitly a compatibility endpoint and does not create an
operation record in this first slice. Scheduler-triggered refreshes also remain
outside operation history until the scheduler is routed through an operation
service in a later slice.

### `POST /api/operations/preflight`

Request:

```json
{
  "type": "refresh",
  "deviceUdid": "000081...",
  "bundleId": "com.example.certcountdown"
}
```

Response:

```json
{
  "ready": true,
  "target": {
    "deviceUdid": "000081...",
    "bundleId": "com.example.certcountdown",
    "appleId": "developer@example.com",
    "teamId": "TEAMID1234"
  },
  "blockers": [],
  "warnings": [
    {
      "code": "device-reachability-not-verified",
      "message": "The registration exists, but the device is not known reachable in this preflight snapshot.",
      "source": "live"
    }
  ],
  "plannedMutations": [
    "Authenticate Apple ID from server-side custody",
    "Register device with Apple if needed",
    "Ensure App ID, certificate, and provisioning profile",
    "Re-sign IPA",
    "Install signed IPA on the device"
  ],
  "scarceLimits": [
    {
      "code": "free-device-app-slots",
      "label": "Free-account app slots",
      "used": 2,
      "limit": 3,
      "source": "derived"
    }
  ],
  "requiresConfirmation": true,
  "source": "live"
}
```

Rules:

- `ready=false` whenever the registration is missing, its IPA is missing, the
  bundle ID cannot be inspected, the signer is missing, or Sideport detects a
  3-slot conflict for a new registration path.
- Preflight may include warnings for conditions that cannot be proven from the
  current API snapshot, such as device reachability or Apple session freshness.
- Preflight does not perform Apple mutations.
- `requiresConfirmation=true` means the UI must show planned mutations and the
  operator must explicitly start the refresh operation. The operation endpoint
  always re-runs preflight; it does not trust a previous browser preflight.

### `POST /api/operations/refresh`

Request:

```json
{
  "deviceUdid": "000081...",
  "bundleId": "com.example.certcountdown",
  "idempotencyKey": "optional-client-key"
}
```

Response (`201 Created` for a newly recorded terminal operation, `200 OK` when an
idempotency key returns an existing operation):

```json
{
  "operationId": "op_20260623_abc123",
  "type": "refresh",
  "status": "succeeded",
  "createdAt": "2026-06-23T12:00:00Z",
  "startedAt": "2026-06-23T12:00:00Z",
  "updatedAt": "2026-06-23T12:00:05Z",
  "completedAt": "2026-06-23T12:00:05Z",
  "actor": {
    "kind": "api-token",
    "displayName": "api-token-client"
  },
  "idempotencyKey": "optional-client-key",
  "attempt": 1,
  "target": {
    "deviceUdid": "000081...",
    "bundleId": "com.example.certcountdown"
  },
  "stages": [
    {
      "id": "preflight",
      "label": "Preflight",
      "status": "succeeded",
      "startedAt": "2026-06-23T12:00:00Z",
      "completedAt": "2026-06-23T12:00:00Z",
      "message": "Ready to refresh.",
      "error": null
    },
    {
      "id": "refresh",
      "label": "Sign and install",
      "status": "succeeded",
      "startedAt": "2026-06-23T12:00:00Z",
      "completedAt": "2026-06-23T12:00:05Z",
      "message": "Refresh completed.",
      "error": null
    }
  ],
  "result": {
    "success": true,
    "bundleId": "com.example.certcountdown",
    "expiresAt": "2026-06-30T12:00:05Z",
    "error": null
  },
  "error": null,
  "cancelable": false,
  "retryable": false,
  "rerunnable": false,
  "correlationId": "op_20260623_abc123",
  "source": "live"
}
```

Rules:

- This first implementation is synchronous internally but returns a durable
  operation record. `status` is terminal by the time the HTTP response returns.
- If preflight fails, the operation is recorded with `status=blocked`, a failed
  preflight stage, and no refresh stage execution.
- If the refresh loop returns a failure, the operation is recorded with
  `status=failed`, terminal error detail, and `retryable=true` when retry would
  not be destructive.
- `cancelable=false` until Sideport has a background operation worker and a safe
  cancellation boundary.
- An idempotency key, when supplied, is matched by `(type, deviceUdid, bundleId,
  actor.kind, actor.displayName, idempotencyKey)`. If an operation already
  exists for that tuple, Sideport returns the existing record without running the
  refresh again, regardless of whether the existing record succeeded, failed, or
  was blocked. The first implementation uses a process-local lock to make
  duplicate submissions atomic within one API process.
- `attempt` is `1` for this first synchronous operation slice. Retry/rerun will
  create a new operation only after the retry contract lands.
- Stage `status` is one of `pending`, `running`, `succeeded`, `failed`, or
  `blocked`. Operation `status` is one of `running`, `blocked`, `succeeded`, or
  `failed` in this first slice.
- Each failed stage includes an `error` object with `code`, `message`, and
  optional `detail`; the top-level `error` mirrors the terminal failure.

### `GET /api/operations`

Query parameters: `deviceUdid`, `bundleId`, `limit`.

Returns most-recent-first operation records. Defaults to `limit=25`, maximum
`limit=100`. Filters are exact-match. The first implementation is durable
JSON-backed history under `Sideport:State:Directory`.

### `GET /api/operations/{operationId}`

Returns one operation record or `404`.

### `GET /api/renewals`

Returns renewal items derived from registered apps, refresh state, and latest
operation records. Until a background queue exists, `status` is limited to
`idle`, `running`, `failed`, or `blocked`; queued items must not be invented.
When process-local refresh state is absent after an API restart, the endpoint
must recover `expiresAt` from the latest durable successful refresh operation
result, even if a newer failed/blocked operation exists. `status`, `blocker`,
and `operationId` describe the latest operation attempt so the UI can show both
the last known expiry and the most recent operational failure honestly. This
recovery is not limited by the `/api/operations` presentation limit.

## Error Shape

All new operation endpoints use this JSON shape for non-validation failures:

```json
{
  "error": "operation-store-unavailable",
  "message": "Operation history could not be loaded.",
  "detail": "optional diagnostic detail"
}
```

Validation failures use ASP.NET validation problem details where field-specific
input is invalid.

## Planned Endpoints Not Yet Live

- `GET/POST/PATCH /api/devices/known`: persisted known-phone inventory.
- `POST /api/catalog/apps/upload`: browser IPA upload/import.
- `POST /api/operations/{id}/cancel`: only after a safe background worker exists.
- `POST /api/operations/{id}/retry`: only after retry semantics are idempotent.
- `POST /api/operations/{id}/rerun`: only after fresh preflight creates a new operation.
- `GET /api/diagnostics/issues`: durable grouped issue store backed by operation
  history and trace/log evidence.

## Roadmap Contracts

These contracts are the remaining SDD roadmap. They intentionally reuse the
current JSON-store and operation patterns.

### Known Devices

Endpoints:

- `GET /api/devices/known?includeReachable=true`
- `POST /api/devices/known`
- `PATCH /api/devices/known/{udid}`
- `DELETE /api/devices/known/{udid}`

DTO:

```json
{
  "udid": "000081...",
  "displayName": "Dragos iPhone",
  "productType": "iPhone15,2",
  "osVersion": "18.5",
  "connection": "usb",
  "firstSeenAt": "2026-06-24T10:00:00Z",
  "lastSeenAt": "2026-06-24T10:05:00Z",
  "lastSeenSource": "live-poll",
  "currentPollAt": "2026-06-24T10:05:00Z",
  "trustState": "trusted",
  "health": {
    "state": "healthy",
    "reason": "Reachable in current poll.",
    "source": "derived",
    "checkedAt": "2026-06-24T10:05:00Z",
    "nextAction": null
  },
  "appSlots": { "used": 2, "limit": 3, "source": "derived" },
  "owner": null,
  "notes": null,
  "source": "live"
}
```

`POST /api/devices/known` request:

```json
{
  "udid": "000081...",
  "displayName": "Dragos iPhone",
  "owner": "admin",
  "notes": "Daily driver"
}
```

`PATCH /api/devices/known/{udid}` request:

```json
{
  "displayName": "Lab iPhone",
  "owner": null,
  "notes": "Kept on USB hub"
}
```

Responses:

- `200 OK` with known-device DTO for update/merge.
- `201 Created` with known-device DTO for first manual create.
- `204 No Content` for delete.
- `400 ValidationProblem` for missing/invalid UDID.
- `404 Not Found` when patching/deleting an unknown UDID.
- `409 Conflict` when deleting a known device that still has app registrations;
  the response includes `error=device-has-registrations` and registration count.
- `503` with `known-device-store-unavailable` when JSON history cannot load.

Rules:

- `/api/devices` remains the reachable current-poll snapshot.
- Known-device inventory is durable and may include offline/stale devices.
- `lastSeenAt` becomes durable only when the known-device store records it.
- Removing a known device does not remove app registrations or uninstall apps.
- `POST` upserts current-poll evidence when the UDID is reachable and otherwise
  creates a manual known record with `connection=unknown`.
- Mutable fields are limited to `displayName`, `owner`, and `notes` in this phase.
- The store records `firstSeenAt` once and updates `lastSeenAt` only from live
  device evidence, not from editing metadata.

### Browser IPA Upload / Import

Endpoint:

- `POST /api/catalog/apps/upload` multipart form field `ipa`, optional `id`,
  `name`, `purpose`, and `replace`.

Response reuses `CatalogAppDto` plus upload provenance:

```json
{
  "id": "cert-clock",
  "name": "Cert Clock",
  "bundleId": "com.example.certcountdown",
  "ipaPath": "/var/lib/sideport/imports/cert-clock.ipa",
  "source": "upload",
  "status": "ready",
  "sizeBytes": 12345,
  "sha256": "...",
  "lastInspectedAt": "2026-06-24T10:00:00Z",
  "notes": ["No embedded provisioning profile was found; Sideport must sign this IPA before install."]
}
```

Rules:

- Upload/import stores and inspects the IPA. It does not register, sign, or install.
- The server validates extension, content inspection, size, hash, bundle ID, and
  duplicate/replace behavior before saving the catalog entry.
- The initial upload limit is `Sideport:Catalog:MaxUploadBytes`, default
  `268435456` (256 MiB). The response includes this limit on `upload-too-large`.
- Uploads are first written to a temporary file under `Sideport:State:Directory`;
  failed validation removes the temporary file and does not modify the catalog.
- If `replace=false` or omitted and the computed catalog ID already exists,
  return `409 Conflict` with `catalog-id-conflict`.
- If `replace=true`, atomically replace the catalog entry and durable IPA after
  inspection succeeds.
- `201 Created` returns the new catalog app when an ID is new.
- `200 OK` returns the replaced catalog app when `replace=true`.
- Errors include `upload-too-large`, `unsupported-media-type`,
  `ipa-inspection-failed`, `catalog-id-conflict`, and `catalog-store-unavailable`.
- App registration may continue to accept `inputIpaPath`, but UI should prefer a
  catalog artifact once upload exists.

### Workspace Roles / Capabilities

Endpoint:

- `GET /api/workspace`

DTO:

```json
{
  "name": "Sideport workspace",
  "authMode": "bearer-or-oidc",
  "authDelegated": true,
  "roleEnforcement": "advisory",
  "supportsUserAdministration": false,
  "currentMember": { "id": "api-token-client", "name": "api-token-client", "role": "owner", "source": "derived" },
  "members": [],
  "roles": [
    { "id": "owner", "label": "Owner", "capabilities": ["workspace.read", "operations.run", "operations.cancel.queued"] },
    { "id": "viewer", "label": "Viewer", "capabilities": ["workspace.read"] }
  ],
  "capabilities": {
    "users.invite": false,
    "users.suspend": false,
    "operations.cancel.queued": true,
    "operations.cancel.running": false
  },
  "source": "live"
}
```

Rules:

- Workspace roles control Sideport UI/API capabilities only.
- Apple Developer Teams and Apple account roles are separate and must not be
  conflated with Sideport workspace roles.
- No invite, password, local session, API-token management, owner transfer, or
  offboarding mutation is live until the identity source is decided in a later ADR.

Capability/auth-scope table for this roadmap:

| Endpoint | Required capability | Enforcement in this phase |
| --- | --- | --- |
| `GET /api/devices/known` | `devices.read` | server-enforced when auth principal is known; bearer token is owner-equivalent |
| `POST/PATCH/DELETE /api/devices/known` | `devices.manage` | advisory for OIDC claims unless configured; bearer token is owner-equivalent |
| `POST /api/catalog/apps/upload` | `catalog.import` | advisory for OIDC claims unless configured; bearer token is owner-equivalent |
| `POST /api/operations/refresh` | `operations.run` | existing `/api` auth gate, capability reported in workspace contract |
| `POST /api/operations/{id}/cancel` | `operations.cancel.queued` | server checks operation state; role enforcement is advisory unless configured |
| `POST /api/operations/{id}/retry` | `operations.retry` | server checks retryable flag/preflight; role enforcement advisory unless configured |
| `POST /api/operations/{id}/rerun` | `operations.rerun` | server re-runs preflight; role enforcement advisory unless configured |
| `PATCH /api/diagnostics/issues/{id}` | `diagnostics.triage` | advisory unless configured |

If role enforcement is advisory, the API still validates operation state and
input safety; the UI must label user administration as delegated/planned.

### Background Operations

Existing endpoint `POST /api/operations/refresh` remains the refresh submission
entry point. Once the worker lands, ready refresh submissions return `202 Accepted`
with a `queued` operation record. Blocked preflight may still return a terminal
`blocked` record.

Additional endpoints:

- `POST /api/operations/{operationId}/cancel`
- `POST /api/operations/{operationId}/retry`
- `POST /api/operations/{operationId}/rerun`

Cancel request:

```json
{ "reason": "Operator canceled before signing started." }
```

Retry/rerun request:

```json
{ "idempotencyKey": "optional-client-key", "reason": "Retry after completing 2FA." }
```

Responses:

- `202 Accepted` with updated operation for cancel accepted on `queued`/`waiting`.
- `409 Conflict` with `operation-not-cancelable` when operation is running or terminal.
- `201 Created` with new operation for retry/rerun.
- `200 OK` with existing operation when idempotency tuple already exists.
- `404 Not Found` when the operation ID is unknown.
- `422 Unprocessable Entity` with preflight blockers when retry/rerun cannot start.
- `503 operation-store-unavailable` on JSON store failure.

Rules:

- Operation statuses may include `queued`, `waiting`, `running`, `canceling`,
  `canceled`, `blocked`, `succeeded`, `failed`, and `unknown`.
- `cancelable=true` only for queued/waiting operations in this roadmap. Running
  Apple/device mutation stages are non-cancelable until a safe boundary is proven.
- Retry/rerun creates a new operation after fresh preflight. Historical records
  stay immutable except for status transitions owned by the worker.
- Scheduler-triggered work must enqueue as `system:scheduler` instead of calling
  the orchestrator directly.
- Duplicate cancel is idempotent for already-canceling/canceled operations and
  returns the current record.
- Retry is allowed only when the source operation has `retryable=true`.
- Rerun is allowed for any completed refresh operation when fresh preflight is
  ready; it does not copy stale preflight results.
- New operations link to their source with `parentOperationId` and increment
  `attempt` for retry.

State/action table:

| State | Cancel | Retry | Rerun | Notes |
| --- | --- | --- | --- | --- |
| `queued` | allowed -> `canceled` | not allowed | not allowed | no side effect started |
| `waiting` | allowed -> `canceled` | not allowed | not allowed | waiting for signer lock |
| `running` | not allowed | not allowed | not allowed | Apple/device mutation may be in flight |
| `blocked` | not allowed | allowed if `retryable` | allowed after fresh preflight | blocked preflight has no side effect |
| `failed` | not allowed | allowed if `retryable` | allowed after fresh preflight | retry creates new attempt |
| `succeeded` | not allowed | not allowed | allowed after fresh preflight | rerun creates new operation |
| `canceled` | idempotent no-op | not allowed | allowed after fresh preflight | no side effect started |
| `unknown` | not allowed | allowed only after operator confirms review | allowed after fresh preflight | terminal-state-unknown needs human review |

### Durable Diagnostics Issues

Endpoints:

- `GET /api/diagnostics/issues`
- `GET /api/diagnostics/issues/{issueId}`
- `PATCH /api/diagnostics/issues/{issueId}` for `investigating`, `resolved`,
  `ignored`, or `unresolved` once the store owns issue state.

Patch request:

```json
{
  "status": "investigating",
  "note": "2FA recovery in progress."
}
```

DTO:

```json
{
  "issueId": "issue-refresh-failed-000081-com.example.certcountdown",
  "category": "refresh-failed",
  "severity": "error",
  "status": "unresolved",
  "affected": { "deviceUdid": "000081...", "bundleId": "com.example.certcountdown" },
  "firstSeenAt": "2026-06-24T10:00:00Z",
  "lastSeenAt": "2026-06-24T10:05:00Z",
  "occurrenceCount": 2,
  "lastOperationId": "op_...",
  "correlationId": "op_...",
  "evidence": [
    { "type": "operation-stage", "label": "Sign and install", "message": "interactive sign-in required", "source": "live" }
  ],
  "remediation": "Complete Personal Apple ID sign-in, then retry after preflight.",
  "source": "live"
}
```

Rules:

- Durable issues group real operation/readiness/device/log evidence.
- Derived admin issues remain visually distinct when the durable issue endpoint
  is unavailable.
- Do not emit trace links unless real OpenTelemetry trace IDs exist.
- Issue identity is deterministic by `category + affected device/app + primary
  error code`; repeated matching failures increment `occurrenceCount` and update
  `lastSeenAt`.
- `PATCH` returns `200 OK` with the updated issue, `404` for unknown issues,
  `400 ValidationProblem` for invalid status, and `503 diagnostics-store-unavailable`
  on JSON store failure.
- `resolved` issues reopen automatically when a new matching failure arrives.
- Evidence may include operation stage IDs, log entry IDs, readiness check IDs,
  device diagnostic check IDs, redacted messages, and optional real trace IDs.

## UI Binding Rules

- Prefer `/api/operations` and `/api/renewals` when present. Fall back to derived
  renewal rows from `/api/apps` only with `source=derived` or `planned` where
  appropriate.
- Show operation stages only from operation records or clearly labeled Storybook
  fixtures. Do not show a live queue when no operation/renewals endpoint exists.
- Current reachable-device snapshots are not persistent known-device inventory;
  current-poll timestamps must be labeled as derived, not durable last-seen
  history.
- Admin-synthesized diagnostics from failed fetches or app `lastError` fields are
  derived issue cards. Do not present them as the future durable diagnostics
  issue store.
- Disable cancel/rerun controls unless the operation record exposes the matching
  capability flag.
- Treat `/api/workspace` failure as delegated-auth/planned, not as a system
  error, until the backend owns workspace administration.

## Deployment Contract

- `/var/lib/sideport` is durable state and must be volume-backed before
  production cutover.
- Anisette ADI identity must be persistent and backed up. An `emptyDir` is only a
  local-development placeholder and is not valid for the Kubernetes deployment
  contract.
- `/var/lib/lockdown` pairing records are read-only host trust material and must
  be mounted only when the deployment host owns the iPhone pairing.
- IaC changes remain plan-only until human approval.