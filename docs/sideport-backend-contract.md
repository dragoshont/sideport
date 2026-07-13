# Sideport Backend Contract

Date: 2026-07-12
Status: authoritative for current behavior and the explicitly labeled planned V2 contract

This is the cross-tier contract for Sideport's admin UI and .NET API. It
captures what is live now, what is intentionally derived by the UI, the
operation/preflight contracts that make signing and refresh observable, and the
planned Onboarding V2 contract. Planned behavior is not live until its
implementation and gates land.

The remaining roadmap foundation is governed by
`docs/architecture/adr-0001-roadmap-foundations.md`: JSON-backed stores,
single-replica/single-flight execution, role/capability vocabulary before
operation controls, and no database/broker until multi-replica execution is a
current requirement. The planned family boundary is governed by
`docs/architecture/adr-0002-family-access-authorization.md` and remains
non-live until its Phase 9 implementation passes.

## Contract Rules

- Every `/api/*` endpoint is protected by bearer token or authenticated OIDC
  session when either auth mode is configured, except the explicitly planned
  token-authenticated `POST /api/workspace/invitations/handoff` and
  `POST /api/workspace/owner-claims/handoff` exchanges. Those two pre-login
  endpoints accept only their high-entropy fragment token in a bounded JSON
  body over effective HTTPS, are rate-limited/no-store, and grant no membership;
  the corresponding `GET` preview and `POST` acceptance endpoints require an
  authenticated OIDC session. `/healthz` and `/readyz` stay open for probes.
- Secret values never appear in routine API responses, logs, operation records,
  server-observed URL paths or query strings, analytics, or receipts. The
  planned invitation and Owner-claim create endpoints are the narrow exception:
  an authorized Owner/recovery caller receives the new raw token once inside the
  `#fragment` of a no-store share URL. A URL fragment remains browser-local and
  is never sent in the HTTP request target or copied into OIDC state. The planned
  protected credential-connect endpoint is the only browser request allowed to
  contain an Apple password; it is a write-only input that becomes
  server-custodied after Apple authentication.
  API keys are accepted only in their documented Authorization header and are
  never returned to browser code, copied into domain request bodies, or
  persisted in domain/audit records. Private keys and anisette identity
  material never cross the API boundary.
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
- If the API restarts after recording a `running` operation but before saving its
  terminal state, operation-history reads reconcile an ambiguous record older
  than 30 minutes to `unknown` with `operation-terminal-state-unknown`.
  Device-enrollment recovery and operations that already contain durable
  successful device-verification evidence are exempt: their idempotent
  finalizers may resume, but pairing, signing, installation, and device
  verification are not repeated.

## Live Endpoints

| Method | Path | Purpose | Source | Notes |
| --- | --- | --- | --- | --- |
| `GET` | `/healthz` | Process liveness | live | Open probe. |
| `GET` | `/readyz` | Anisette + signer readiness | live | Open probe. |
| `GET` | `/api/about` | Service metadata | live | Protected like other `/api/*`. |
| `GET` | `/api/me` | Current API identity mode | live | OIDC user or bearer-token client. |
| `GET` | `/api/authentication/options` | Public sign-in presentation and enrollment capability | live | Provider ID/labels are deployment-configurable; passkey enrollment is advertised only when the Authentik adapter is configured. |
| `GET` | `/api/anisette/info` | Anisette client info probe | live | No raw anisette secrets. |
| `GET` | `/api/logs?limit=` | In-process API log tail | live | Ring buffer, not durable operation history. |
| `GET` | `/api/apple-access/status` | App Store Connect read-only probe | live | Optional paid-team path. |
| `GET` | `/api/apple-access/personal/status` | Personal Apple ID connector status | live | Host-side credential custody. |
| `POST` | `/api/apple-access/personal/sign-in` | Start Personal Apple sign-in | live | Apple ID only; password from custody. |
| `POST` | `/api/apple-access/personal/connect` | Validate and store a managed Apple credential | live | Protected transport; password is write-only. |
| `POST` | `/api/apple-access/personal/2fa` | Complete pending 2FA challenge | live | Code only. |
| `PUT` | `/api/apple-access/personal/team` | Persist an Apple-returned team | live | A typed arbitrary Team ID is rejected. |
| `GET` | `/api/devices` | Reachable device snapshot | live | Not persistent known-device inventory. |
| `GET` | `/api/devices/diagnostics` | Device transport self-test | live | Human-readable remediation. |
| `GET` | `/api/devices/{udid}/installed-apps` | Installed app snapshot | live | Only when device is reachable. |
| `GET/POST/PATCH/DELETE` | `/api/devices/known` | Durable known-device inventory | live | Manual records do not imply Trust or acceptance. |
| `POST` | `/api/devices/enrollments` | Bounded Add iPhone operation | live | Pairing occurs only in this explicit user-started flow. |
| `GET` | `/api/system/status` | Protected operational checks | live | Repair UI remains available when a dependency fails. |
| `GET` | `/api/scheduler/status` | Durable automatic-refresh status | live | Includes prerequisites, policy, and evaluation history. |
| `PUT` | `/api/scheduler/settings` | Change durable automatic-refresh settings | live | Enabling fails closed until prerequisites pass. |
| `GET` | `/api/onboarding/status` | V1 first-run prerequisite checklist | live | The planned additive V2 `workflow` contract is defined below. |
| `POST` | `/api/onboarding/complete` | Resume verified onboarding finalization | live | Never repeats signing, installation, or device verification. |
| `GET` | `/api/workspace` | Workspace auth mode, current member, roles, and capabilities | live | Read-only contract; user administration is not live. |
| `GET` | `/api/catalog/apps` | Server-side IPA catalog | live | Durable JSON catalog. |
| `POST` | `/api/catalog/apps/inspect` | Inspect/store server-side IPA path | live | Adds a server-local IPA to the durable catalog. |
| `POST` | `/api/catalog/apps/upload` | Upload, inspect, and durably import an IPA | live | Multipart `ipa`; size/media/conflict validation is server-enforced. |
| `GET` | `/api/apps` | Registered apps + last refresh state | live | Durable registrations, process-local refresh state. |
| `POST` | `/api/apps` | Save app registration | live | Validates IPA path, bundle ID, and 3-slot limit. |
| `DELETE` | `/api/apps/{udid}/{bundleId}` | Remove registration | live | Does not uninstall from the device. |
| `POST` | `/api/apps/{udid}/{bundleId}/verify` | Verify a legacy active registration | live | Queued read-only migration; never signs or installs. |
| `POST` | `/api/apps/{udid}/{bundleId}/refresh` | Deprecated queued refresh wrapper | live | Uses the guarded operation path; new UI should prefer operations. |
| `POST` | `/api/operations/preflight` | Install or refresh preflight | live | Install plans bind the exact current selection. |
| `POST` | `/api/operations/install` | Queued first/later install | live | Device verification precedes registration activation. |
| `POST` | `/api/operations/refresh` | Queued guarded refresh | live | Preferred UI refresh entry point. |
| `GET` | `/api/operations[/{id}]` | Durable operation history | live | Supports polling and restart recovery. |
| `POST` | `/api/operations/{id}/cancel|retry|rerun` | State-gated operation actions | live | Server flags and state remain authoritative. |

### Current Device Transport Truth

The live Netimobiledevice backend enumerates both USB and network devices,
prefers USB when both are available, and otherwise opens a paired Wi-Fi device
directly over TCP using the existing pairing record. Install and refresh use
the selected lockdown session, so the current scheduler can attempt a due
refresh over paired Wi-Fi when USB is absent. USB is required to create the
pairing; Wi-Fi consumes existing trust and never initiates pairing.

That capability is not yet equivalent to proven wireless reliability. Issue #3
records bulk-transfer stalls, socket failures, and ambiguous network
verification, and the current synchronous route has no complete termination
and reconciliation boundary. Operationally, USB remains the reliable fallback.
The planned V2 contract below preserves paired-Wi-Fi refresh while adding the
bounded transfer, unknown-state quarantine, verification, and fallback rules
required before it is called production-ready.

### Identity provider and passkey ownership

Sideport is an OIDC relying party, not an account or WebAuthn authority. A
deployment may configure `Sideport:Oidc:ProviderId`, `ProviderLabel`, and
`LoginLabel` to describe its standards-compliant OIDC provider without changing
the immutable workspace identity key: the validated OIDC issuer plus subject.

The currently implemented invited-user provisioning adapter is Authentik. When
its base URL, least-privilege API token, and enrollment flow are configured,
`GET /api/authentication/options` reports `enrollmentEnabled=true` and the
invitation handoff offers **Create passkey** before the existing-account OIDC
login. Authentik owns the discoverable credential, user verification, recovery,
and cross-platform passkey ceremony. Sideport creates only the short-lived
provider invitation and returns the browser to `/invite`; membership is still
granted only after the resulting validated OIDC session explicitly accepts the
Sideport invitation. A different OIDC provider works for existing-account login
without claiming generic account provisioning or passkey enrollment.

## Onboarding V2 Runtime and Explicitly Planned Contract

This section is the canonical backend contract for a fresh deployment
accepting a server-custodied Apple signer, a new physical iPhone, and a first
app through the UI. Implemented behavior is live unless a subsection is
explicitly labeled planned. It is additive for one compatibility release. Where it
overlaps the older roadmap contracts later in this document, this section's
stronger authentication, trust, verification, serialization, and migration
rules take precedence for Onboarding V2.

### Completion and Current Readiness

The API exposes two independent truths:

- `workflow.setupState=complete` is monotonic historical evidence that the full
  first-run path succeeded at least once. It is true only when an immutable V2
  completion receipt exists.
- `workflow.readyNow` is the current operational assessment. It may become
  false after completion when Apple authentication is stale, anisette is
  unavailable, a required store is corrupt, a phone is offline/untrusted, or a
  signer cutover is newly required.

A reachable device, saved registration, queued operation, operator
acknowledgement, or enabled config value is not completion evidence by itself.
`setupState` does not regress when `readyNow` regresses, and later outages route
to Overview/Diagnostics rather than forcing the user through first-run setup
again.

The normal happy path creates the receipt inside the first install operation
when its durable intent has `finishOnboarding=true`. The compatibility/recovery
`POST /api/onboarding/complete` entry point calls the same idempotent finalizer;
it is not a separate happy-path UI step. The finalizer may create the receipt
only after the server rechecks all of these conditions:

1. Mutation access is protected by configured bearer-token auth or OIDC.
2. Sideport state and work directories are writable and all required JSON
   stores load without corruption.
3. Provisioned anisette headers are available; static `client_info` metadata is
   insufficient.
4. A server-custodied Personal Apple ID credential has authenticated
   successfully.
5. A team returned by Apple is selected, persisted, and has a validation
   timestamp.
6. A usable persisted signing identity is verified. If mint or replacement was
   necessary, its durable signer-cutover operation is terminal `succeeded`.
7. The selected device is durably accepted, currently reachable over USB, and
   has a successful lockdown/trust handshake.
8. A ready catalog artifact and registration retain durable lineage to the
   artifact, bundle ID, accepted device, account profile, and selected team.
9. A durable install operation recorded successful sign/install stages and
   device-side verification matched the bundle ID and provisioning-profile
   expiry; the enclosing onboarding operation need not be terminal until the
   receipt is written.
10. The verified registration is `active`, and the scheduler is durably enabled
    with a computed next evaluation.

The receipt is written last as `onboarding-completion.json` under
`Sideport:State:Directory`, using the existing atomic JSON-store pattern. It is
immutable, one-per-workspace, non-secret, and has this semantic shape:

```json
{
  "schemaVersion": 2,
  "completedAt": "2026-07-11T12:05:00Z",
  "actor": {
    "kind": "oidc-user",
    "displayName": "operator@example.com"
  },
  "accountProfileId": "acct_...",
  "teamId": "TEAMID1234",
  "deviceUdid": "000081...",
  "registrationKey": {
    "deviceUdid": "000081...",
    "bundleId": "com.example.app"
  },
  "verifiedOperationId": "op_...",
  "schedulerSettingsVersion": "settings_...",
  "operationalCheckedAt": "2026-07-11T12:04:59Z"
}
```

No GET creates or repairs this receipt. Removing a registration, disabling the
scheduler, taking the phone offline, or reconfiguring the signer changes
`readyNow` and affected steps but never edits or deletes the receipt. V2 has no
UI reset endpoint; factory reset remains a separately approved state-volume
operation.

`readyNow=true` requires passing current operational checks, a recently
validated cached Apple session with no pending 2FA challenge, at least one
accepted device currently trusted and usable for a supported operation, no
corrupt required store, and no new signer cutover requirement. An Apple status
GET never predicts session renewal or refreshes authentication.

### Authentication, Capabilities, and Secret Custody

All endpoints in this section remain under the existing authenticated `/api/*`
boundary. In the current Phase 6 runtime, bearer-token clients and every
authenticated OIDC principal are owner-equivalent. This is a known ship blocker,
not the family authorization design. The UI must not create invitations or
claim that roles are enforced until the planned family boundary below is live.

Open-behind-proxy mode has no verifiable actor and is read-only. Every `/api`
mutation, including credential connect, upload, team selection, cutover,
pairing, acceptance, registration, install, refresh, reconciliation, scheduler
settings, and completion, returns `403 mutation-protection-required` in that
mode. Trusted proxy identity headers require a separate auth contract.

The API server-enforces these capabilities even though all authenticated actors
are owner-equivalent in V2:

| Mutation | Required capability |
| --- | --- |
| Apple credential connect, connect-challenge completion, or same-account rotation | `apple.signer.manage` |
| Team selection and signer cutover | `apple.signer.manage` |
| Pairing and device acceptance | `devices.manage` |
| Catalog import and registration | `catalog.import` |
| Install, refresh, verify, and reconciliation | `operations.run` |
| Scheduler settings | `scheduler.manage` |
| Onboarding completion | All capabilities required by the preceding evidence-producing mutations |

Every accepted mutation records the actor from the existing bearer/OIDC actor
model. Apple passwords, session tokens, anisette headers, API tokens, private
keys, PKCS#12 bytes, 2FA codes, and IPA contents never appear in a response,
operation record, receipt, or log. Signer replacement acknowledgement is bound
to the current preflight's exact certificate IDs, impact codes, and inventory
version; the API never accepts a loose confirmation flag. The live legacy
sign-in path still accepts only an Apple ID and resolves the password from
existing custody. The planned V2
connect path accepts a password once as write-only request data, clears it from
browser state after submission, and returns only a stable account-profile ID
and redacted Apple ID hint. Durable password custody remains server-side.

### Family Membership and Authorization — planned Phase 8 contract

This subsection is the implementation target for Phases 9–10. It is **planned,
not live** while Phase 8 awaits human sign-off. Once implemented it takes
precedence over the current owner-equivalent OIDC behavior and the older
advisory workspace-role section later in this document.

#### Trust boundaries and request resolution

Authentik proves an OIDC session; Sideport decides membership and resource
scope. A human identity key is the exact issuer from the successfully validated
OIDC security token plus its non-empty `sub` claim. Sideport never accepts an
issuer from a proxy header, request body, display claim, or a fallback such as
`configured-oidc`. Subject comparison is ordinal and case-sensitive. Email,
username, display name, `amr`, and upstream-provider claims are presentation
metadata only.

Each API request resolves in this order:

1. A constant-time match for the configured API bearer yields the non-human
   `recovery-bearer` actor with Owner capabilities and `all` resource scope.
2. A validated OIDC session loads `workspace-access.json` and finds the member
   by exact issuer and subject. `active` members receive their role and scopes.
3. An authenticated but unknown OIDC principal may use only `GET /api/me`, the
   authenticated Owner-claim/invitation handoff-preview reads, and their
   corresponding acceptance endpoints. Every other API returns
   `403 workspace-membership-required`.
4. A `suspended` or `offboarded` member may use only `GET /api/me` and logout.
   Domain endpoints return `403 member-access-disabled`; a stale session never
   preserves access.
5. Open-behind-proxy mode has no verifiable human or resource scope. Under the
   family-aware contract it serves only shallow probes/service metadata and a
   configuration-required shell; domain reads and all mutations return
   `403 authentication-required` until bearer or OIDC authentication is
   configured.

Identity/bootstrap status is unambiguous:

| Condition | `/api/me` membership state | Other domain API |
| --- | --- | --- |
| OIDC validation fails or validated token has no non-empty subject/issuer | no authenticated session | `401 unauthorized` |
| Valid OIDC, workspace missing/bootstrap-required | `bootstrap-required` with recovery-proof readiness | `403 workspace-bootstrap-required` except handoff/Owner-claim acceptance |
| Valid OIDC, workspace active, no member | `none` | `403 workspace-membership-required` except handoff/acceptance |
| Valid OIDC, member suspended/offboarded | exact disabled state for self | `403 member-access-disabled` except logout |
| Valid active member | `active` | capability and resource checks apply |

Ordinary invitations always create role `family`. The first release has one
active human `owner`; it has no normal promotion/demotion API. A recovery claim
is the only way to replace an inaccessible Owner and must atomically activate
the claimant and suspend the previous Owner.

#### Session and workspace read models

`GET /api/me` remains available to an authenticated OIDC principal before
membership so an invitation can be accepted without granting any other API
access. It never returns raw issuer or subject:

```json
{
  "authenticated": true,
  "via": "oidc",
  "identity": {
    "displayName": "Mara",
    "email": "mara@example.test"
  },
  "membership": {
    "state": "none",
    "memberId": null,
    "role": null
  },
  "source": "live"
}
```

For a cookie-authenticated response, the endpoint sets a fresh
`X-Sideport-CSRF` request-token header and the matching secure, HTTP-only,
SameSite antiforgery cookie. A bearer response reports `via=recovery-bearer`,
has no human membership, and does not mint browser CSRF state.
`/api/me` and every workspace, member, invitation, claim, handoff, recovery,
and audit response set `Cache-Control: no-store`.

`GET /api/workspace` is available only to an active member or the recovery
bearer. It replaces advisory roles with enforced role and scope data:

```json
{
  "schemaVersion": 2,
  "workspaceId": "workspace_...",
  "name": "Sideport",
  "roleEnforcement": "server",
  "currentMember": {
    "memberId": "member_...",
    "displayName": "Mara",
    "email": "mara@example.test",
    "role": "family",
    "status": "active",
    "joinedAt": "2026-07-12T12:00:00Z",
    "lastActiveAt": "2026-07-12T12:05:00Z",
    "source": "live"
  },
  "members": [],
  "household": [
    { "displayName": "Dragos", "role": "owner", "deviceCount": null },
    { "displayName": "Mara", "role": "family", "deviceCount": 1 },
    { "displayName": "Alex", "role": "family", "deviceCount": 1 }
  ],
  "invitations": [],
  "roles": [
    { "id": "owner", "label": "Owner" },
    { "id": "family", "label": "Family" }
  ],
  "capabilities": {
    "catalog.read": { "allowed": true, "scope": "shared" },
    "devices.read": { "allowed": true, "scope": "own" },
    "operations.run": { "allowed": true, "scope": "own" },
    "members.invite": { "allowed": false, "scope": "none", "reason": "Owner only." }
  },
  "version": "workspace-version_...",
  "source": "live"
}
```

Owner and recovery-bearer projections include all human members and pending,
expired, or revoked invitation metadata. Family receives its own full
`currentMember`, an empty `members`/`invitations` administration surface, and an
active-household directory with display name, role, and coarse accepted-iPhone
count only. It receives no member ID, email, status, last-active time, invitation
state, device identity, or suspension detail for another person. No projection
exposes OIDC issuer/subject, token/hash, recovery material, Apple identifiers,
or another member's device or operation data.

For the non-human recovery bearer, `currentMember` is `null` and the response
instead includes `currentActor={"kind":"recovery-bearer","displayName":"Recovery access"}`.
It receives Owner capabilities with `all` scope, but is never counted as the
active human Owner and cannot accept an invitation.

Member status is `active|suspended|offboarded`. Invitation status is
`pending|accepted|revoked|expired`. Contact email and display name may be saved
for owner-facing delivery/recognition but never constrain the accepting OIDC
identity.

#### Owner bootstrap and recovery

An OIDC/family-capable deployment requires a configured recovery bearer. When
it is missing, `/api/me` reports `membership.state=bootstrap-required` with
`reason=recovery-proof-not-configured`, Owner-claim minting is unavailable, and
the setup/system status remains blocked. Phase 10/15 deployment examples must
generate and reference this secret and provide one local, non-printing command
that reads the bearer without echoing it and returns only the short-lived Owner
link; a nontechnical owner is never sent to inspect a Kubernetes secret during
browser onboarding.

The long-lived recovery bearer is never pasted into the browser UI. A trusted
administrator uses it only in the standard `Authorization: Bearer` header to
call `POST /api/workspace/owner-claims`:

```json
{
  "expectedOwnerMemberId": null,
  "impactVersion": null,
  "confirmReplacement": false,
  "expiresInMinutes": 15,
  "idempotencyKey": "owner-claim-..."
}
```

On an empty workspace this creates a bootstrap-required workspace containing a
hashed, single-use Owner-claim record. It returns a share URL once, using the
same 32-byte secret, fragment-only transport, no-store, hash, expiry,
idempotency, and log-redaction rules as a Family invitation. The default and
maximum expiry are fifteen minutes and one hour. An exact create replay returns
claim metadata without the URL. If the first response was lost, the
administrator uses that metadata to call
`POST /api/workspace/owner-claims/{claimId}/revoke` with the exact claim version
and a fresh idempotency key, then mints a replacement with another create key.
Revocation atomically marks the pending claim revoked and invalidates all of its
handoffs; it never returns the token and an accepted/expired/revoked claim can
never become pending again. Merely being the first OIDC login never creates an
Owner.

When an active Owner already exists, the first request returns
`409 owner-replacement-confirmation-required` with an impact object containing
only stable IDs and counts of members, owned/unassigned devices,
registrations, queued/running work, and scheduler effects plus a short-lived
`impactVersion`. A replacement requires the exact current Owner member ID,
matching `impactVersion`, `confirmReplacement=true`, and a fresh idempotency
key. That creates a short-lived claim bound to the exact impact and existing
Owner; it does not change membership yet.

The public `/owner-claim` shell reads `#spown1_...` into memory, immediately
strips the fragment from browser history, and POSTs it once to the
unauthenticated, rate-limited
`POST /api/workspace/owner-claims/handoff` endpoint. The endpoint validates the
complete claim from the exact JSON body
`{ "claimToken": "spown1_..." }` without consuming it and replaces the raw
authority with a
ten-minute, single-use, opaque `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/`
handoff cookie. It returns only a safe pre-login workspace and claim-kind
preview. The raw claim is never written to local/session storage, another
cookie, the login return URL, or OIDC state.

After OIDC login, `/owner-claim` calls authenticated, no-store
`GET /api/workspace/owner-claims/handoff`. The endpoint resolves only the opaque
cookie, revalidates either the pending claim or an accepted claim bound to that
same immutable identity, and returns the actual signed-in account plus the exact
bootstrap/recovery impact or accepted receipt; it never returns either token.
An explicit **Finish owner setup** or **Recover owner access** action sends only
an idempotency key to
`POST /api/workspace/owner-claims/accept`; the raw claim token is not submitted
again. This endpoint permits a valid unknown OIDC principal or active Family
member, but not a suspended/offboarded member or the recovery bearer. It
atomically rechecks the opaque handoff, claim, expiry, identity, and replacement
impact. Bootstrap creates the claimant as the sole Owner. Recovery
activates/promotes the claimant, suspends the former Owner, retains all
resources/evidence, consumes the handoff and claim, clears the handoff cookie,
and writes an immutable recovery receipt. Same-claim/same-identity replay
returns the receipt; stale replacement impact consumes nothing and returns
`409 owner-replacement-preflight-stale`, requiring a new bearer-minted claim.

Claim minting and handoff are JSON-only, at most 8 KiB, effective-HTTPS
protected, rate-limited, and no-store; minting additionally requires the
recovery bearer. Handoff creation does not grant membership and does not
require cookie antiforgery state. Acceptance is cookie-authenticated and
additionally same-origin/antiforgery protected. The handoff request accepts the
raw claim only for that single exchange and never formats, returns, or logs it.
Validation detail, exception messages, logs, audit, and responses never include
the bearer, raw claim token, or token hash. Phase 9 exposes the API; the approved
Storybook already includes the Owner-claim setup and recovery layouts for the
later runtime binding.

The invitation and Owner-claim POST handoff routes are the only API routes that
accept these raw link tokens. Endpoint-specific middleware disables request-body
capture before routing, content-type/model-binding diagnostics use fixed messages,
and tracing/logging records only outcome codes and a generated request ID. The
body is read once over effective HTTPS, bounded to 8 KiB, never formatted into an
exception, metric, trace, or structured-log property, and cleared from browser
memory after the response. This JSON-body exchange is deliberate: the fragment
is not part of an HTTP request and must first be read by the shell; the token is
then protected in transit by HTTPS without entering a path, query, referrer,
cookie, or OIDC state.

Both public shells set `Cache-Control: no-store`, `Referrer-Policy: no-referrer`,
`X-Content-Type-Options: nosniff`, and a nonce-free CSP at least as strict as
`default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:;
connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self';
frame-ancestors 'none'`. They load no third-party script, analytics, font, image,
or frame before or after exchanging the fragment.

#### Invitation lifecycle

`POST /api/workspace/invitations` is Owner/recovery-bearer only:

```json
{
  "displayName": "Mara",
  "contactEmail": "mara@example.test",
  "expiresInDays": 7,
  "idempotencyKey": "invite-create-..."
}
```

`contactEmail` is required by the approved first UI so the Owner can identify
where to send the copied link; Sideport does not send mail. It is owner-private
delivery metadata and never identity authority. `displayName` is optional. The
only role is `family`. Expiry defaults to seven days and is
bounded from ten minutes through thirty days. Sideport generates a 32-byte cryptographically
secure random secret and an independent opaque invitation ID. The share token
is versioned as `spinv1_<invitationId>_<base64url-secret>`; the store retains
only SHA-256 of the secret and compares it in constant time.

The existing configured HTTPS `Sideport:PublicOrigin` is the only authority used to
build the returned URL; request `Host` and forwarded host values never choose
it. A successful first response is `201` and contains:

```json
{
  "invitation": {
    "invitationId": "invitation_...",
    "displayName": "Mara",
    "contactEmail": "mara@example.test",
    "role": "family",
    "status": "pending",
    "createdAt": "2026-07-12T12:00:00Z",
    "expiresAt": "2026-07-19T12:00:00Z",
    "createdByActor": { "kind": "member", "memberId": "member_..." },
    "acceptedAt": null,
    "acceptedMemberId": null,
    "version": "invitation-version_..."
  },
  "shareUrl": "https://sideport.example/invite#spinv1_...",
  "linkAvailable": true
}
```

The raw token is returned once. An exact create replay returns the existing
invitation with `shareUrl=null` and `linkAvailable=false`; if the first response
was lost, the UI explains that it must replace the link rather than pretending
to recover it. `POST /api/workspace/invitations/{invitationId}/revoke` accepts
`expectedVersion` and `idempotencyKey`, revokes a pending invite idempotently,
and never returns the token.

`createdByActor.kind` is `member` with an Owner `memberId`, or
`recovery-bearer` with `memberId=null`. The bearer remains a service actor and
never becomes a synthetic member.

The public `/invite` shell reads the fragment into memory, immediately strips it
from browser history, and POSTs it once to the unauthenticated, rate-limited
`/api/workspace/invitations/handoff` endpoint. That endpoint validates the
complete token from the exact JSON body
`{ "invitationToken": "spinv1_..." }` without consuming the invitation, creates a ten-minute
single-use handoff record with an independent random ID/hash, sets only the
opaque ID in a `Secure`, `HttpOnly`, `SameSite=Lax`, `Path=/` cookie, and returns
safe pre-login workspace/inviter/role preview data. The browser clears the raw token
before starting OIDC and never writes it to local/session storage, another
cookie, the login return URL, or OIDC state.

After Authentik login, `/invite` calls authenticated, no-store
`GET /api/workspace/invitations/handoff`. The endpoint resolves only the opaque
cookie, revalidates either the pending invitation or an accepted handoff bound
to that same immutable identity, and returns the workspace, current signed-in
account, inviter display name, fixed Family permissions, expiry, and accepted
receipt when applicable without returning either token. The shell shows that
preview and one explicit **Join Sideport** action when still pending. That
action sends:

```json
{
  "idempotencyKey": "invite-accept-..."
}
```

to `POST /api/workspace/invitations/accept`. The endpoint requires a valid OIDC
cookie but deliberately permits an unknown principal. It atomically validates
the handoff, pending state, invitation expiry/version, and identity status;
creates one Family member; marks the invitation and handoff accepted; clears
the handoff cookie; and writes the audit event. A retry carrying the accepted
handoff by the same immutable identity returns `200` with the original receipt,
even though that identity now resolves as an active Family member. A different
active member receives `409 member-already-active` without consuming the
invitation; replay by another identity never reveals the accepted member. Since
handoff creation is unauthenticated, a suspended/offboarded person may obtain
the opaque cookie before identity is known, but the authenticated preview and
acceptance both deny that identity and consume nothing.

Owner-claim links use the same handoff pattern at
`POST /api/workspace/owner-claims/handoff` and an explicit post-login
**Finish owner setup** or **Recover owner access** action. Handoff creation does
not grant membership and does not require a cookie CSRF token; it accepts only a
same-origin request when `Origin` is present and relies on the explicit,
CSRF-protected post-login acceptance to prevent login/invitation CSRF.

The invitation shell loads no third-party scripts or analytics. Invitation
APIs are JSON-only, at most 8 KiB, no-store, rate-limited by client and
invitation ID, and never log the fragment, token, token hash, contact email, or
OIDC subject. A syntactically valid, hash-matched token may receive an
actionable `expired|revoked|already-used` state without naming another person;
all malformed, unknown, or mismatched tokens receive the same
`invitation-unavailable` response.

OIDC `displayName`, email, username, and upstream-provider presentation claims
are untrusted display input, never authority. At the boundary Sideport applies
Unicode normalization, fixed length limits, and rejects C0/C1 controls,
newlines/tabs, NUL, and bidi override/isolate controls. Email is retained only
when it passes the existing bounded email validator. API JSON serialization and
the React UI render these values as text; no identity claim may enter raw HTML,
CSS, a URL, a log template, an audit field, an operation message, a response
header, or a filesystem path. Phase 9 adds no CSV/spreadsheet export; any future
export must separately neutralize formula prefixes. Invalid presentation claims
fall back to a fixed neutral account label while the validated issuer+subject
continues to determine identity.

#### Capability and resource matrix

Capability and resource scope are both required. Having `operations.run` does
not authorize another member's iPhone.

| Capability | Owner / recovery bearer | Family |
| --- | --- | --- |
| `workspace.read` | allowed, all | allowed, self |
| `members.invite`, `members.manage`, `audit.read` | allowed | denied |
| `apple.signer.read`, `apple.signer.manage` | allowed | denied |
| `devices.read` | allowed, all | allowed, own |
| `devices.enroll` | allowed, target member required | allowed, self only |
| `devices.manage` | allowed, all; no ownership reassignment in the first release | allowed, own display metadata only |
| `catalog.read` | allowed, shared | allowed, shared |
| `catalog.import`, `integrations.github.manage` | allowed | denied |
| `registrations.read`, `registrations.manage` | allowed, all | read and remove own; creation only through approved-catalog install |
| `operations.read`, `operations.run` | allowed, all | allowed, own approved catalog apps |
| `operations.action` | allowed, all | own target and own actor only |
| `scheduler.read` | allowed, all | denied; per-app refresh state is projected from owned registrations |
| `scheduler.manage` | allowed | denied |
| `diagnostics.read` | allowed, all | allowed, own projection |
| `diagnostics.triage`, `logs.read` | allowed | denied |
| `onboarding.complete` | allowed | denied |

Family install/refresh may use the already selected team and usable local
signing identity. Normal, non-destructive provisioning for an approved catalog
app may proceed only when preflight reports quota and device-slot capacity. A
stale Apple session, missing/expiring signing identity, certificate
mint/replacement/revocation, team/account change, arbitrary artifact, or scarce
limit that needs an override returns `owner-action-required`; Family cannot
confirm or trigger that mutation.

Resource ownership is enforced as follows:

- Known devices add stable authority field `ownerMemberId`; free-text `owner`
  remains display-only during migration. Planned enrollment expands its body to
  `{ "targetMemberId": "member_...", "idempotencyKey": "..." }`. Family may
  omit the target or name only itself and may accept only a new/unassigned
  phone. Family self-service is limited to its first active accepted iPhone;
  another returns `owner-action-required` before pairing or Apple resource use.
  Owner must name an active target member and may deliberately enroll an
  additional phone after the normal device/team-capacity preflight. Once
  accepted, ownership is not reassigned in the first release; restore, remove,
  or intentionally re-enroll is required instead.
- Family device DTOs omit `acceptedBy`, legacy owner text, private notes,
  enrollment operation ID, and any other member identity. Those fields remain
  available only in the Owner projection.
- A registration inherits its device's owner and cannot be assigned separately.
  Family registration DTOs omit Apple ID, Team ID, input IPA path, signer
  details, and internal storage lineage. Family creation uses only the V2
  catalog ID, own device ID, and server-selected active signer; legacy
  registration creation remains Owner-only.
- Each operation snapshots `actorMemberId` and `ownerMemberId`. Family history
  is filtered to its owned targets. Cancel/retry/rerun/reconcile additionally
  requires the current Family member to be the initiating actor.
- Family operation responses use distinct allowlisted projections; they never
  serialize `OperationRecordDto` or `OperationPreflightDto` and redact fields
  afterward. The Family preflight projection contains readiness,
  `preflightId`, `planVersion`, expiry, the own-device/bundle/catalog target,
  safe blocker/warning codes and user messages, safe planned-effect labels,
  quota counts, confirmation/Owner-action flags, and source. It omits Apple ID,
  Team ID, account-profile ID, signing/certificate inventory, artifact path or
  hash, and internal details. The Family operation projection contains
  operation ID/type/status/timestamps, the same safe target, allowlisted stage
  label/status/timestamps, safe result version/expiry/next-refresh values,
  allowlisted error code/user message, action-capability flags, and source. It
  omits actor, idempotency key, correlation/parent IDs, raw stage or exception
  messages, Apple/team/account-profile values, install intent, artifact lineage,
  and any candidate outside the member's own enrollment operation.
- Renewal and diagnostic projections inherit registration/operation ownership
  and are also distinct allowlists. A Family renewal contains only its stable
  item ID, own device/bundle/catalog reference, app display name/version,
  risk/status/expiry, safe blocker code/message, refresh state, and source; it
  omits Team ID and internal operation/signer lineage. A Family diagnostic
  contains only issue ID, allowlisted category/severity/status, its own safe
  device/app reference, first/last seen time, count, safe remediation, and
  source. It omits correlation IDs, raw evidence/messages, operation actor,
  Owner notes, and triage controls. Raw in-process logs and global diagnostic
  triage remain Owner-only.
- Catalog entries are shared approved artifacts. Import roots, repository
  identities, private GitHub connections, and internal storage paths are not
  visible to Family.
- Legacy/unassigned devices, registrations without a resolvable device owner,
  and historical operations without stable ownership are Owner-only.

Out-of-scope object access returns the same `404 resource-not-found` as an
unknown object. Lists are filtered before pagination/counting, and no aggregate,
search, error detail, operation actor, or audit field leaks another member's
resource.

#### Endpoint authorization matrix

| Endpoint group | Owner | Recovery bearer | Family | Unknown OIDC | Suspended/offboarded OIDC |
| --- | --- | --- | --- | --- | --- |
| `/healthz`, `/readyz`, OIDC callbacks, `/invite` and `/owner-claim` shells | public as specified | public as specified | public as specified | public as specified | public as specified |
| Token-to-handoff endpoints | public, token required | public, token required | public, token required | public, token required | handoff may be set before identity is known; acceptance is denied |
| `GET /api/workspace/owner-claims/handoff` preview | same-identity accepted-claim replay only | denied | valid recovery claim | valid bootstrap/recovery claim | denied |
| `GET /api/workspace/invitations/handoff` preview | denied as already a member | denied | same-identity accepted-handoff replay only; otherwise `member-already-active` | valid pending invitation | denied |
| `GET /api/me` | allowed | machine projection | allowed | minimal identity + membership state | minimal disabled status |
| protected `POST /logout` | allowed | not applicable | allowed | allowed | allowed |
| `GET /api/workspace` | all human projection | all machine projection, `currentMember=null` | self + household projection | denied | denied |
| Mint/revoke Owner claim | denied without bearer | allowed | denied | denied | denied |
| Accept Owner claim | same-identity replay only | denied | valid recovery claim may replace Owner | valid bootstrap/recovery claim | denied |
| Accept Family invitation | denied as already a member | denied | same-identity accepted-handoff replay only; otherwise `member-already-active` | valid pending invitation | denied |
| Invitation/member/offboarding/audit APIs | allowed | allowed | denied | denied | denied |
| `/api/about`, `/api/v2/catalog/apps` | allowed | allowed | allowed | denied | denied |
| Legacy `/api/catalog/apps*`, import/upload/inspect roots, and GitHub source/connection APIs | allowed | allowed | denied | denied | denied |
| Apple access, anisette, raw system status, global logs | allowed | allowed | denied | denied | denied |
| Global `GET /api/onboarding/status` and completion | allowed | allowed | denied | denied | denied |
| Raw device discovery/diagnostics | allowed | allowed | denied; enrollment operation exposes only safe candidate guidance | denied | denied |
| Known devices and installed-app reads | all | all | own safe projection | denied | denied |
| Device enrollments | active target required | active target required | self, first active iPhone only | denied | denied |
| Device create/delete and private metadata | allowed; no reassignment in v1 | allowed; no reassignment in v1 | own display patch only | denied | denied |
| App registrations and renewals | all | all | safe own projection; approved-catalog create/remove only | denied | denied |
| Operation preflight/install/refresh/list/get | all | all | own approved target | denied | denied |
| Operation cancel/retry/rerun/reconcile | all | all | own target plus own actor | denied | denied |
| Scheduler status/settings | allowed | allowed | denied; owned app/device DTOs carry refresh state | denied | denied |
| Diagnostic issues | all + triage | all + triage | own read projection, no triage | denied | denied |

`GET /login` accepts only a normalized relative path under `/` and rejects
scheme-relative paths, backslashes, encoded control characters, and external
origins. Logout is `POST /logout` with same-origin and antiforgery validation;
the OIDC signout callback remains a state-validated GET owned by the OIDC
middleware. The GitHub callback keeps its hashed single-use state boundary and
does not inherit cookie-mutation exemptions.

#### Suspension, offboarding, and recovery receipts

`PATCH /api/workspace/members/{memberId}` permits an Owner to suspend or restore
a Family member with `expectedVersion`, `reason`, and `idempotencyKey`. It cannot
target the Owner. Suspension is committed before queued-work cancellation is
requested; authorization and scheduler eligibility read the committed status,
so failure to cancel work cannot restore access.

`POST /api/workspace/members/{memberId}/offboard` is a two-pass mutation. With
no `impactVersion`, it returns `409 offboarding-confirmation-required` and exact
counts/states for devices, registrations, queued/running operations, and future
refreshes. Confirmation supplies that short-lived version and the member's
current entity version. The server rechecks both, requires the member already
be suspended, marks it `offboarded`, cancels only safe queued work, freezes
future scheduler work, retains all resources/evidence, and returns an immutable
audit receipt. Running device/Apple work reaches a safe terminal or
`unknown`/reconciliation state and becomes Owner-only.

Offboarding never uninstalls an app, wipes a phone, revokes an Apple credential,
certificate, profile, passkey, or Authentik account, or deletes operation/audit
history. Authentik session/passkey revocation is a separate identity-provider
action; Sideport access is already denied by the member status on every request.

#### Store, audit, idempotency, and concurrency

`workspace-access.json` is a versioned atomic store under
`Sideport:State:Directory` and follows the existing process-local lock plus
temp-file/atomic-replace pattern. It contains workspace metadata, exact private
identity keys, member presentation data, invitation hashes, bounded
idempotency records, and security audit events. Public DTOs never expose the
private identity keys. Corrupt or future-version state fails closed with
`503 workspace-store-unavailable` and is never repaired by a GET or silently
overwritten.

Member/invitation mutation and its audit event are one store transaction. Audit
events contain `eventId`, action, outcome, actor kind/member ID, target
type/stable ID, timestamp, request/correlation ID, and allowlisted impact
counts/version. They never contain issuer, subject, email, network address,
token/hash, recovery material, Apple secret/identifier, IPA path/content, or raw
exception. Owner-only `GET /api/workspace/audit?cursor=&limit=` returns newest
first, with an opaque cursor and a bounded `1..100` limit.

The existing in-process operation log applies one centralized sanitizer before
retaining formatted messages or exception text. It redacts credentials/tokens,
email, OIDC identity, full UDID, private repository identity, Apple/team/cert
identifiers, and host paths. Family Activity is built from scoped operation
records and never receives `/api/logs`; Owner raw-log access does not waive
secret/PII redaction.

All workspace mutations take a 16–128 character client idempotency key. Records
are scoped to actor, action, and semantic target. Reusing a key for different
semantics returns `409 idempotency-key-reused`. Invitation acceptance is also
naturally idempotent by token and immutable identity. Entity updates require an
expected version; stale changes return `409 workspace-version-conflict`.

Retention is explicit:

- General completed idempotency entries are retained for 24 hours, capped at
  2,000, and may evict only expired entries. The underlying member/status state
  still makes a later semantic replay safe.
- Invitation and Owner-claim records retain a minimal terminal tombstone for
  the workspace lifetime: stable ID, state, creator/claimant member or actor,
  token hash, creation-idempotency-key hash, semantic digest, timestamps, and
  receipt pointer. Reusing the same create key can therefore never mint a new
  live bearer link. The store supports at most 10,000 such records and fails new
  link creation with `workspace-security-history-full` rather than silently
  evicting authority history.
- Handoff records expire after ten minutes and are removed after 24 hours;
  restoring or replaying one cannot extend its original invitation/claim.
- The owner Activity audit retains the newest 10,000 allowlisted events and
  reports that bound in the API. Immutable bootstrap/recovery/offboarding
  receipts live on the retained workspace/member/claim records and are not
  removed when the activity ring rotates.

The first implementation remains one replica with `Recreate`. No contract
claims distributed file locking or multi-replica authorization. Restart-safe
reconciliation may repeat idempotent cancellation/freeze work but never repeats
an accepted invitation, Owner replacement, or destructive operation.

Submission-time authorization is not sufficient for queued work. The worker,
scheduler, retry/rerun path, and final device/signing boundary re-read current
membership, capability, and resource ownership immediately before an external
effect. Suspension or offboarding after submission blocks
not-yet-started work; an effect already underway reaches the existing safe
terminal or unknown/reconciliation boundary.

#### Errors and migration/rollback

Required family-security errors include:

- `401 unauthorized` for missing/invalid authentication;
- `403 workspace-bootstrap-required`, `workspace-membership-required`, `member-access-disabled`,
  `capability-denied`, `origin-or-antiforgery`, and
  `recovery-proof-invalid`;
- `404 resource-not-found` and `invitation-unavailable` without cross-member
  existence disclosure;
- `409 workspace-version-conflict`, `idempotency-key-reused`,
  `owner-replacement-confirmation-required`,
  `owner-replacement-preflight-stale`, `member-already-active`,
  `offboarding-confirmation-required`, `offboarding-preflight-stale`, and
  `recovery-proof-not-configured`;
- `410 invitation-expired|revoked|already-used` only after a complete token
  matches the stored invitation and without accepted-member detail;
- `422 owner-action-required` when a Family operation reaches an Owner-only
  signer or scarcity boundary;
- `429 invitation-rate-limited|owner-claim-rate-limited` with `Retry-After`;
- `503 workspace-store-unavailable` for unreadable/unwritable/corrupt state and
  `workspace-security-history-full` when retained authority tombstones reach
  their explicit safety bound.

The migration is additive and idempotent:

1. Add `workspace-access.json`, `ownerMemberId`, operation ownership snapshots,
   and the planned endpoints while preserving legacy JSON fields.
2. With no workspace store, OIDC domain access becomes
   `workspace-bootstrap-required`; it never retains the current implicit Owner
   behavior. The recovery bearer remains available.
3. The explicit Owner claim creates the workspace. Existing free-text device
   owner values are never matched to a person. Legacy/unassigned resources stay
   Owner-only; new accepted devices receive stable ownership.
4. Only after unknown-principal, cross-member, suspension, antiforgery, replay,
   migration, and rollback tests pass may invitation creation be enabled.

Rollback to a build that makes every OIDC principal Owner is safe only before a
Family member is admitted. After admission, rollback requires first restricting
Authentik/ingress access to the Owner or otherwise disabling family OIDC access,
then draining/reconciling operations. State, pairing, signer, anisette, and app
volumes remain intact. No rollback deletes `workspace-access.json`; the older
build ignores it, and a return to the family-aware build resumes from it.

Backup restore is a separate security event because an older workspace file can
resurrect a pending invitation or predate a suspension. The workspace carries a
random `securityEpoch`, copied into each local OIDC cookie ticket and checked on
every request. Restore procedure:

1. keep Family ingress/OIDC access disabled while restoring state;
2. use recovery-bearer-only
   `POST /api/workspace/recovery/after-restore` with expected workspace version
   and idempotency key to rotate `securityEpoch`, revoke every pending
   invitation/Owner claim/handoff, and write a restore receipt;
3. review the restored member list and reapply any required suspension or
   offboarding before access reopens; and
4. reopen OIDC, forcing every browser through a new local cookie ticket.

The epoch invalidates pre-restore Sideport cookies, including cookies restored
from a browser backup. It cannot reconstruct membership changes newer than the
backup, so the explicit Owner review is mandatory and is recorded in the
restore receipt. Authentik credential/session revocation remains Authentik's
responsibility.

### Provenance and Evidence Origin

`source` retains the existing contract vocabulary:
`live|derived|demo|planned`. Availability or workflow state is not a source.
Onboarding adds a separate `evidenceOrigin` vocabulary:
`operator|device|apple|artifact|system|operation`.

For example, an installed-app/profile re-read has `source=live` and
`evidenceOrigin=device`; an operator acknowledgement for Developer Mode has
`source=live` and `evidenceOrigin=operator`. Operator acknowledgements never
become device-verified evidence. Developer Mode, developer-profile trust, and
opening the app remain guided actions; V1 never claims launch verification.

### Service and Operational Status

In V2, open `GET /readyz` is deliberately shallow. It proves only that ASP.NET
routing and the packaged admin shell are serviceable, so a recoverable signer,
anisette, or store problem cannot hide the setup UI. It does not open domain
stores and does not claim signing readiness.

Authenticated `GET /api/system/status` owns operational truth:

```json
{
  "operational": false,
  "checkedAt": "2026-07-11T12:00:00Z",
  "checks": [
    {
      "id": "anisette-headers",
      "status": "fail",
      "source": "live",
      "checkedAt": "2026-07-11T12:00:00Z",
      "scope": "apple-signer",
      "affectedResources": ["configured-apple-account"],
      "reason": "Provisioned ADI headers are unavailable.",
      "nextAction": "Restore or provision the persistent anisette identity."
    }
  ]
}
```

Required check IDs are `mutation-protection`, `state-readable`,
`state-writable`, `work-writable`, `anisette-headers`, `signer-executable`,
`device-transport`, and `operation-store`. Write checks create and delete a
random zero-byte probe in the target directory. The signer check is a bounded,
non-mutating executable invocation, not only `File.Exists`. Anisette checks are
coalesced for 30 seconds; only pass/fail/time is cached and header values are
discarded immediately. One-time anisette password material is never reused for
authentication. Checks never log a credential, token, header, or private path
contents. Stores load lazily or retain a structured load error so corrupt state
remains diagnosable through the UI.

### Onboarding Read Model and Completion Mutation

For one compatibility release, `GET /api/onboarding/status` retains the current
root `firstRunComplete`, `schedulerEnabled`, and legacy `steps` fields and their
`pending|warning|blocked|complete` vocabulary. It adds `workflow`; new clients
must read V2 and older clients may ignore it. `firstRunComplete` is true only
when the V2 receipt exists, while root `schedulerEnabled` is always derived
from the current scheduler settings store.

```json
{
  "firstRunComplete": false,
  "schedulerEnabled": false,
  "steps": [],
  "workflow": {
    "schemaVersion": 2,
    "setupState": "in-progress",
    "readyNow": false,
    "completedAt": null,
    "verifiedOperationId": null,
    "nextAction": {
      "stepId": "apple-signer",
      "action": "start-sign-in",
      "label": "Start Apple sign-in"
    },
    "steps": [
      {
        "id": "apple-signer",
        "state": "action-required",
        "required": true,
        "source": "live",
        "evidenceOrigin": "apple",
        "checkedAt": "2026-07-11T12:00:00Z",
        "reason": "A credential is configured but has not authenticated.",
        "activeOperationId": null,
        "nextAction": {
          "action": "start-sign-in",
          "label": "Start Apple sign-in"
        },
        "evidence": []
      }
    ]
  }
}
```

V2 steps are `server`, `apple-signer`, `device`, `app`, `install`, and `ready`.
Scheduler enablement is part of `install`; it is not a separate UI step.
`ready` becomes complete only when the immutable receipt exists.
`setupState` is `in-progress|complete`; a fresh deployment starts
`in-progress` and only the immutable receipt changes it to `complete`. Step
state is one of `not-started`, `action-required`, `in-progress`, `complete`, or
`blocked`. Every incomplete step has a reason and at most one `nextAction`.
`activeOperationId` is present when a durable operation currently owns that
step, including a device enrollment that began before a UDID was known; clients
resume it through `GET /api/operations/{activeOperationId}` after reload.
Workflow-level `nextAction` repeats the first available required action and is
`null` while the first incomplete step is automatically in progress or after
completion. The aggregate is derived from existing stores and live checks; it
is not a mutable workflow database and stores no per-step flags. After
completion, `completedAt` and `verifiedOperationId` come from the receipt.

`POST /api/onboarding/complete` is an idempotent compatibility and
finalization-recovery entry point. It accepts:

```json
{
  "verifiedOperationId": "op_...",
  "idempotencyKey": "ui-generated-key"
}
```

The operation ID must name an install whose persisted semantic intent included
`finishOnboarding=true`. The endpoint resumes only the first unfinished
idempotent finalization boundary: activate the already verified registration,
enable the scheduler, compute its next evaluation, then write the receipt. It
never authenticates, signs, uploads, installs, or repeats device verification.
It returns `201 Created` with the new receipt, `200 OK` with the existing
receipt for replay or already-completed setup, `409 onboarding-incomplete` with
the current workflow and blockers if required evidence is absent, or
`503 onboarding-store-unavailable` when the receipt cannot be read or written.
Its evidence snapshot contains stable IDs, timestamps, and versions only.

### Apple Account, Team, and Signer Cutover

`GET /api/apple-access/personal/status` becomes side-effect-free and adds these
required semantics without returning secrets:

- stable `accountProfileId` and redacted account hint;
- credential source plus `credentialEntry.supported`,
  `credentialEntry.allowedNow`, and a structured `blockedReason`, so the UI
  offers direct entry only when the managed store and security boundary are
  available;
- cached auth state, `lastAuthenticatedAt`, `authValidatedAt`, and an explicit
  freshness reason;
- teams from the latest successful Apple response, `selectedTeamId`, and
  `teamValidatedAt`;
- local signing-identity state, serial/fingerprint suffix, and expiry;
- the last explicitly fetched Apple certificate inventory summary;
- `impact` as `reuse`, `mint`, `replace-existing`, or `unknown`;
- latest signer-cutover operation/state and actor/timestamp; and
- one precise next action.

Auth state is `credential-configured`, `two-factor-required`,
`validated-recently`, `validation-stale`, `failed`, or `unknown`, always with a
reason and timestamp. `validated-recently` means explicit sign-in, successful
2FA completion, Apple preflight, or another successful authenticated Apple
operation succeeded within 15 minutes and its cached session is still the one
used for that validation. Status GET never signs in, fetches
teams/certificates, or asserts that an in-memory session will renew.

#### Managed credential establishment

`POST /api/apple-access/personal/connect` is planned and is not part of the
current live endpoint set. It is the direct fresh-install path for official
Docker and Apple `container` deployments whose configured credential source is
`managed`:

```json
{
  "appleId": "owner@example.com",
  "password": "write-only secret"
}
```

The request is JSON-only and at most 16 KiB. The server trims and validates the
Apple ID (1–320 characters) but preserves the password byte-for-byte (1–1024
characters). The password request type must not expose its value through a
generated `ToString`, structured logging, validation detail, or exception
message.

The endpoint enforces the following boundary before reading the request body:

- a configured bearer or OIDC actor with `apple.signer.manage`; open mode
  returns `403 mutation-protection-required`;
- effective HTTPS, or an explicitly enabled loopback-only exception on a
  listener bound to `127.0.0.1`/`::1`; a `Host: localhost` header is not proof
  of loopback;
- forwarded HTTPS is trusted only from configured proxy addresses/networks;
- same-origin requests, no credential CORS, and antiforgery validation for
  cookie/OIDC authentication;
- a production admin-shell CSP that permits only self-hosted scripts and API
  connections, sets `form-action 'self'` and `frame-ancestors 'none'`, plus an
  equivalent framing denial for older clients; third-party scripts are not
  permitted on the credential-entry surface;
- safe Apple TLS policy (`Sideport:Apple:AllowInsecureTls` must be false); and
- a pre-body client/IP rate limit, followed by a per-account limit only after
  the validated Apple ID is parsed, both with `Retry-After`.

The response is `Cache-Control: no-store` and contains only the extended,
redacted Apple status. First-account authentication and durable storage returns
`201`; a validated same-account password rotation returns `200`; Apple 2FA
returns `202` with only an opaque `pendingChallengeId`, challenge kind, and a
five-minute `expiresAt`. The candidate credential for a 2FA challenge remains
only in process memory, is single-use, and is discarded on success, failure,
expiry, or restart. Existing `POST /api/apple-access/personal/2fa` accepts that
challenge ID and exactly six decimal digits; only successful completion may
persist the candidate credential. A connect-originated challenge is bound to
its initiating actor and account profile. Its completion enforces the same
capability, effective-transport, proxy, origin/antiforgery, CSP-hosted UI, TLS,
and two-stage rate-limit boundary as `/connect` before it reads the code. An
invalid or expired code consumes the challenge and returns the UI to credential
entry; it never retries with the retained candidate.

The managed provider stores one encrypted credential envelope under the
durable Sideport state root. It uses authenticated encryption with a persisted,
purpose-scoped ASP.NET Data Protection key ring, atomic replacement, a `0700`
directory, and `0600` files owned by the non-root Sideport user. Ciphertext and
the key ring must be backed up together. This protects against accidental file
disclosure, not an attacker who can read both the state volume and key ring.
Environment/SOPS and macOS Keychain providers remain read-only compatibility
sources; when either is configured, status skips the form and the UI proceeds
with the already-custodied account. Provider choice never silently falls
through: explicit `Sideport:Apple:CredentialSource=managed|environment|keychain`
wins. Official fresh Docker and Apple `container` examples set `managed`.
Existing deployments with the setting absent retain the current one-release
`environment` default; migration status tells an unconfigured owner to select
`managed`, and a later default change requires its own compatibility gate.

Authentication must succeed before the first credential is committed or a
same-account credential version is atomically swapped. A failed candidate
leaves the previous working credential unchanged. A different Apple account
returns `409 apple-account-replacement-requires-cutover`; onboarding never
overwrites it, rebinds apps, revokes certificates, or changes the scheduler.
V2 exposes no credential-delete endpoint; removal belongs to a separately
approved offboarding/factory-reset contract.

Required structured failures are `400` validation/body-too-large, `401`
unauthorized, `403 mutation-protection-required`,
`credential-entry-transport-required`, `origin-or-antiforgery`, or
`apple-tls-policy-unsafe`, `409 credential-source-read-only`,
`apple-account-replacement-requires-cutover`, or `apple-challenge-expired`,
`422 apple-authentication-failed` or `apple-two-factor-invalid`,
`429 apple-auth-rate-limited`, `502 apple-authentication-unavailable`, and
`503 apple-credential-store-unavailable`. Raw Apple/anisette errors are sanitized
before they reach API error detail or the durable operation log.

`PUT /api/apple-access/personal/team` accepts:

```json
{ "accountProfileId": "acct_...", "teamId": "TEAMID1234" }
```

The team must be in the most recent successful Apple response. A manually
typed legacy Team ID cannot satisfy onboarding. Success is `200` with current
Apple status; failures are `404` unknown profile, `409` stale auth/team list, or
`422 apple-team-not-returned`.

The explicit signer-cutover endpoints in this subsection are **planned and not
live**. The planned
`POST /api/apple-access/personal/signing-preflight` is a read-only Apple call
with this request:

```json
{ "accountProfileId": "acct_...", "teamId": "TEAMID1234" }
```

Its expiring result is:

```json
{
  "preflightId": "signing_preflight_...",
  "expiresAt": "2026-07-11T12:10:00Z",
  "accountProfileId": "acct_...",
  "teamId": "TEAMID1234",
  "localIdentity": { "state": "missing", "expiresAt": null },
  "appleCertificates": [
    {
      "id": "cert_...",
      "serialSuffix": "A1B2",
      "expiresAt": "2026-09-01T00:00:00Z"
    }
  ],
  "impact": "replace-existing",
  "requiresAcknowledgement": true,
  "inventoryVersion": "sha256:..."
}
```

It returns `200`, `409` when auth/2FA is required, or
`503 apple-certificate-inventory-unavailable` when inventory cannot be read.

The planned `POST /api/apple-access/personal/cutover` accepts:

```json
{
  "preflightId": "signing_preflight_...",
  "inventoryVersion": "sha256:...",
  "acknowledgedCertificateIds": ["cert_..."],
  "acknowledgedImpactCodes": ["replace-existing"],
  "idempotencyKey": "ui-generated-key"
}
```

It returns `202` with a queued durable
`type=signer-cutover` operation, or `200` with the existing operation for an
idempotent replay. Unknown/expired preflight is `409`; acknowledgement mismatch
is `422`. Revalidation drift produces a durable blocked operation with
`signing-preflight-stale` and performs no revocation.

A reusable local identity requires no cutover. Otherwise, cutover stages are
`preflight`, `revalidate-certificate-inventory`,
`revoke-acknowledged-certificates` (skipped when empty), `mint-certificate`,
`persist-identity`, and `verify-identity`. Cutover and app install/refresh share
one process-wide signer gate. The operation re-fetches inventory under that
gate and may revoke only the exact certificate IDs from the confirmed
preflight. A missing acknowledged certificate is already absent; it is never
permission to revoke a replacement. Certificate creation must never call a
revoke-all path.

Before the first revoke, the operation durably records the profile, team,
inventory version, exact acknowledged certificate IDs, impact codes, actor,
risk-contract version, and running stage. The server rejects a loose boolean
acknowledgement or any certificate/impact set that differs from the current
preflight. New identity bytes are atomically persisted before their fingerprint
suffix is recorded. The authorization is single-use: replay returns the same
operation, and a linked recovery retry may only finish its already-authorized
mint/persist/verify steps. It cannot authorize another revoke set.

After interruption following an irreversible revoke, startup performs no
automatic mint or repeat revocation. The operation becomes `unknown` or
`recovery-required` and blocks signer work for that profile/team. Verify-only
reconciliation may complete a matching persisted identity or prove a linked
mint/persist retry eligible; unexpected certificate inventory always requires
a fresh preflight and confirmation.

### Device Discovery, Pairing, and Acceptance

Every discovered device exposes current connection plus `trustState` as
`trusted|untrusted|locked|error|unknown`, `trustReason`,
`lockdownCheckedAt`, and `usableForInstall`. A lockdown failure must never map
to `trusted` or `healthy`.

Known-device DTOs add `inventoryState` as
`discovered|legacy-unverified|accepted`, `acceptedAt`, `acceptedBy`, the same
live trust fields, and `supportedForFirstInstall`. In V2,
`supportedForFirstInstall=true` only for a current trusted USB connection.

All discovery, diagnostics, and status GETs open lockdown with
`autopair=false`; a read never triggers a Trust prompt, writes pairing state, or
adds a device to inventory.

`POST /api/devices/enrollments` is the one user-started Add iPhone mutation. The
existing Phase 3 request is expanded additively in Phase 9 so the combined
contract accepts:

```json
{
  "idempotencyKey": "ui-generated-key",
  "deviceUdid": null,
  "targetMemberId": null
}
```

`deviceUdid` is optional when no phone is connected yet. An active Family caller
may omit `targetMemberId` or name only its own member ID; the server always
normalizes the target to that caller and enforces the first-active-iPhone limit.
An Owner or recovery-bearer caller must name an active target member. Unknown,
suspended, and offboarded principals are denied before discovery or pairing.
The endpoint returns
`202` with a durable `type=enroll-device` operation, or `200` with the existing
operation on idempotent replay. The operation is bounded to five minutes and
uses these stages: `wait-for-usb`, `request-pairing`, `await-user-trust`,
`verify-lockdown`, and `accept-device`. Its waiting stages must not occupy the
install/refresh worker.

The operation selects exactly one unaccepted USB candidate during its active
window. With zero candidates it waits; with more than one it reaches a terminal
blocked result with `device-selection-required` and safe candidate summaries
before requesting pairing. Choosing a phone starts a new request with that
`deviceUdid` and a new idempotency key; the server revalidates that it is still
an eligible unaccepted USB candidate. An already-paired phone skips the Trust
prompt but still requires a fresh lockdown check. Wi-Fi never initiates pairing
and only consumes an existing host pairing record. After lockdown succeeds,
the operation rechecks writable inventory and persists the
device as accepted with the initiating actor and timestamp. There is no second
**Add to Sideport** confirmation.

Timeout or restart before a pairing request is retry-safe. After a pairing
request, recovery first performs a non-pairing trust check: trusted continues
to acceptance, definitely denied/unpaired fails safely, and ambiguous or
unreachable becomes `recovery-required`; it never repeats pairing
automatically. Trust denial, lock, timeout, disconnect, Wi-Fi-only discovery,
or an unknown concurrent device operation adds nothing.

The existing `POST /api/devices/known` remains for compatibility and manual
inventory outside onboarding, but the onboarding and future **Devices → Add
iPhone** UI call only the enrollment endpoint. Manual/offline records never
satisfy completion. Passive discovery never starts enrollment, even for a
previously trusted phone.

Existing known-device JSON has no reliable acceptance or lockdown evidence and
migrates to `legacy-unverified`. Only a successful live USB trust check followed
by the explicit enrollment operation upgrades it to `accepted`; enumeration or
migration alone never satisfies onboarding.

Device status may later add `developerModeState=enabled|disabled|unknown` from a
read-only lockdown probe. Until that probe passes physical-device validation,
the UI treats Developer Mode as operator guidance and never claims Sideport
enabled or verified it.

### App Library and Artifact Sources

The primary onboarding choice is the Sideport app library returned by
`GET /api/catalog/apps`. A selectable item always represents an IPA that the
server has downloaded or received, inspected, and marked `status=ready`.
Existing live sources are the configured server seed, an operator-inspected
server path, and a browser upload. The UI may merge duplicate provenance into
one app card, but the IPA metadata—not a filename or release tag—remains the
authority for bundle ID, display name, version/build, size, and checksum.

The server-path mutation is replaced additively by a confined request
`{ rootId, relativePath }`. `rootId` resolves one configured read-only import
root; the server rejects traversal and symlink escape, applies the same
compressed and inspected-entry limits as browser upload, copies the artifact
into managed Sideport storage, and never returns an absolute host path. The
legacy absolute-path request remains compatibility-only for one release and is
not used by the new UI.

GitHub release sources are planned, not live. The smallest safe contract uses
configured source IDs for public or private `owner/repository` entries. The UI
may validate `owner/repository` while connecting a source, but release listing
and import submit only the server-issued `sourceId`, numeric `releaseId`, and
numeric `assetId`; the browser never supplies a download URL. A read endpoint
lists `.ipa` release assets and immutable provenance. Selecting one starts an
authenticated catalog import that enforces the existing maximum size, streams
to temporary managed storage with timeout and checksum, inspects the IPA, and
atomically publishes it before the item becomes selectable. Sideport never
signs or installs directly from a remote URL and never treats mutable `latest`
or a tag as artifact identity.

Private source authorization prefers a GitHub App installation limited to the
selected repositories with **Metadata: read** and **Contents: read** only. No
write, administration, workflow, organization-wide, or webhook permission is
requested. Installation and repository IDs may be persisted; short-lived
installation tokens remain in memory and are never returned. The bounded
interim provider is a deployment-secret reference to a fine-grained PAT limited
to the selected repositories, Contents read, and an expiry. Sideport never
accepts a PAT in the browser or catalog JSON, never logs it, and never falls
back to classic broad `repo` scope.

Metadata calls use fixed `api.github.com` paths. Asset download follows at most
three manually validated HTTPS redirects to the exact GitHub asset host
allowlist, strips Authorization on every cross-host redirect, rejects IP
literals/private/loopback/link-local destinations, counts streamed bytes, and
cleans temporary files on every failure. GitHub response bodies, signed URLs,
tokens, repository-private URLs, and host storage paths never enter public DTOs,
operation records, or logs.

#### GitHub source connection

`POST /api/v2/catalog/github/connections` requires
`integrations.github.manage` and accepts:

```json
{
  "repository": "owner/repository",
  "visibility": "private",
  "idempotencyKey": "ui-generated-key"
}
```

Only the two-segment `owner/repository` shape is accepted. Public connections
are validated against the fixed GitHub API and may complete synchronously.
Private connections require a configured GitHub App and return `202` with a
redacted connection DTO whose `status=authorization-required` and whose
`authorizationUrl` is a server-built
`https://github.com/apps/{configuredSlug}/installations/new?state={opaqueState}`
URL. The 256-bit opaque state is present explicitly; the URL contains no
Sideport credential.

`GET /api/v2/catalog/github/connections/{connectionId}` returns only the actor's
or an owner's redacted status: `validating|authorization-required|connected|
failed|expired`, repository, visibility, safe permission summary, expiry, and
the resulting `sourceId` when connected. It never returns tokens, GitHub App
private-key details, signed asset URLs, or upstream response bodies.

`GET /github/setup/callback` is the configured GitHub App setup callback and is
intentionally outside `/api`; GitHub cannot attach Sideport's bearer header.
Before navigation, Sideport persists a random 256-bit, five-minute, single-use
state hash bound to the initiating authenticated actor, connection ID,
repository request, installation intent, and allowed return origin. The
callback authenticates the exchange with that opaque state, consumes it
atomically, rejects replay/expiry/binding mismatch,
validates `installation_id` through GitHub, verifies the exact selected
repository ID is installed, and verifies that effective permissions contain
exactly Metadata read and Contents read, with every other repository,
organization, and account permission absent or `none`. Only then does it
persist installation ID, repository ID, repository name, and the Sideport
source ID. A callback never accepts a token or repository identity as proof,
never creates a Sideport login session, and redirects only to the configured
same-origin UI status route. The initiating bearer/OIDC actor (or an owner) must
still authenticate to read the connection status.

The GitHub App ID/slug and private-key **file path** are deployment
configuration. The key file is a read-only secret mount, is never copied into
Sideport JSON, and its content is never exposed or logged. Sideport signs a
short-lived App JWT, mints a repository-restricted installation token, caches
that token in memory only, and discards it at least five minutes before expiry.
If GitHub App custody is unavailable, the UI reports the deployment remedy; it
does not ask for a browser PAT. A configured fine-grained PAT secret reference
may supply an already-configured source but does not make the interactive
connection endpoint claim GitHub App authorization.

Connection errors are `github-app-not-configured`, `github-state-invalid`,
`github-state-expired`, `github-state-replayed`, `github-installation-invalid`,
`github-repository-not-selected`, `github-permission-insufficient`,
`github-rate-limited`, and `github-upstream-unavailable`. Authentication and
authorization are checked before state is created and whenever status is read;
the callback itself is authorized only by the bound single-use state.

#### GitHub release listing and import

`GET /api/v2/catalog/github/sources` returns an envelope with a redacted
provider capability (`kind`, `supported`, `allowedNow`, `blockedReason`, and
the exact Metadata-read/Contents-read permission summary) plus redacted source
summaries. The UI enables connection only when `supported && allowedNow`.
`GET /api/v2/catalog/github/sources/{sourceId}/releases?page=1` returns at most 20
non-draft releases allowed by source policy and only `.ipa` asset summaries:
numeric release/asset IDs, tag/name, publish/update timestamps, byte size,
optional upstream digest, and importability. Neither response contains API or
download URLs.

`POST /api/v2/catalog/apps/import-github` requires `catalog.import` and accepts:

```json
{
  "sourceId": "ghsrc_01",
  "releaseId": 123456,
  "assetId": 789012,
  "idempotencyKey": "ui-generated-key",
  "expectedDigest": "sha256:optional-upstream-value",
  "catalogId": "cert-clock",
  "expectedCatalogVersion": 3
}
```

`catalogId`, `expectedDigest`, and `expectedCatalogVersion` are optional. The
server re-fetches release metadata, verifies configured repository ID plus
release/asset identity, draft/prerelease policy, `.ipa` extension, and byte
limit, then downloads and inspects before atomic publish. `201` returns the new
path-free `CatalogAppDto`; exact actor/key/semantic-target replay or exact
immutable-source/checksum replay returns `200` without another download.
Reusing a key for a different target, or changing an existing catalog ID
without its current version, returns `409` and preserves the prior item.

Import errors are `github-source-not-found`, `github-credential-unavailable`,
`github-release-not-found`, `github-asset-not-found`, `github-asset-not-ipa`,
`github-asset-too-large`, `github-asset-changed`, `github-rate-limited`,
`github-upstream-unavailable`, `github-download-timeout`,
`github-redirect-rejected`, `catalog-id-conflict`,
`catalog-version-conflict`, `ipa-inspection-failed`, and
`catalog-store-unavailable`. Error DTOs never include upstream bodies, URLs,
tokens, or storage paths.

`POST /api/v2/catalog/apps/upload` is the browser/computer counterpart. It uses
multipart fields `ipa`, optional `id`, `name`, `purpose`, `idempotencyKey`, and
`expectedCatalogVersion`; it applies the same byte/ZIP/inspection/atomic-publish
limits and returns the same path-free V2 `CatalogAppDto`. Exact replay returns
`200` without rewriting the artifact. It does not accept the V1 `replace=true`
shortcut: changing an existing ID requires its current catalog version.

#### Configured server import roots

`GET /api/v2/catalog/import-roots` returns only `{ id, label, available, source }`
for configured read-only roots; it never returns host paths and does not provide
arbitrary filesystem browsing. `POST /api/v2/catalog/apps/inspect` uses this V2
request:

```json
{
  "rootId": "shared-ipas",
  "relativePath": "CertClock/CertClock.ipa",
  "id": "cert-clock",
  "name": "Cert Clock",
  "purpose": "Certificate expiry helper",
  "expectedCatalogVersion": 3
}
```

The server resolves the canonical target under the configured canonical root,
rejects rooted input, `..`, symlink/reparse escape, non-regular files, and
case/canonicalization ambiguity, then applies upload and inspected-ZIP limits
and copies into managed storage before publishing. Errors are
`catalog-root-not-found`, `catalog-path-invalid`, `catalog-path-outside-root`,
`catalog-source-not-found`, `catalog-source-too-large`,
`ipa-inspection-failed`, `catalog-version-conflict`, and
`catalog-store-unavailable`.

`GET /api/v2/catalog/apps` and every V2 catalog mutation use a path-free DTO:

```json
{
  "id": "cert-clock",
  "catalogVersion": 3,
  "name": "Cert Clock",
  "bundleId": "com.example.certcountdown",
  "source": "live",
  "status": "ready",
  "sizeBytes": 12345,
  "sha256": "...",
  "artifactSources": [{ "kind": "browser-upload", "label": "This computer" }],
  "lastInspectedAt": "2026-06-24T10:00:00Z",
  "notes": []
}
```

An internal catalog record retains the managed storage path for signing and
migrates existing `ipaPath` JSON without rewriting on read. No V2 response
returns `ipaPath`. The existing `GET /api/catalog/apps`, upload, and inspect V1
responses retain their current shape for one compatibility release and are
marked deprecated; the approved UI uses only V2. Registration by `catalogAppId`
resolves the internal ready artifact server-side; legacy direct-path
registration is compatibility-only and owner-equivalent.

The additive planned fields are:

```json
{
  "artifactSources": [
    { "kind": "server", "label": "On this server" },
    {
      "kind": "github-release",
      "label": "GitHub release",
      "repository": "dragoshont/sideport",
      "releaseTag": "sample-apps",
      "assetName": "CertCountdown.ipa"
    }
  ],
  "icon": null
}
```

`icon=null` uses a generated initial/tone fallback. A later inspector may
extract an app icon into trusted catalog storage; clients never render arbitrary
remote HTML or SVG as an app icon.

`GET /api/devices/{udid}/installed-apps` is metadata only. It never supplies IPA
bytes and cannot be used as a signing source. The UI may match an installed
bundle ID to a ready catalog artifact and direct the user to that library item;
otherwise it shows **IPA file needed**. It must never claim Sideport can copy or
re-sign an app directly from the iPhone.

### Pending Registration and Install Preflight

`AppRegistration` adds `lifecycle` (`pending-install|active`), `catalogAppId`,
`createdAt`, `activatedAt`, and `lastVerifiedOperationId`. Onboarding creates a
`pending-install` registration. Missing `lifecycle` on legacy JSON defaults to
`active`, but a legacy record without `lastVerifiedOperationId` remains
`verification-required` and scheduler-ineligible.

`POST /api/apps` accepts the V2 identity/artifact references:

```json
{
  "catalogAppId": "example-app",
  "deviceUdid": "000081...",
  "accountProfileId": "acct_...",
  "lifecycle": "pending-install"
}
```

The server resolves Apple ID, selected team, bundle ID, and durable IPA path.
Its response is an app-registration DTO with `appleIdHint`, never the storage
record or a credential. `201` creates the pending registration and natural-key
replay returns `200`. Slot/registration conflict is `409`; catalog/account/
device validation is `422`. Legacy `appleId`, `teamId`, and `inputIpaPath`
remain accepted for API compatibility but are not used by the onboarding UI,
and a manual team ID does not satisfy V2.

`POST /api/operations/preflight` supports `type=install`. It is read-only and checks
operational status, accepted/current trusted USB device, catalog integrity and
bundle ID, pending registration and three-registration limit, authenticated
account and selected returned team, the exact development-certificate inventory
that the current Apple interface can read, persisted signer identity/cutover
state, single-flight availability, and scheduler eligibility after activation.
The current Apple interface does **not** expose safe exact read-only inventory
for registered devices, App IDs, or provisioning profiles. Preflight therefore
reports their ensure operations as planned install mutations; it does not claim
those three resources already exist. Install performs the idempotent ensure
operations only after the confirmed plan is submitted.

```json
{
  "type": "install",
  "deviceUdid": "000081...",
  "bundleId": "com.example.app",
  "finishOnboarding": true
}
```

The response retains `ready`, `target`, `blockers`, `warnings`,
`plannedMutations`, `scarceLimits`, `requiresConfirmation`, and `source`; it
adds `preflightId`, `expiresAt`, grouped checks, signing `inventoryVersion`, and
`planVersion`. `planVersion` is a server-generated semantic digest of the
selected account/team/device/artifact, scarce limits, planned external
mutations, and `finishOnboarding`. Timestamp-only changes do not change it. The
short-lived preflight record is process-local; restart or expiry requires fresh
review inside the Install step; it does not add a separate Review step.

Submission reruns preflight under the submission lock. It queues only if the
new semantic plan exactly matches the confirmed `planVersion`. Any changed
target, limit, certificate impact, blocker/warning, or planned mutation returns
`409 install-preflight-stale` with the replacement preflight and enqueues
nothing. The UI must review and confirm the replacement.

### Install, Device Verification, Reconciliation, and Refresh

`POST /api/operations/install` accepts:

```json
{
  "deviceUdid": "000081...",
  "bundleId": "com.example.app",
  "preflightId": "install_preflight_...",
  "planVersion": "sha256:...",
  "finishOnboarding": true,
  "confirmedPlannedMutations": true,
  "idempotencyKey": "ui-generated-key"
}
```

A ready request returns `202 Accepted`, the complete initial operation record,
and `Location: /api/operations/{operationId}`. The same idempotency tuple
returns `200` with the existing record and never enqueues twice. A terminal
blocked preflight may return a durable `201` blocked operation. Confirmation
mismatch is `422`; operation-store failure is `503`.

Install stages are `preflight`, the consolidated `install` stage (Apple session,
signing preparation, signing, and device upload), `verify`,
`activate-registration`, `enable-scheduler`,
`compute-next-evaluation`, and `write-completion-receipt` when
`finishOnboarding=true`. The flag is stored in the durable operation intent
before any external effect and must exactly match the value covered by
`planVersion`. For installs outside first-run onboarding it is false/absent and
the three onboarding-finalization stages are omitted. Each stage has status,
timestamps, duration, redacted message, structured error, and recovery action.
Only backend callbacks at real boundaries advance stages; the UI never
simulates progress.

The finalization write order is fixed: durable device verification first,
registration activation second, scheduler enablement third, next-evaluation
computation fourth, and the immutable completion receipt last. The install
operation is not terminal `succeeded` for onboarding until the receipt is
durable, and the UI must not show **Ready** before that receipt is returned by
the read model. A crash or store failure after durable verification records the
exact unfinished finalization stage and exposes **Retry finishing setup**. That
recovery calls the idempotent completion finalizer above and resumes at the
first unfinished boundary without authenticating, signing, uploading,
installing, or repeating device verification.

The 180-second watchdog applies to the device upload/install stage only, not
queue wait or the whole operation. Releasing a lock requires proof that the
managed transfer terminated cooperatively, after Sideport closed every owned
AFC/installation-proxy transport socket, or at a killable helper-process
boundary. A deadline remains `unknown` even when hard abort terminates the task,
because the phone may already have accepted part or all of the mutation; only
verify-only reconciliation can establish the result or make rerun safe. If the
underlying task remains active, the durable operation becomes `unknown`, the
signer/device lease remains held, the device is quarantined from manual and
scheduled work, and retry is unavailable. A cancellation token firing without
confirmed task termination is not proof that the external mutation stopped.

Verification re-reads installed apps and provisioning profiles from the
physical device. The operation result records `success`, `bundleId`,
`expiresAt`, and (for legacy-registration migration) `version`; the target
supplies `deviceUdid`, while the `verify` stage's `completedAt` is the durable
verification timestamp. The API does not currently return a second nested
verification object.

Only this verification activates the registration and writes
`lastVerifiedOperationId`. Verification failure leaves it `pending-install`.
The result has no `launchVerified` field in V1.

`POST /api/operations/{operationId}/reconcile` accepts:

```json
{
  "idempotencyKey": "ui-generated-key",
  "note": "Optional operator note."
}
```

It is verify-only: it never repeats pairing,
revocation, minting, signing, or installation. It creates a linked durable
reconciliation operation only for `unknown`, `recovery-required`, or
verification-failed install/cutover work. For install it rechecks USB/trust,
bundle, version, and profile expiry. Matching device evidence succeeds and
idempotently activates the registration. App absence plus proof that no task or
lease is active may record `safeToRerun=true`; active task/lease is
`409 device-operation-still-active`; unreachable or ambiguous state remains
blocked and non-retryable. The original record stays immutable. Rerun is
available only after reconciliation proves it safe.

`POST /api/apps/{udid}/{bundleId}/verify` accepts:

```json
{ "idempotencyKey": "ui-generated-key" }
```

It queues a
`verify-existing-registration` operation for migration. It requires current
accepted/trusted USB plus an inspectable saved IPA, and only reads the installed
bundle, version, and profile expiry. A match first persists that device evidence,
then writes `lastVerifiedOperationId`, and only then marks the operation
succeeded. Restart may resume the final linking write without another device
read. Absence, version mismatch, or missing/expired profile produces a durable
blocked operation that directs the owner to Install. It never pairs, signs, or
installs, and it never satisfies first-run completion; first run still requires
the confirmed install/finalization path and its complete signer/device/artifact
lineage.

No refresh entry point bypasses these guards. Operation refresh, scheduler,
retry/rerun, and the legacy refresh endpoint use one full preflight and queued
operation service. Refresh requires an active device-verified registration,
usable identity and cutover state, operational dependencies, either a current
trusted USB connection or a usable saved pairing for the current Wi-Fi device,
and no unknown operation or held lease. A Wi-Fi bulk transfer is bounded; a
timeout, dropped socket, or ambiguous post-install read becomes `unknown`,
holds or quarantines the device lease, and requires reconciliation instead of
an automatic retry. It never implicitly mints or revokes a certificate. For
one compatibility release the legacy refresh route becomes a deprecated `202`
wrapper returning the same operation/Location, rather than calling the
orchestrator synchronously.

### Scheduler Status and Settings

V2 always hosts the operation scheduler, but it checks a durable settings
record before each evaluation. Bootstrap config seeds the store only when no
record exists. The first V2 migration seeds `enabled=false` unless every V2
prerequisite already exists; legacy `Enabled=true` is reported as requested,
not silently activated.

`GET /api/scheduler/status` returns:

```json
{
  "enabled": true,
  "checkedAt": "2026-07-11T12:00:00Z",
  "policy": {
    "mode": "due-only",
    "evaluationInterval": "01:00:00",
    "refreshLeadTime": "2.00:00:00",
    "resignInterval": null,
    "catchUp": "evaluate-on-startup",
    "missedIntervals": "not-replayed"
  },
  "nextEvaluationAt": "2026-07-11T13:00:00Z",
  "lastEvaluation": {
    "evaluationId": "sched_...",
    "startedAt": "2026-07-11T12:00:00Z",
    "completedAt": "2026-07-11T12:00:01Z",
    "outcome": "succeeded",
    "blockedCount": 0,
    "skippedCount": 0
  },
  "dueCount": 0,
  "queuedCount": 0,
  "concurrency": {
    "maxRunning": 1,
    "lockState": "idle",
    "operationId": null
  },
  "historyRetention": { "maxEvaluations": 100 },
  "source": "live"
}
```

`PUT /api/scheduler/settings` accepts only `{ "enabled": true|false }` in V1.
An identical request is an idempotent `200` no-op. Enabling requires at least
one active verified registration, a valid signer identity, and a usable paired
device currently reachable through trusted USB or saved-pairing Wi-Fi;
otherwise it returns
`409 scheduler-prerequisites-not-met`. Store failure is
`503 scheduler-store-unavailable`. Manual run-now and custom schedule windows
are out of scope.

Every startup/hourly evaluation writes a bounded receipt, including when no app
is due, and rechecks operational status, identity/cutover state, registration
eligibility, and either current trusted USB or a usable saved pairing over Wi-Fi
per due device. Offline, unpaired, unknown-operation, or dependency-failed
targets are recorded blocked/skipped and are not queued. A paired Wi-Fi target
may be queued under the bounded transfer and reconciliation rules above. Due
state comes from the latest durable successful verified operation per
registration; a newer failed operation does not erase the last verified
expiry. The UI must state that one-time USB pairing enables later refresh over
the same Wi-Fi network and that USB is the fallback when a wireless refresh
cannot finish.

### Operation Model, Status Codes, and Idempotency

The existing operation record remains the only job model. Its target adds
`kind`: `app` has `deviceUdid`/`bundleId`, `device` has `deviceUdid`,
`device-enrollment` permits `deviceUdid=null` until one USB candidate is
selected, `signer` has `accountProfileId`/`teamId`, and `reconciliation` has
`parentOperationId` plus the original target. Irrelevant fields are absent.
Legacy refresh records infer `kind=app` during the compatibility window.
Operation status adds `recovery-required`; typed results add `verification`,
`safeToRerun`, and `reconciledOperationId`. Existing server-produced
cancel/retry/rerun capability flags remain authoritative. Cancellation is
available only while the record says it is cancelable; running Apple/device
mutation stages remain non-cancelable until a proven safe boundary exists.

| Endpoint | Success | Principal failure rules |
| --- | --- | --- |
| `POST /api/apple-access/personal/connect` | `201` first account; `200` same-account rotation; `202` 2FA challenge | `403` auth/transport/origin/TLS policy; `409` read-only source/account replacement/challenge expiry; `422` Apple auth/2FA; `429` rate limit; `502/503` upstream/store unavailable |
| `POST /api/apple-access/personal/2fa` for a connect challenge | `201` first account or `200` same-account rotation after commit | Same boundary as connect; `409` expired/consumed challenge; `422` invalid code; `429` rate limit; `503` store unavailable |
| `PUT /api/apple-access/personal/team` | `200` current status | `404` profile; `409` stale auth/team list; `422 apple-team-not-returned` |
| `POST /api/apple-access/personal/signing-preflight` | `200` expiring preflight | `409` auth/2FA required; `503` inventory unavailable |
| `POST /api/apple-access/personal/cutover` | `202` queued; replay `200` existing | `409` expired/unknown preflight; `422` acknowledgement mismatch; drift creates blocked operation |
| `POST /api/devices/enrollments` | `202` queued; replay `200` | Submission: `409` conflicting active enrollment; `422` selected device ineligible. Selection, Trust denial, lock, disconnect, timeout, and recovery are durable terminal results after `202` and add nothing. |
| `POST /api/devices/known` | Compatibility/manual inventory only | Does not satisfy onboarding without enrollment evidence |
| `POST /api/apps` | `201` pending registration; natural-key replay `200` | `409` slot/conflict; `422` catalog/account/device validation |
| `POST /api/operations/preflight` with `type=install` | `200` including `ready=false` | `400` malformed target; `503` required store/probe unavailable |
| `POST /api/operations/install` | `202` queued; replay `200`; terminal blocked `201`; with `finishOnboarding=true`, success includes a durable completion receipt | `409 install-preflight-stale`; `422` confirmation/finish-intent mismatch; `503` store unavailable |
| `POST /api/operations/{id}/reconcile` | `202` queued child; replay `200` | `404` operation; `409` unsupported/active task; `503` store/probe unavailable |
| `POST /api/apps/{udid}/{bundleId}/verify` | `202` queued; replay `200` | `404` registration; `409` pending registration/idempotency/active-or-unresolved device work; `422` accepted USB/trust or saved-artifact blocker; device absence/version/profile mismatch becomes a durable blocked operation |
| Operation and legacy refresh | `202` queued; replay `200` where key is supported | `409` unverified/cutover/quarantine; `422` pairing/transport/confirmation blocker |
| `PUT /api/scheduler/settings` | `200` current status | `409 scheduler-prerequisites-not-met`; `503` store unavailable |
| `POST /api/onboarding/complete` | `201` receipt; replay `200`; recovery resumes finalization only | `409 onboarding-incomplete` or mismatched verified operation/intent; `503` receipt store unavailable |

Operation idempotency keys are scoped to actor, operation type, and semantic
target. Cutover, enrollment, install, verification, and reconciliation return the
existing operation for the same tuple. Device/app upserts are idempotent by
stable natural key. A stale preflight never authorizes an external effect:
install and cutover repeat safety reads under the server lock before the first
Apple/device mutation.

In addition to endpoint-specific codes above, V2 reserves these structured
errors:

- Apple: `apple-credential-missing`, `apple-authentication-failed`,
  `apple-two-factor-required`, `apple-challenge-expired`,
  `apple-team-not-returned`, `apple-team-selection-stale`,
  `signing-preflight-stale`, `signing-cutover-required`,
  `signing-identity-corrupt`, and
  `apple-certificate-inventory-unavailable`.
- Device: `device-not-discovered`, `device-selection-required`,
  `device-lockdown-untrusted`, `device-locked`, `device-usb-required`,
  `device-trust-check-unavailable`, `device-enrollment-timeout`,
  `device-enrollment-disconnected`, and
  `device-enrollment-recovery-required`.
- Install/reconciliation: `install-preflight-stale`,
  `device-operation-still-active`, and the existing structured operation/store
  errors.
- Scheduler/onboarding/auth: `scheduler-prerequisites-not-met`,
  `scheduler-store-unavailable`, `onboarding-incomplete`,
  `onboarding-store-unavailable`, and `mutation-protection-required`.

Apple protocol failures must map to structured responses and never escape as an
undifferentiated 500.

### Migration, Compatibility, and Recovery Invariants

V2 uses additive JSON changes and separate atomic stores:

- Missing registration `lifecycle` deserializes as `active`; existing records
  still require verify-existing evidence before scheduler eligibility.
- Existing known devices deserialize as `legacy-unverified`, never implicitly
  trusted or accepted.
- Apple signing configuration, scheduler settings/evaluations, and onboarding
  completion use separate JSON files under `Sideport:State:Directory`.
- Existing known-device and operation fields remain readable. Existing refresh
  targets infer `kind=app` for the compatibility window.
- Every store uses temp-file plus atomic replace behind the existing
  process-local lock. Corrupt JSON produces structured readiness/API failure
  and is never silently overwritten.

There is no cross-file transaction. Safety relies on durable operation evidence,
fixed write order, and idempotent startup reconciliation:

1. Cutover records confirmed inventory and its running stage before revoke;
   identity bytes persist before fingerprint/config/terminal operation state.
2. Install records successful device verification before setting registration
   `active`, `activatedAt`, and `lastVerifiedOperationId`.
3. Legacy-registration verification records successful bundle/version/profile
   evidence before linking `lastVerifiedOperationId`, then marks the operation
   terminal. Startup may resume only the link/terminal writes from that evidence.
4. For an operation with `finishOnboarding=true`, registration activation is
   followed by scheduler enablement and a persisted next-evaluation value; the
   immutable completion receipt is written last. Only then is onboarding install
   terminal `succeeded` and eligible to render **Ready**.
5. Startup or an explicit finalization retry may resume those idempotent writes
   from the first unfinished boundary using the verified operation's durable
   intent. It never repeats authentication, signing, upload, installation, or
   device verification.

Reconciliation completes before scheduler evaluation starts. Ambiguous evidence
stays blocked. Rollback must first disable scheduling, drain queued/running work,
reconcile unknown/recovery-required work, and resolve every `pending-install`
registration because an older scheduler does not understand lifecycle. Neither
Sideport state nor anisette identity volumes are deleted. Apple certificate
revocation is never automatically rolled back.

### USB Pairing, Wi-Fi Refresh, and Device-Verification Truth

USB is the supported pairing, acceptance, and first-install transport in V2.
Wi-Fi discovery is useful evidence but yields `device-usb-required` for those
first-run mutations. After a successful USB pairing and verified first install,
the saved pairing may be used for scheduled or manual refresh over the same
Wi-Fi network. USB remains supported and is the immediate fallback if a
wireless transfer cannot finish. A phone must be reachable, explicitly paired,
accepted, and live-trust checked. Discovery alone is never trust.

The first install requires a post-install USB read of the requested bundle and
provisioning profile, including signature expiry. A later Wi-Fi refresh may use
the managed device read over the same trusted session, but a missing/ambiguous
wireless read never becomes success and never triggers an automatic duplicate
install; it becomes `unknown` and requires reconciliation, normally over USB.
A queued request, completed upload call, operator acknowledgement, home-screen
observation, or external `ideviceinstaller -n` result is not the verification
evidence used by the contract. Sideport does not claim that the app launched.
Unknown transfer state quarantines the device from another mutation until
reconciliation. Its in-process lease is preserved until the managed transfer
task terminates; confirmed owned-socket hard abort may terminate that task and
release the lease, but never converts the unknown mutation into success or an
automatic retry.

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

Verify-only `POST /api/operations/{id}/reconcile` is live as specified in the
Onboarding V2 operation table above. It is distinct from the legacy-registration
verification endpoint and never pairs, signs, or installs.

## Planned Endpoints Not Yet Live

- `POST /api/apple-access/personal/signing-preflight`: exact read-only
  development-certificate impact inventory for an explicit cutover flow.
- `POST /api/apple-access/personal/cutover`: impact-confirmed signer replacement;
  no current endpoint revokes a certificate.
- `POST /api/workspace/owner-claims`,
  `POST /api/workspace/owner-claims/{claimId}/revoke`, `POST` and authenticated
  `GET /api/workspace/owner-claims/handoff`, and
  `POST /api/workspace/owner-claims/accept`: bearer-mint/revoke,
  short-lived opaque handoff, post-login preview, and explicit OIDC acceptance
  for Owner bootstrap/recovery.
- `POST /api/workspace/invitations`,
  `POST /api/workspace/invitations/{invitationId}/revoke`, `POST` and
  authenticated `GET /api/workspace/invitations/handoff`, and
  `POST /api/workspace/invitations/accept`: create, revoke, safely hand off,
  preview after login, and atomically consume a private Family invitation.
- `PATCH /api/workspace/members/{memberId}` and
  `POST /api/workspace/members/{memberId}/offboard`: suspend, restore, preflight,
  and retain an offboarded Family member.
- `GET /api/workspace/audit`: owner-only redacted security audit.
- `POST /api/workspace/recovery/after-restore`: bearer-only security-epoch
  rotation and pending-authority revocation after backup restore.

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
  "inventoryState": "accepted",
  "acceptedAt": "2026-06-24T10:00:30Z",
  "acceptedBy": "api-token-client",
  "enrollmentOperationId": "op_enroll_01",
  "trustState": "trusted",
  "trustReason": "Lockdown session verified over USB.",
  "lockdownCheckedAt": "2026-06-24T10:05:00Z",
  "usableForInstall": true,
  "supportedForFirstInstall": true,
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
- `inventoryState` is `discovered|legacy-unverified|accepted`. Only the bounded
  enrollment operation writes `accepted`, `acceptedAt`, `acceptedBy`, and
  `enrollmentOperationId` after a live USB lockdown check.
- Manual `POST /api/devices/known` records remain `discovered`; they can carry
  metadata but never satisfy onboarding or install trust prerequisites.
- Missing acceptance evidence in legacy JSON loads as `legacy-unverified`, not
  trusted or accepted.
- Live trust fields are current observations and may regress without deleting
  durable acceptance history. `supportedForFirstInstall=true` requires a
  current trusted USB observation.
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
  "catalogVersion": 1,
  "name": "Cert Clock",
  "bundleId": "com.example.certcountdown",
  "source": "live",
  "status": "ready",
  "sizeBytes": 12345,
  "sha256": "...",
  "lastInspectedAt": "2026-06-24T10:00:00Z",
  "artifactSources": [{ "kind": "browser-upload", "label": "This computer" }],
  "notes": ["No embedded provisioning profile was found; Sideport must sign this IPA before install."]
}
```

Rules:

- Upload/import stores and inspects the IPA. It does not register, sign, or install.
- Public catalog DTOs never expose the internal managed storage path.
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

This is the live Phase 6 compatibility shape. Its `advisory` roles and
Owner-equivalent OIDC behavior are superseded as an implementation target by
**Family Membership and Authorization — planned Phase 8 contract** above. It
must be removed with the compatibility UI after Phase 9 lands; it must not be
used to admit a Family principal.

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
- No invitation, member, Owner recovery, or offboarding mutation is live yet.
  ADR 0002 now decides the identity/membership boundary; Phase 9 implements it.

Capability/auth-scope table for this roadmap:

| Endpoint | Required capability | Enforcement in this phase |
| --- | --- | --- |
| `GET /api/devices/known` | `devices.read` | server-enforced when auth principal is known; bearer token is owner-equivalent |
| `POST /api/devices/enrollments` | `devices.manage` | server-enforced authenticated user-started enrollment; passive discovery never calls it |
| `POST/PATCH/DELETE /api/devices/known` | `devices.manage` | advisory for OIDC claims unless configured; bearer token is owner-equivalent |
| `POST /api/catalog/apps/upload` | `catalog.import` | advisory for OIDC claims unless configured; bearer token is owner-equivalent |
| `POST /api/v2/catalog/apps/inspect` | `catalog.import` | new UI uses configured-root requests; legacy absolute path remains owner-equivalent compatibility only |
| `GET /api/v2/catalog/github/sources/{sourceId}/releases` | `catalog.read` | source allowlist and repository identity are always server-enforced |
| `POST /api/v2/catalog/apps/import-github` | `catalog.import` | authenticated owner-equivalent caller plus configured source/repository enforcement |
| GitHub source authorization changes | `integrations.github.manage` | owner-equivalent only; credentials remain deployment/GitHub-App custody and are never returned |
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
  `canceled`, `blocked`, `succeeded`, `failed`, `unknown`, and
  `recovery-required`.
- `cancelable=true` only for queued/waiting operations in this roadmap. Running
  Apple/device mutation stages are non-cancelable until a safe boundary is proven.
- Retry/rerun creates a new operation after fresh preflight. Historical records
  stay immutable except for status transitions owned by the worker.
- An enrollment retry after `request-pairing` first performs a non-pairing trust
  check. Trusted continues to acceptance, definitely denied/unpaired fails
  safely, and ambiguous evidence remains `recovery-required`; retry/rerun never
  repeats the pairing request blindly.
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
- Apple `container` support targets version 1.1 or newer on Apple
  silicon/macOS 26. It runs the current `linux/amd64` Sideport image with
  official Rosetta translation and anisette as a separate native container on
  one explicit network. Both state roots use persistent named volumes.
- Apple `container` has no released Compose contract for this deployment. Use
  two explicit official-CLI runs, configure the full anisette container FQDN
  instead of relying on a bare Compose service name, and forward macOS
  `/var/run/usbmuxd` into Sideport. Raw USB passthrough is neither required nor
  claimed. Do not mount protected macOS `/var/db/lockdown` by default because
  Sideport requests the pairing record from usbmuxd before filesystem fallback.
- Apple `container` remains experimental until the physical gate proves
  non-root socket forwarding, first USB install, named-volume restart, and a
  bounded paired-Wi-Fi refresh with safe USB fallback.
- IaC changes remain plan-only until human approval.
