# Sideport Onboarding — Implementation Plan

Date: 2026-07-11
Status: Phase 6 runtime binding in progress; deterministic and semantic gates pending
Scope: fresh Docker or Apple `container` deployment → safe Apple signer → accepted iPhone → verified first install → automatic USB/paired-Wi-Fi refresh enabled
Implementation: Storybook and backend contracts implemented; runtime UI binding under verification; deployment remains separate

## Outcome

A new Sideport deployment must be operable from the admin UI without relying on
hidden CLI steps after deployment configuration is supplied. The UI uses six
macro steps—Check Sideport, Connect Apple, Connect iPhone, Choose app, Install,
and Ready—while still enforcing Personal Apple ID/team/signer safety, trusted
device acceptance, read-only preflight, device-side verification, automatic
refresh enablement, and an immutable completion receipt inside those steps.

The product must distinguish two truths:

- `setupState=complete` is durable historical evidence that the full first-run
  path succeeded at least once.
- `readyNow` is a live assessment and may regress when Apple authentication
  expires, a phone is offline, anisette is unavailable, or another dependency
  fails.

Sideport must never call a deployment “complete” merely because a device was
enumerated, an app registration was saved, or a request was queued.

The live backend now implements bounded automatic enrollment, protected managed
first-credential entry, verified install/finalization, scheduler settings, and
verify-only reconciliation. The runtime UI remains capability-gated, and USB,
paired-Wi-Fi, and Apple `container` physical acceptance are not claimed until
their separate gates pass.

## Starting Point

### Repository and worktree

- One worktree exists: `/Users/dragoshont/Repo/sideport`.
- It is on `main` at `697646b`, equal to `origin/main`.
- Five tracked files have pre-existing unstaged changes:
  `deploy/k8s/README.md`, `deploy/k8s/deployment.yaml`,
  `src/Sideport.Api/Operations/OperationScheduler.cs`,
  `src/Sideport.Api/Program.cs`, and
  `tests/Sideport.Api.Tests/ApiSmokeTests.cs`.
- Seventeen unrelated untracked artifacts are also present.
- The tracked deployment diff removes durable Sideport and anisette storage;
  the scheduler dependency guard and `/metrics` are also removed. These changes
  conflict with onboarding safety and must be reconciled by the owner before an
  onboarding implementation edits the same files. They must not be discarded,
  reset, or overwritten automatically.

### What already works in current source

- Server-custodied Personal Apple ID password lookup.
- Apple sign-in, 2FA completion, and team listing.
- USB/Wi-Fi discovery, device diagnostics, installed-app reads, and known-device
  JSON storage.
- Browser IPA upload and server-path inspection.
- Durable registrations, queued operations, retry/rerun, and operation history.
- Signing, installation, renewal derivation, and scheduler enqueue through the
  operation service.
- Durable diagnostic issues.

### What prevents a complete first run

1. Kubernetes pins `ghcr.io/dragoshont/sideport:0.1.0`, which predates the
   current admin UI and onboarding APIs.
2. The current unstaged manifest removes `/var/lib/sideport` persistence while
   the container root filesystem stays read-only, and makes anisette ephemeral.
3. `firstRunComplete` omits Apple authentication/team selection, durable device
   acceptance, a successful verified install, and an enabled scheduler.
4. Enumeration is interpreted as trust: lockdown failures are returned as
   devices, then mapped to `trusted` and `healthy`.
5. The UI displays Apple teams but still asks the operator to type a Team ID.
6. When no usable local identity exists,
   `EnsureCertificateAsync` revokes all development certificates before minting
   one, without a server-enforced impact acknowledgement.
7. The first-app wizard stops after registration and does not execute
   preflight → operation → polling → verification → device detail.
8. Scheduler UI state is hard-coded as disabled/planned and no settings or
   run-now contract exists.
9. `/readyz` tests static anisette client metadata rather than provisioned ADI
   headers and does not test state/work-directory writes.
10. The manifest maps Apple ID to `Sideport__Apple__AppleId`, while current code
    reads `Sideport:Apple:PersonalAppleId`.
11. `SIDEPORT_DEVICE_ID` is documented as an iPhone UDID. It is actually the
    stable `X-Mme-Device-Id` UUID for the Sideport/anisette identity.
12. Compose omits required identity/state/device settings and masks the signer
    baked into the image with a nonexistent bind mount.
13. The runtime discovers, reads, installs, and refreshes over paired Wi-Fi when
    USB is absent, but issue #3 records real bulk-transfer stalls and ambiguous
    network verification. First pairing and the first install remain USB. Later
    refresh may use paired Wi-Fi with a bounded transfer and an explicit USB
    fallback; production reliability is not claimed before the physical gate.
14. Apple’s `container` runtime can run the current amd64 image under Rosetta on
    Apple silicon/macOS 26, but the repository has no launcher, the current
    Compose file is not translatable, and non-root usbmux socket forwarding has
    not yet passed a physical Sideport install.
15. A fresh deployment cannot establish its first Personal Apple ID from the
    UI: the live API accepts only an Apple ID, while environment and Keychain
    credential providers are read-only. The current Step 2 therefore sends the
    owner out of the UI without naming a workable in-product destination.

## Grounding and Decisions

This plan is subordinate to:

- `architrave.config.json`
- `docs/sideport-backend-contract.md`
- `docs/ui/sideport-ui-design-spec.md`
- `docs/ui/sideport-ui-data-contract.md`
- `docs/architecture/adr-0001-roadmap-foundations.md`
- the existing Storybook under `src/Sideport.Admin`
- the web, backend, operations-UX, YAGNI, and learning-loop knowledge packs

The implementation will use the existing React/.NET/JSON-store/single-replica
architecture. It will not add a database, broker, distributed worker, local
user system, or a new UI library.

| Decision | Selected behavior |
| --- | --- |
| Completion | Durable full-path evidence and a separate live `readyNow` value. |
| First install transport | USB required; Wi-Fi-only discovery gives a precise blocker. |
| Later refresh transport | Use an existing pairing over Wi-Fi or USB. Bound wireless transfers, verify device state, and present USB as fallback when wireless transfer/verification is uncertain. |
| Apple `container` | Support `container` 1.1+ on Apple silicon/macOS 26 with the current amd64 Sideport image under Rosetta, native anisette, persistent named volumes, and the macOS usbmuxd socket. Native arm64 and Compose emulation are not prerequisites. |
| Apple passwords | Enter once in Step 2 only when the server advertises protected managed entry. Submit over authenticated HTTPS or explicit loopback, clear immediately, never return/redisplay/log, and keep durable custody in the encrypted server store. Environment/SOPS and Keychain remain advanced preconfigured sources. |
| Team choice | Auto-select one Apple-returned Personal Team; show a chooser only when Apple returns multiple eligible teams. A free-only account has one Personal Team, while extra organization teams require membership/invitation. Manual Team ID remains a legacy/advanced API fallback and cannot complete onboarding. |
| Certificate cutover | Never revoke certificates implicitly. Inspect first, show exact impact, require a current server-enforced acknowledgement, then revoke only the acknowledged certificate IDs. |
| Install verification | Re-read installed apps/profiles and verify bundle ID plus signature expiry. Do not claim a launch check. |
| Scheduler V1 | Existing hourly, due-only evaluation with a two-day lead; UI can enable/disable it. Manual run-now and custom wall-clock windows are out of scope. |
| Registration lifecycle | A UI-created first-app registration is `pending-install`; the scheduler ignores it until a verified install activates it. Existing records migrate as active. |
| App library | Default to ready server-catalog artifacts with icon fallback, name, purpose, inspected version, and provenance. Configured GitHub release assets are selectable only after a planned safe download into durable storage and IPA inspection. Installed-iPhone metadata is never an IPA source. |
| Onboarding steps | Exactly six macro steps: Server, Apple signer, iPhone, App, Install, Ready. Preflight review, verification, post-install iPhone guidance, scheduler enablement, and receipt creation are inline states, not additional rail items. |
| Final action | **Install and finish** submits one durable intent with `finishOnboarding=true`; success means verify → activate → enable scheduler → compute next evaluation → write receipt last. |
| Readiness | Kubernetes service readiness stays shallow enough to serve the setup UI. Operational dependency checks live in the authenticated system/onboarding read model. |
| UI style | Reuse current `Panel`, status, source, empty-state, stepper, dialog, and pipeline patterns. Add no new visual values unless a token/source-of-truth entry is established first. |
| Infrastructure | Repository changes and render/policy checks only. A human performs any deployment reconciliation. |

The current Architrave config does not declare `designMap` or `tokens` paths.
Phase 0 must either identify those sources or record that the implementation
uses existing Storybook components and existing CSS values without adding new
ones. A global token retrofit is not part of onboarding.

## Product Contract

### Required completion evidence

`setupState` may become `complete` only when all durable evidence below exists:

1. Mutation access is protected by OIDC or the configured bearer-token path.
2. Sideport state and work directories are writable and their JSON stores load.
3. Provisioned anisette headers are available; static `client_info` alone is
   insufficient.
4. A server-custodied Personal Apple ID credential is configured and has
   authenticated successfully.
5. An Apple-returned team is selected and persisted with a validation timestamp.
6. A usable persisted signing identity is verified; when mint/replacement was
   needed, its signer-cutover operation is terminal succeeded.
7. A device is accepted into the durable inventory, is currently reachable over
   USB, and has a successful lockdown/trust handshake for the first install.
8. A ready catalog artifact and its registration retain durable lineage
   (`catalogAppId`, bundle ID, accepted device, account profile, and selected
   team); the registration may be pending during the flow and is active by
   completion.
9. A durable install operation has successful sign/install stages and
   device-side verification confirms the bundle ID and profile expiry; the
   enclosing onboarding operation remains nonterminal until finalization ends.
10. The registration is active and the scheduler is enabled with a computed
    next evaluation.

The final UI action is **Install and finish**. It submits an install preflight
and operation whose `planVersion` and durable intent both include
`finishOnboarding=true`. After signing/installing, the server performs one fixed
finalization sequence: record device verification → activate the registration
→ enable the scheduler → compute its next evaluation → write one immutable,
non-secret completion receipt under `Sideport:State:Directory` last. The receipt
contains schema version, completion time, actor, selected account-profile/team
IDs, accepted device UDID, registration key, verified operation ID,
scheduler-settings version, and the operational-check timestamp. `GET`
endpoints never create it, and **Ready** is unavailable until it exists.

If finalization fails after durable verification, the operation exposes **Retry
finishing setup**. The idempotent recovery path resumes at the first unfinished
finalization boundary and never authenticates, signs, uploads, installs, or
repeats device verification. `POST /api/onboarding/complete` remains a
compatibility/recovery entry point into that same finalizer, not a separate
happy-path UI step.

Once written, the receipt is not automatically deleted when a phone goes
offline, a registration is removed, scheduling is disabled, or the signer is
reconfigured. Those changes make `readyNow=false` and identify the affected
step; they do not rewrite history. There is no UI reset endpoint in this slice.
An intentional factory reset means backing up and clearing the state volume
through the separately approved deployment process.

Developer Mode, developer-profile trust, and opening the app are guided iPhone
tasks. The UI may record an operator acknowledgement, but it must label that
evidence origin `operator` while retaining the contract source `live`; it must
not present the acknowledgement as device-verified evidence. Launch success is
not part of V1 completion because Sideport has no truthful launch probe.

### Live readiness

`readyNow=true` requires:

- all server operational checks are passing;
- the selected Apple account has a recently validated cached session and no
  pending 2FA challenge; renewal is never predicted from a status GET;
- at least one accepted device is trusted and currently usable for a supported
  operation;
- no corrupt required store is present;
- no signer cutover is newly required because certificate inventory changed.

After setup completes, a phone going offline makes `readyNow=false` but does not
erase `setupState=complete`. The Overview and Diagnostics surfaces own recovery;
the app must not force the user back into first-run onboarding.

### Out of scope

- Persisting an Apple password in browser storage, exposing entry over an
  unprotected connection, or wiring the Storybook form to the current live API
  before the managed credential contract is implemented.
- Pairing from discovery/status reads, pairing over Wi-Fi, or enabling Developer
  Mode; USB pairing is available only through an explicit user-started operation.
- Claiming an app launched successfully.
- Wi-Fi pairing or Wi-Fi as the supported first-install transport.
- Native arm64 packaging, raw USB passthrough, or a third-party Compose layer
  for Apple `container`; the minimum path uses the official CLI and Rosetta.
- Multiple concurrent signers, multiple replicas, distributed locks, or a
  message broker.
- Custom daily schedule windows, notifications, user invitations, or RBAC
  administration.
- App version history, resumable upload, malware scanning, or uninstall.
- Live infrastructure apply/reconcile/restart or secret inspection.

## Target Experience

The existing left checklist/right detail pattern remains the shell. Its rail has
exactly six macro steps. A persistent
footer contains Back and at most one primary action. While an external event is
pending, the action area becomes a disabled status unless the operation exposes
a safe cancel capability. Advanced/manual paths stay collapsed.

1. **Server** — show mutation protection, writable durable state, provisioned
   anisette, executable signer, and supported device transport.
2. **Apple signer** — if no credential is configured, collect Apple Account
   email and password directly in the step, submit once to protected managed
   custody, clear the password field, then handle 2FA. Preconfigured custody
   skips entry. Show Sign in → Verify → Team → Certificate subprogress so the
   macro step never appears stuck. Auto-select one returned Personal Team and
   show a chooser only when Apple returns multiple eligible teams. Inspect the
   local/Apple certificate situation, create without revocation on a fresh
   account, and run the durable, exactly acknowledged cutover operation only
   when replacement is actually required.
3. **iPhone** — show the USB, unlock, and Trust/passcode guidance first, with
   Developer Mode/restart guidance on the same screen after acceptance. One **Connect iPhone**
   action starts a bounded enrollment operation. Sideport waits for one USB
   iPhone, requests pairing automatically, waits for Trust, verifies lockdown,
   and accepts the phone without more UI confirmations. Wi-Fi-only discovery
   remains a blocker for first pairing and install. Multiple candidates stop
   before pairing for a user choice; passive discovery never enrolls a phone.
4. **App** — default to the Sideport library: a compact selectable list with
   icon fallback, name, purpose, inspected version, and merged **On this
   server**/**GitHub release** provenance. The Storybook fixture shows the three
   apps in the reviewed deployment context; it is not a hard product limit.
   Keep browser IPA import and advanced server-path inspection secondary. Show
   installed-iPhone apps separately and read-only; only a matching ready catalog
   artifact can be selected, while unmatched apps say **IPA file needed**.
5. **Install** — keep the grouped Server/Apple/iPhone/IPA/Limits/automatic-
   refresh preflight inside this step. Show blockers, warnings, exact planned
   mutations, and semantic changes when a preflight goes stale. One **Install
   and finish** press submits `finishOnboarding=true`, then shows real durable
   sign/install/verify/finalization stages. Automatic refresh is the smart
   default. A post-verification failure offers finalization-only retry, never a
   second install.
6. **Ready** — render only after the completion receipt exists. Show the
   device-observed app/version/expiry, next automatic evaluation, paired-Wi-Fi
   option, and USB fallback. Keep conditional developer-profile trust and
   opening-the-app guidance as a small non-blocking note with no launch claim;
   then route to the accepted device detail. Setup remains accessible from
   Settings.

### External pattern evidence

Mobbin references inform interaction anatomy only; Sideport retains its own
Storybook components and visual language.

| Reference | Pattern adopted |
| --- | --- |
| [Sentry onboarding](https://mobbin.com/flows/4bc66677-8630-45cb-b389-0d34a48a0385) | Focused setup step, recommended/advanced paths, real verification, and a persistent footer leading to the created result. |
| [Tailscale adding a device](https://mobbin.com/flows/a20bdd03-774a-49df-b900-e85b003af39c) | Inventory-context `Add device`, device-type/setup guidance, and connected-state evidence. |
| [Apple Watch camera setup](https://mobbin.com/screens/da23966e-ae3c-490b-bf5b-1d576864d9c8) | One clear physical instruction while software owns the pairing transition. |
| [Tonal device search](https://mobbin.com/screens/e1c04d07-892d-4cca-94ea-cc80b8d5cbe8) | A calm waiting state with one live outcome instead of repeated confirmation buttons. |
| [Vercel deployment setup](https://mobbin.com/flows/edfece19-b9e5-482a-b867-ec0e47ff61ad) | One review surface, explicit team selection, collapsed advanced settings, and execution only after review. |
| [Uxcel destructive confirmation](https://mobbin.com/screens/9497f672-ac78-4378-b51f-8049e44b9943) | Explicit impact and a deliberate confirmation for destructive signer cutover. Sideport keeps one exact acknowledgement instead of copying the reference's typed phrase. |
| [Laravel Cloud deployment progress](https://mobbin.com/screens/f9a999ee-f498-4a02-b17f-be383d866950) | Grouped sequential stages, pending/running states, elapsed time, logs, and capability-driven cancel. |

## Contract Changes

The Service Architect updates `docs/sideport-backend-contract.md` before backend
or runtime UI code. All mutations remain under the existing `/api/*` auth gate.
Every response uses structured error codes and omits secrets.

### 1. Service and operational readiness

`GET /readyz` becomes shallow service readiness so a recoverable signer or
anisette/store problem cannot remove the only setup UI pod from the Service. It
checks only that ASP.NET routing and the packaged admin shell are available. It
does not open domain stores and its payload does not claim signing readiness.
All JSON stores must load lazily or capture their load error instead of throwing
from service constructors, so corrupt state can be shown and recovered through
the authenticated UI.

Operational checks move to authenticated `GET /api/system/status`:

```json
{
  "operational": false,
  "checkedAt": "2026-07-11T12:00:00Z",
  "checks": [
    {
      "id": "state-writable",
      "status": "pass",
      "source": "live",
      "checkedAt": "2026-07-11T12:00:00Z",
      "scope": "deployment",
      "affectedResources": ["sideport-state"],
      "reason": "Durable state is writable.",
      "nextAction": null
    },
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

Required checks are:

- `mutation-protection`
- `state-readable`
- `state-writable`
- `work-writable`
- `anisette-headers`
- `signer-executable`
- `device-transport`
- `operation-store`

Write checks create and delete a random zero-byte probe inside the target
directory. Signer executability uses a bounded non-mutating invocation, not
`File.Exists` alone. No check logs a header, token, credential, or private path
contents. Provisioned-header probes are coalesced and cached for 30 seconds so
normal UI polling does not churn one-time anisette material. Only the
pass/fail/timestamp is cached; header values are immediately discarded and a
one-time password is never reused for authentication.

### 2. Onboarding read model

`GET /api/onboarding/status` becomes an aggregate over existing stores and live
checks. For one compatibility release, the root retains the current
`firstRunComplete`, `schedulerEnabled`, and legacy `steps` objects with their
existing fields and `pending|warning|blocked|complete` vocabulary. New clients
read a nested V2 `workflow` object; older clients may ignore it.
`firstRunComplete` is true only when the immutable V2 receipt exists, and
`schedulerEnabled` comes from the live settings store.

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
        "evidence": []
      }
    ]
  }
}
```

Step state vocabulary is:

- `not-started`
- `action-required`
- `in-progress`
- `complete`
- `blocked`

Steps are `server`, `apple-signer`, `device`, `app`, `install`, and `ready`.
Scheduler enablement and completion are states inside `install`; `ready` becomes
complete only when the immutable receipt exists. Every incomplete step has a
reason and at most one next action;
an automatically in-progress step has none. A step also carries
`activeOperationId` when a durable operation owns it, so a browser reload can
resume a pre-UDID enrollment through `GET /api/operations/{id}`. The aggregator
derives progress from Apple configuration, known-device state, active
registration, verified operation result, and scheduler settings. After
the install finalizer or recovery `POST /api/onboarding/complete` writes the
narrow receipt,
`setupState=complete` is monotonic historical state and
`completedAt`/`verifiedOperationId` come from that receipt. This is not a
general-purpose workflow database and stores no mutable per-step state.

`POST /api/onboarding/complete` accepts the verified install operation ID plus
an `idempotencyKey`, requires that operation's durable
`finishOnboarding=true` intent, and invokes the same idempotent finalizer used
by **Install and finish**. It returns:

- `201 Created` with the immutable receipt when it writes
  `onboarding-completion.json`;
- `200 OK` with the existing receipt on replay or after setup was already
  completed;
- `409 onboarding-incomplete` with the current V2 workflow and blockers when any
  condition is absent;
- `503 onboarding-store-unavailable` when the receipt cannot be read or written.

It may activate an already verified pending registration, enable the scheduler,
and compute the next evaluation, in that order, but it can never repeat Apple
authentication, signing, upload, installation, or device verification. The
receipt is written last and is keyed by one workspace. Its evidence snapshot
contains only stable IDs/timestamps/versions, never Apple credentials, session
tokens, anisette headers, certificate bytes, or IPA contents.

### 3. Apple signer configuration and cutover

Add one managed credential path for official Docker and Apple `container`
installs: `POST /api/apple-access/personal/connect` with write-only `appleId`
and `password`. It requires a configured bearer/OIDC owner with
`apple.signer.manage`, effective HTTPS or explicitly enabled loopback-only
binding, trusted-proxy/same-origin/antiforgery checks, safe Apple TLS policy,
and client/IP rate limiting before the body is read. After the validated Apple
ID is parsed, apply the per-account limit. The production credential surface
also uses self-only script/connect/form CSP, denies framing, and loads no
third-party script. Open mode and remote HTTP reject the request. The response
is no-store, redacted status only: `201` after first
authentication and durable storage, `200` after validated same-account atomic
rotation, or `202` with an opaque five-minute one-use 2FA challenge. A password
waiting on 2FA exists only in process memory and is discarded on any terminal
outcome, expiry, or restart.

Extend `POST /api/apple-access/personal/2fa` for connect-originated challenges.
Bind each challenge to its initiating actor/account and apply the same
capability, secure-transport, trusted-proxy, origin/antiforgery, TLS, and
two-stage rate-limit boundary before reading the code. Only successful 2FA
persists the candidate; invalid, expired, restarted, or replayed challenges
discard it and return Step 2 to credential entry.

Implement one writable `ManagedAppleCredentialProvider` under the durable
Sideport state root. Store a versioned credential envelope with authenticated
encryption using a persisted purpose-scoped Data Protection key ring, atomic
replacement, and `0700` directory/`0600` file permissions for the non-root
Sideport user. Environment/SOPS and macOS Keychain providers remain read-only
compatibility paths and skip the form. A failed authentication never replaces
a working credential; a different Apple ID returns
`apple-account-replacement-requires-cutover`. Credential removal and a
multi-account switch remain separate offboarding/cutover work.

Provider choice is explicit and has no secret fallback chain.
`Sideport:Apple:CredentialSource=managed|environment|keychain` selects exactly
one provider. Official fresh Docker and Apple `container` examples set
`managed`; existing deployments with the setting absent retain the current
one-release `environment` default and receive a migration blocker/remedy rather
than a silent behavior change.

Extend `GET /api/apple-access/personal/status` with:

- stable non-secret account profile ID;
- credential source and `credentialEntry` capability (`supported`,
  `allowedNow`, structured `blockedReason`);
- current cached auth state, `lastAuthenticatedAt`, `authValidatedAt`, and an
  explicit freshness reason;
- returned teams, `selectedTeamId`, and `teamValidatedAt`;
- local signing identity state, serial/fingerprint suffix, and expiry;
- last explicitly fetched Apple certificate inventory summary;
- `impact` (`reuse`, `mint`, `replace-existing`, `unknown`);
- latest cutover operation/state and actor/timestamp;
- precise next action.

`GET` is side-effect-free: it reads cached/local state and never signs in,
fetches Apple teams/certificates, or asserts that an in-memory Apple session can
renew. Explicit sign-in, 2FA completion, signing-preflight, and successful
authenticated operation calls refresh the cache. Auth states are
`credential-configured`, `two-factor-required`,
`validated-recently`, `validation-stale`, `failed`, and `unknown`; each includes
the reason and timestamp. `readyNow` requires `validated-recently` plus the
cached session used for that validation. `validated-recently` means an explicit
sign-in, 2FA completion, or Apple preflight succeeded within 15 minutes. If
Apple session expiry cannot be proven, the contract says so instead of
predicting renewal.

Add:

- `POST /api/apple-access/personal/connect`
- `PUT /api/apple-access/personal/team`
- `POST /api/apple-access/personal/signing-preflight`
- `POST /api/apple-access/personal/cutover`

Team selection request:

```json
{ "accountProfileId": "acct_...", "teamId": "TEAMID1234" }
```

The team must be present in the most recent successful Apple response. A manual
team ID cannot satisfy onboarding.

Signing preflight is read-only and returns:

```json
{
  "preflightId": "signing_preflight_...",
  "expiresAt": "2026-07-11T12:10:00Z",
  "accountProfileId": "acct_...",
  "teamId": "TEAMID1234",
  "localIdentity": { "state": "missing", "expiresAt": null },
  "appleCertificates": [
    { "id": "cert_...", "serialSuffix": "A1B2", "expiresAt": "2026-09-01T00:00:00Z" }
  ],
  "impact": "replace-existing",
  "requiresAcknowledgement": true,
  "inventoryVersion": "sha256:..."
}
```

Cutover request includes `preflightId`, `inventoryVersion`, the exact
acknowledged certificate IDs and impact codes, and an idempotency key. It
creates a durable `type=signer-cutover` operation in the existing operation
store/queue; it is not a loose acknowledgement flag. The server rejects any
set that differs from the current preflight. A valid local Sideport identity
is reused and requires no cutover operation.

The cutover stages are:

1. `preflight`
2. `revalidate-certificate-inventory`
3. `revoke-acknowledged-certificates` (skipped when the inventory is empty)
4. `mint-certificate`
5. `persist-identity`
6. `verify-identity`

Cutover and install/refresh acquire the same process-wide signing gate. The
operation re-fetches inventory under that gate before any mutation. If the
inventory/version differs, it records a blocked operation with
`signing-preflight-stale` and revokes nothing. It may revoke only the exact
certificate IDs captured in the confirmed preflight; 404 for one of those IDs
is treated as already absent, never as permission to revoke a replacement.

Before the first revoke, the durable operation records the confirmed account,
team, inventory version, exact certificate IDs, impact codes, actor,
risk-contract version, and stage transition to running. It never stores secret
material and rejects a certificate/impact set that differs from the current
preflight. After minting, the PKCS#12 is atomically persisted before the
operation records the new serial/fingerprint suffix. That authorization is
single-use: normal replay returns the same operation, and a linked recovery
retry may only finish the already-authorized mint/persist steps. It can never
authorize another revoke set.

If the process fails after an irreversible revoke, startup does not
automatically mint or repeat revocation. The operation reconciles to `unknown`
or `recovery-required` and blocks all other signer work for that account/team.
The generic verify-only operation reconciliation endpoint inspects local
identity plus Apple inventory:

- a matching persisted identity completes the cutover;
- acknowledged certificates absent and no unexpected certificate makes a
  linked retry eligible to perform only mint/persist/verify;
- any unexpected certificate requires a fresh signing preflight and
  confirmation.

The developer-portal implementation therefore splits certificate listing,
exact-ID revocation, and creation. Certificate creation never calls
`RevokeAllDevelopmentCertificatesAsync`. If Sideport's own certificate later
needs replacement, the same cutover operation is used; unknown/new
certificates always force a new review.

Required structured errors include:

- `apple-credential-missing`
- `credential-entry-transport-required`
- `credential-source-read-only`
- `apple-credential-store-unavailable`
- `apple-account-replacement-requires-cutover`
- `apple-auth-rate-limited`
- `apple-authentication-failed`
- `apple-two-factor-required`
- `apple-challenge-expired`
- `apple-team-not-returned`
- `apple-team-selection-stale`
- `signing-preflight-stale`
- `signing-cutover-required`
- `signing-identity-corrupt`
- `apple-certificate-inventory-unavailable`

Apple protocol failures must not escape as generic 500 responses.

### 4. Device discovery, trust, and acceptance

Extend the device transport model so a discovered device carries:

- `trustState`: `trusted`, `untrusted`, `locked`, `error`, or `unknown`;
- `trustReason`;
- `lockdownCheckedAt`;
- `usableForInstall`;
- current connection.

Lockdown failure must never be converted to `trusted` or `healthy`.

Extend known-device responses with:

- `inventoryState`: `discovered`, `legacy-unverified`, or `accepted`;
- `acceptedAt` and `acceptedBy`;
- the live trust fields above;
- `supportedForFirstInstall`, which is true only for a trusted USB connection in
  this release.

All read-only discovery, diagnostics, and status paths open lockdown with
`autopair=false`; a GET must never trigger a Trust prompt, write a pairing
record, or accept a device.

`POST /api/devices/enrollments` is the one authenticated, user-started Add
iPhone mutation. It accepts an idempotency key and an optional `deviceUdid`,
returns a durable `enroll-device` operation, and is bounded to five minutes.
The operation advances through `wait-for-usb`, `request-pairing`,
`await-user-trust`, `verify-lockdown`, and `accept-device`. The UI starts it once
from **Connect iPhone**, then observes progress; it does not expose Pair, “I
tapped Trust,” or Add buttons. Waiting runs outside the install/refresh worker.

Zero USB candidates keeps the operation waiting. More than one ends it blocked
with `device-selection-required` before pairing and returns safe candidate
summaries. Choosing a phone starts a new enrollment request with that
`deviceUdid` and a new idempotency key; the server revalidates that it is still
an unaccepted USB candidate. An already-paired phone skips the Trust prompt but
still receives a fresh lockdown check. Wi-Fi never initiates pairing and
continues to consume only the host's read-only pairing record.

After lockdown succeeds, the same operation rechecks inventory writability and
persists acceptance. Trust denial, a locked phone, disconnect, timeout,
Wi-Fi-only discovery, or ambiguous recovery adds nothing. If recovery begins
after a pairing request, Sideport first performs a non-pairing trust check and
never blindly repeats pairing. `POST /api/devices/known` remains a compatibility
or manual-inventory path outside onboarding; it is not the first-run UI path and
cannot independently satisfy completion.

Existing known-device JSON has no trustworthy acceptance or lockdown evidence.
It migrates as `legacy-unverified`, never as trusted. Only a successful live USB
trust check within an explicit enrollment session upgrades it to `accepted`;
migration or passive discovery alone cannot complete onboarding.

After acceptance, Developer Mode is visible guidance on the same screen:
Settings → Privacy & Security → Developer Mode, turn it on, restart, then
unlock, tap Enable, enter the passcode, and reconnect USB if needed. Sideport
does not claim to enable or verify it. A future nullable read-only probe may report
`developerModeState=enabled|disabled|unknown` only after physical validation.

Required errors include:

- `device-not-discovered`
- `device-selection-required`
- `device-lockdown-untrusted`
- `device-locked`
- `device-usb-required`
- `device-trust-check-unavailable`
- `device-enrollment-timeout`
- `device-enrollment-recovery-required`

### 5. App library sources, pending registration, and full preflight

`GET /api/catalog/apps`, server-path inspection, and browser upload are live in
the current tree. Preserve artifact provenance separately from the UI's
`source=live|demo|planned` truth label. The picker merges duplicate source
badges but selects only a server-held, inspected `status=ready` IPA.

GitHub release import is a later runtime slice. It accepts only configured
public or private source IDs, lists `.ipa` release assets without mutation, and
imports one selected numeric release/asset identity through the existing size,
durable-storage, checksum, conflict, and IPA-inspection path. The browser never
submits a download URL. Private access prefers a GitHub App installation limited
to selected repositories with Metadata read and Contents read only; the bounded
interim is a deployment-secret reference to a fine-grained, expiring,
repository-scoped read token. No token is accepted from or returned to the
browser. It never signs or installs from a remote URL. The inspected IPA owns
bundle/version truth; the GitHub tag is provenance only.

Server-path import accepts a configured `rootId` plus relative path, rejects
traversal and symlink escape, applies the upload/ZIP limits, and copies the IPA
into managed storage before returning `ready`. New DTOs never expose the host
path.

The connected iPhone supplies installed-app metadata, not IPA bytes. A matching
bundle ID can point back to a ready library artifact. An unmatched installed app
is read-only with **IPA file needed**; Sideport never claims it can extract or
re-sign the app from the phone. Until trusted icon extraction lands, the UI uses
a generated initial/tone rather than a remote image.

Add backward-compatible fields to `AppRegistration`:

- `lifecycle`: `pending-install` or `active`;
- `catalogAppId`;
- `createdAt`;
- `activatedAt`;
- `lastVerifiedOperationId`.

Existing JSON records default to `active`. The new UI creates
`pending-install`. Existing records have no `lastVerifiedOperationId` and are
therefore `verification-required`; the scheduler filters to registrations that
are both `active` and backed by a durable successful device verification.

Extend `POST /api/apps` with a request DTO that can resolve the configured Apple
account/team by `accountProfileId` and the durable IPA by `catalogAppId`.
Legacy `appleId`, `teamId`, and `inputIpaPath` remain accepted for API
compatibility, but the onboarding UI does not ask the operator to retype values
already known by the server.

Extend `POST /api/operations/preflight` to accept `type=install` and perform
read-only checks for:

- operational server status;
- accepted/trusted/current USB device;
- catalog/IPA integrity and bundle ID;
- pending registration and three-registration limit;
- authenticated Apple account and selected returned team;
- Apple device/App ID/profile/certificate capability;
- a usable persisted Sideport signing identity; missing/expired identity routes
  back to the signer-cutover operation instead of revoking inside install;
- single-flight availability;
- scheduler eligibility after activation.

The response continues to expose blockers, warnings, planned mutations, scarce
limits, source, and `requiresConfirmation`. It also returns `preflightId`,
`expiresAt`, grouped checks, signing `inventoryVersion`, and a server-generated
`planVersion` over the selected account/team/device/artifact, scarce limits,
planned external mutations, and `finishOnboarding`. The short-lived preflight
record is process-local; a restart or expiry requires a fresh inline review in
Install, not a separate workflow step.

Install submission always reruns preflight under the submission lock. It queues
only when the new `planVersion` exactly matches the confirmed version. If the
target, limits, certificate impact, blockers/warnings, or planned mutations
changed, the server returns `409 install-preflight-stale` with the replacement
preflight and enqueues nothing. The UI stays in Install, highlights the semantic
change, and requires a new confirmation. Timestamp-only changes do not alter
`planVersion`.

### 6. Install operation and verification

Add `POST /api/operations/install`. It uses the existing operation queue,
idempotency, store, actor model, and single-flight execution.

Request:

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
and `Location: /api/operations/{operationId}`. The UI polls that existing GET
endpoint immediately, then with bounded backoff up to five seconds; visibility
pause or route change does not cancel the operation. The same idempotency tuple
returns `200` with the existing record and never enqueues twice.

Stages are:

1. `preflight`
2. `authenticate`
3. `prepare-signing`
4. `sign`
5. `install`
6. `verify`
7. `activate-registration`
8. `enable-scheduler`
9. `compute-next-evaluation`
10. `write-completion-receipt`

Each stage has status, timestamps, duration, redacted message, structured error,
and recovery action. Backend callbacks update the durable operation record at
real stage boundaries; the UI does not simulate progress.

The preflight request also carries `finishOnboarding=true`; the server includes
it in `planVersion`, then persists the matching flag in the operation intent
before any external effect. Installs outside onboarding omit/disable it and skip
stages 8–10. For onboarding, the fixed durable order is verification →
registration activation → scheduler enablement → next-evaluation computation →
completion receipt. The operation is not terminal `succeeded`, and the UI does
not advance to Ready, until the receipt is durable.

If any finalization write fails after durable verification, record the exact
unfinished stage and expose a finalization-only recovery action. Retrying calls
the shared idempotent finalizer, resumes from the first unfinished boundary, and
must not authenticate, sign, upload, install, or repeat device verification.

The 180-second watchdog applies only to the device-upload/install stage, not to
queue wait or the entire operation. Pairing, acceptance, and first-install
preflight block Wi-Fi; later scheduled/manual refresh may use a usable saved
pairing over Wi-Fi under the same watchdog, verification, quarantine, and USB-
fallback rules. Cancellation must be proven cooperative in the managed
Netimobiledevice path before the code may release its lock on timeout. If the
underlying task does not end, Sideport marks the durable operation `unknown`,
keeps the signer/device lease held, quarantines that device from scheduler and
manual work, and offers no retry. It must not claim the external mutation was
bounded merely because a cancellation token fired.

Phase 5 has a hard implementation fork: either the host integration gate proves
the transfer terminates cooperatively, or the install stage moves behind the
smallest killable helper-process boundary before physical acceptance. Until
one is true, an unknown operation requires the existing approved process-restart
runbook to end the in-process task, followed by reconciliation; Sideport never
releases the lock while an abandoned transfer may still mutate the phone.

Verification re-reads the device's installed apps and provisioning profiles,
matches the requested bundle ID, and records:

```json
{
  "installed": true,
  "deviceUdid": "000081...",
  "bundleId": "com.example.app",
  "version": "1.2.3",
  "signatureExpiresAt": "2026-07-18T12:00:00Z",
  "verifiedAt": "2026-07-11T12:00:30Z",
  "source": "live",
  "evidenceOrigin": "device"
}
```

Only this verification activates the registration. Failure leaves it
`pending-install`, keeps the operation evidence, and supplies a recovery action.
The result must not contain `launchVerified` in V1.

Add `POST /api/operations/{operationId}/reconcile`. It never repeats pairing,
certificate revocation/minting, signing, or installation. It creates a linked
durable reconciliation operation and is allowed only for `unknown`,
`recovery-required`, or verification-failed install/cutover operations. For an
install it rechecks USB/trust, installed bundle, version, and profile expiry:

- matching device evidence succeeds and idempotently activates the pending
  registration;
- app absent plus proof that no install task/lease is active records
  `safeToRerun=true`;
- an active task/lease returns `409 device-operation-still-active`;
- unreachable/ambiguous state remains blocked and non-retryable.

The original operation stays immutable; the child record carries actor,
parent-operation ID, evidence, and outcome. Rerun becomes available only when a
reconciliation record proves `safeToRerun=true`.

No refresh path bypasses these guards. `POST /api/operations/refresh`, scheduler
submissions, retry/rerun, and the legacy
`POST /api/apps/{udid}/{bundleId}/refresh` all call the same full refresh
preflight and queued operation service. Refresh requires an active,
device-verified registration, current usable Sideport identity, valid cutover
state, operational dependencies, a currently trusted USB connection or usable
saved pairing for the current Wi-Fi device, and no unknown operation or held
device lease. Wi-Fi bulk transfer is bounded; a timeout or ambiguous completion
becomes `unknown`, holds or quarantines the device lease, and requires
reconciliation instead of an automatic retry. It never mints or revokes a
certificate implicitly.

For one compatibility release the legacy endpoint becomes a deprecated wrapper
that returns the same `202` operation record/Location (or existing idempotent
record) rather than calling `IRefreshOrchestrator` synchronously. A later
contract revision may remove it; it cannot retain weaker safety semantics.

Existing registrations need a non-destructive migration path. Add
`POST /api/apps/{udid}/{bundleId}/verify`, which queues a
`verify-existing-registration` operation. It requires current trusted USB,
reads installed bundle/version/profile only, and never signs or installs. A
match writes `lastVerifiedOperationId` and makes the active registration
scheduler-eligible; absence/mismatch leaves it verification-required and offers
the normal install flow. This operation cannot satisfy first-run completion
unless the rest of the current signer/device/artifact lineage also matches.

### 7. Scheduler status and controls

Always register the operation scheduler. It reads a durable settings record
before each evaluation; the bootstrap config seeds that record only when it does
not exist. On the first V2 migration it seeds `enabled=false` unless all V2
prerequisites already exist; a legacy `Enabled=true` config is reported as a
requested value, not silently activated.

Add:

- `GET /api/scheduler/status`
- `PUT /api/scheduler/settings`

V1 settings allow only `enabled`. Manual run-now and custom windows are deferred:
neither is required to prove automatic renewal, and the existing operation
controls already cover deliberate refresh. Status truthfully exposes the
existing policy and operational limits:

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

The scheduler writes a bounded evaluation receipt even when no app is due.
After restart, due-state comes from the latest durable successful operation
result per active registration, not `RefreshOrchestrator` memory. A newer failed
operation does not erase the last verified expiry.

Every startup/hourly evaluation rechecks operational status, a usable persisted
signing identity/cutover state, active registration state, and whether each due
device has a current trusted USB connection or a usable saved pairing over
Wi-Fi. Offline, unpaired, unknown-operation, or dependency-failed targets are
recorded as blocked/skipped with a reason and are not enqueued. A paired Wi-Fi
target may be enqueued with the bounded transfer/verification rules above. The
enable surface says the one-time USB pairing enables later refresh over the
same Wi-Fi network, and that USB is the recovery path if a wireless refresh
cannot finish. Enabling requires at least one active verified registration, a
valid signer identity, and a usable paired device; it does not require the
iPhone to be on USB at that moment.

Required errors include:

- `scheduler-prerequisites-not-met`
- `scheduler-store-unavailable`

### 8. Mutation and status-code summary

The contract update must freeze these request/response and idempotency rules
before implementation:

The existing operation record stays the single job model. Its target expands
additively with `kind`:

- `app`: `deviceUdid`, `bundleId`;
- `device`: `deviceUdid`;
- `device-enrollment`: optional `deviceUdid` until a single USB candidate is
  selected, plus the server-generated enrollment operation ID;
- `signer`: `accountProfileId`, `teamId`;
- `reconciliation`: `parentOperationId` plus the original target.

Fields irrelevant to a target kind are absent, not empty placeholders. Existing
refresh records remain valid and infer `kind=app` during the compatibility
window. Operation status adds `recovery-required`; result adds typed
`verification`, `safeToRerun`, and `reconciledOperationId` fields. Existing
cancel/retry/rerun capability flags remain authoritative.

| Endpoint | Request | Success | Failure rules |
| --- | --- | --- | --- |
| `POST /api/apple-access/personal/connect` | write-only `appleId`, `password` | `201` first account; `200` same-account rotation; `202` expiring 2FA challenge | `403` auth/transport/origin/TLS policy; `409` read-only source/account replacement/challenge expiry; `422` Apple auth/2FA; `429` rate limit; `502/503` upstream/store unavailable |
| `POST /api/apple-access/personal/2fa` for connect challenge | `pendingChallengeId`, six-digit `code` | `201` first account or `200` same-account rotation after commit | Same boundary as connect; `409` expired/consumed; `422` invalid; `429` rate limit; `503` store unavailable |
| `PUT /api/apple-access/personal/team` | `accountProfileId`, `teamId` | `200` current Apple status with persisted selection | `404` profile; `409` stale auth/team list; `422 apple-team-not-returned` |
| `POST /api/apple-access/personal/signing-preflight` | `accountProfileId`, `teamId` | `200` expiring read-only preflight | `409` 2FA/auth required; `503` inventory unavailable |
| `POST /api/apple-access/personal/cutover` | `preflightId`, `inventoryVersion`, exact certificate IDs, impact codes, `idempotencyKey` | `202` queued cutover operation; replay returns `200` existing operation | `409` expired/unknown preflight; `422` acknowledgement mismatch; revalidation drift becomes a durable blocked operation |
| `POST /api/devices/enrollments` | `idempotencyKey`, optional `deviceUdid` | `202` bounded enroll-device operation; replay returns `200` | Submission: `409` conflicting active enrollment; `422` specified device ineligible. Selection, Trust denial, lock, disconnect, timeout, and recovery are durable terminal operation results after `202` and add nothing. |
| `POST /api/devices/known` | compatibility/manual inventory metadata | `201` created; `200` upserted | Never supplies onboarding enrollment evidence by itself |
| `POST /api/apps` | `catalogAppId`, `deviceUdid`, `accountProfileId`, `lifecycle=pending-install` | `201` pending registration; natural-key replay returns `200` | `409` slot/registration conflict; `422` catalog/account/device validation |
| `POST /api/operations/preflight` | `type=install`, `deviceUdid`, `bundleId`, `finishOnboarding=true` for first run | `200` preflight whose `planVersion` covers the finish intent, including when `ready=false` | `400` malformed target; `503` required store/probe unavailable |
| `POST /api/operations/install` | `deviceUdid`, `bundleId`, `preflightId`, confirmed `planVersion`, matching `finishOnboarding=true`, `idempotencyKey`, planned-mutation confirmation | `202` queued; duplicate key returns `200` existing operation; blocked preflight returns durable `201` blocked operation; onboarding success includes the receipt | `409 install-preflight-stale` with replacement preflight or stale cutover; `422` confirmation/finish-intent mismatch; `503` store unavailable |
| `POST /api/operations/{id}/reconcile` | `idempotencyKey`, optional operator note | `202` queued verify-only child; duplicate returns `200` | `409` unsupported state or active external task; `404` operation; `503` store/probe unavailable |
| `POST /api/apps/{udid}/{bundleId}/verify` | `idempotencyKey` | `202` queued verify-existing-registration operation; duplicate returns `200` | `409` device/lease state; `404` registration; `422` USB/trust blocker |
| `POST /api/operations/refresh` and legacy refresh wrapper | target plus idempotency key where supported | `202` queued fully preflighted refresh; duplicate returns `200` | `409` unverified registration/cutover/quarantine; `422` pairing, transport, or confirmation blocker |
| `PUT /api/scheduler/settings` | `{ "enabled": true }` | `200` current status; identical request is a no-op | `409 scheduler-prerequisites-not-met`; `503` store unavailable |
| `POST /api/onboarding/complete` | `verifiedOperationId`, `idempotencyKey` | Compatibility/recovery finalization returns `201` immutable receipt; replay returns `200`; never reinstalls | `409 onboarding-incomplete` or mismatched finish intent; `503` receipt store unavailable |

`POST /api/apps` resolves Apple ID, selected team, and durable IPA path on the
server. The UI never submits a redacted hint as an identifier. Its response is
an app-registration DTO, not the storage record, and returns `appleIdHint`
rather than a credential or password.

All listed mutations require the existing authenticated `/api` boundary.
Bearer-token access remains owner-equivalent. In this single-owner release every
authenticated OIDC principal is explicitly owner-equivalent; the UI must not
present the currently fictional operator/viewer roles as enforced.

The API nevertheless server-enforces endpoint capabilities: credential
connect/rotation and team/cutover use owner-only `apple.signer.manage`;
scheduler enable uses owner-only
`scheduler.manage`; enrollment uses `devices.manage`; registration uses
`catalog.import`; install/reconciliation use `operations.run`; onboarding
completion requires all preceding capabilities. Because Sideport cannot verify
an identity in current open-behind-proxy mode, that mode is read-only:
**every** `/api` mutation—including credential connect, upload, team selection,
cutover, enrollment, registration, install, refresh, reconciliation,
scheduler settings, and completion—returns
`403 mutation-protection-required`. Supporting trusted
proxy identity headers is a separate auth contract, not an onboarding
assumption. Every accepted mutation records the actor produced by the existing
bearer/OIDC actor model.

Idempotency keys are scoped to actor plus operation type and target. Cutover,
enrollment, install, and reconciliation return the existing operation for the same
tuple; device/app upserts are idempotent by stable key. A stale browser
preflight never authorizes an external effect: install and cutover rerun safety
reads under the server lock and require semantic plan equality before the first
Apple/device mutation.

## UI State Matrix

The authoritative provenance vocabulary remains
`live|derived|demo|planned`, matching `SourceKind` and the current source pill.
`operator`, `device`, `apple`, `artifact`, `system`, and `operation` are values
of a separate `evidenceOrigin` field, not new sources. Phase 0 adds this explicit
DTO/type/design delta before Storybook.

The V2 workflow states map onto existing status components without inventing a
parallel visual primitive: `not-started` uses neutral, `action-required` uses
warning, `in-progress` uses the existing running treatment, `complete` uses
healthy, and `blocked` uses blocked. Text and icons carry meaning. Legacy V1
step states remain unchanged during the compatibility window.

| Step/state | Evidence shown | Primary action | Continue when |
| --- | --- | --- | --- |
| Server checking | Per-check live status and timestamp | Retry checks | Required checks pass |
| Server configuration blocked | Exact failed check and deployment-owned remedy | Open deployment guide | Never from UI |
| Permission/mutation protection missing | Current auth mode and required owner capability | Configure auth or sign in | Protected owner-equivalent session exists |
| Partial/system request failure | Successful sources plus failed source/timestamp | Retry failed source | Required data is current |
| Apple credential missing, managed entry allowed | Apple Account email and password fields plus one short custody reassurance; no infrastructure jargon | Sign in | Both fields are present and protected connect accepts the request |
| Apple credential missing, entry blocked | Plain secure-connection or owner-access remedy; technical reason on disclosure | Fix connection/access | Status reports `credentialEntry.allowedNow=true` |
| Apple credential configured | Redacted account hint; no password value | Sign in | Auth starts |
| 2FA required | Challenge kind and expiry | Submit code | Auth succeeds |
| 2FA expired | Expiry and discarded challenge | Start sign-in again | New challenge/auth succeeds |
| Apple auth failed/throttled | Structured Apple code, retry guidance, last attempt | Retry when allowed | Explicit validation succeeds |
| Apple returned no teams | Auth evidence and empty live team result | Retry Apple check | A returned team exists |
| Apple authenticated, one team | One returned team card, selected automatically | Continue | Selection persists |
| Apple authenticated, multiple teams | Returned team cards with Personal/Organization type | Select team | Selection persists |
| Team selection stale | Previously selected team and latest returned teams | Select again | Current returned team persists |
| Identity reusable | Serial suffix and expiry | Continue | Identity is validated |
| Mint, no existing cert | Planned mutation | Confirm | Standard confirmation accepted |
| Existing cert replacement | Exact impact and affected certificate summaries | Acknowledge the exact certificate once | Server accepts the exact current preflight set |
| Cutover queued/running | Real cutover stages and current non-cancelable boundary | Cancel only while queued | Verified identity persists |
| Cutover unknown/recovery-required | Last durable stage, exact acknowledged IDs, reconciliation reason | Reconcile | Matching identity or safe linked retry is proven |
| Ready to connect | USB/unlock and Trust/passcode guidance; Developer Mode is clearly labeled as the after-acceptance section | Connect iPhone | Enrollment operation starts |
| Waiting for USB | Live bounded-session status | No second decision; disabled status label | One eligible USB iPhone appears |
| Awaiting Trust | Detected phone plus “Trust This Computer” and passcode guidance | Respond on the iPhone | Lockdown reports the user response |
| Device untrusted/locked | Lockdown evidence and one remediation | Unlock/respond on iPhone; retry starts a new session only after terminal failure | Lockdown succeeds |
| Device Wi-Fi-only | Current connection, one-time USB pairing/first-install requirement, and later Wi-Fi capability | Connect USB during the active session | Eligible USB snapshot appears |
| Multiple USB devices | Safe candidate names/models and no pairing side effect | Choose one iPhone | New selected-device enrollment starts |
| Verifying/adding | Friendly automatic progress; raw operation stages only in technical details | No second decision; disabled status label | Lockdown and inventory acceptance both succeed |
| Legacy known device | Stored metadata plus missing validated enrollment evidence | Start Add iPhone over USB | Enrollment succeeds |
| Device accepted | Accepted device summary; Developer Mode → Restart → post-restart Enable/passcode/reconnect guide; future Add iPhone explanation | Choose app | Durable enrollment evidence exists |
| No app | Upload and server-path choices | Import IPA | Catalog record is ready |
| Upload too large/unsupported | Actual limit/media error | Choose another IPA | Inspection succeeds |
| Upload inspection failed | Structured inspection evidence | Replace file | Catalog record is ready |
| Catalog ID conflict | Existing artifact summary and replace impact | Choose ID or explicitly replace | Conflict resolves |
| Device slots full | Current `3/3` registrations and affected apps | Remove a registration | Slot is available |
| Legacy registration verification required | Existing registration plus missing device-verification lineage | Verify over USB | Verify-existing operation matches bundle/profile |
| Install preflight blocked | Inline grouped blockers/warnings/planned mutations | Fix first blocker | Preflight is ready |
| Install ready | App/device/team/slot/impact summary plus automatic-refresh default | Install and finish | Confirmed operation starts with `finishOnboarding=true` |
| Install preflight stale | Replacement plan with highlighted semantic changes in the same step | Confirm updated plan | New plan is confirmed |
| Queued/running | Real stages, elapsed time, redacted detail | Cancel only if record allows | Terminal operation |
| Operation canceled | Canceled-before-side-effect receipt | Return to Install | New preflight is ready |
| Install failed | Failed stage, code, evidence, recovery | Retry/rerun only if capability allows | New operation succeeds |
| Install timeout/unknown | Quarantined device, active-task state, non-retryable reason | Reconcile after task ends/restart | Device state is proven |
| Verification failed | Install result versus missing/ambiguous device evidence | Reconcile only | Verification or safe rerun is proven |
| Reconnect/resume | Existing operation ID and latest durable stage after reload | Resume viewing | Poll reaches terminal state |
| Verified/finalizing | Device-observed version and expiry plus current activate/scheduler/next-evaluation/receipt stage | No second decision; disabled status label | Completion receipt is durable |
| Scheduler enable blocked during finalization | Missing identity/active registration or unusable saved pairing reason | Fix prerequisite | Finalization can resume |
| Scheduler or receipt write failed after verification | Exact unchanged durable evidence and unfinished boundary | Retry finishing setup | Finalizer resumes without signing or reinstalling |
| Ready | Immutable receipt, installed app, next evaluation, paired-Wi-Fi option and USB fallback; conditional profile-trust/open-app guidance is a non-blocking note | View device | Receipt already exists; route to device detail |

Loading, partial, permission, no-data, and system-error states remain distinct.
No `0`, empty table, or green status is shown until its source request finishes.
Status is never color-only. Dialog focus is trapped and restored; progress uses
an `aria-live=polite` text summary and respects reduced motion. Step navigation
moves focus to the new step heading; failed submissions move focus to a linked
error summary and associate field errors with inputs. The credential form uses
`autocomplete=username` and `current-password`, submits on Enter or its
persistent-footer **Sign in** action, clears the password immediately, and
focuses the 2FA field when challenged. 2FA expiry is announced.
Live regions announce stage transitions only, not every polling/elapsed-time
update. The approved stories verify reflow at 320 CSS pixels and 200% zoom.

## Phase Ledger

Exactly one phase is active. A phase transition is recorded in
`.architrave/runs/sideport-onboarding-plan-20260711/phase-ledger.md` only after
its gate is satisfied.

### Phase 0 — Contract and worktree decision (`completed`)

Scope:

- Obtain human agreement on this plan's completion, USB, scheduler, and cutover
  semantics.
- Reconcile the five pre-existing tracked changes without destroying them.
- Update the backend contract and relevant ADR/spec before code.
- Confirm how the missing Architrave token/design-map pointers are handled.
- Freeze the legacy/V2 state mapping and `source` versus `evidenceOrigin` delta.

Out of scope: product code, Storybook changes, infrastructure mutation.

Dependencies: none.

Gate:

- Human approves the contract decisions.
- Backend contract includes every endpoint/DTO/error/auth/source/migration rule.
- Adversarial Judge returns PASS on the proposal.
- Worktree ownership decision is recorded.

### Phase 1 — Storybook interaction prototype (`in-progress`)

Scope:

- Reproduce existing Sideport setup, panel, status, empty-state, dialog, and
  pipeline components.
- Replace the Step 2 configuration detour with a direct Apple Account/password
  form, immediate in-memory clearing, existing 2FA screen, and preconfigured-
  credential skip state. Show explicit Apple subprogress, auto-select the sole
  Personal Team in the free-account happy path, and model non-destructive
  certificate creation instead of replacement. Keep it deterministic and
  demo-only.
- Add fixture-only stories for the complete state matrix and interactive
  Apple/device/install path.
- Keep the rail to the six approved macro steps. Inline preflight and show one
  **Install and finish** progression through verify, activate, scheduler enable,
  next evaluation, receipt, and Ready; do not add Review, post-install device,
  or separate automatic-refresh steps.
- Make the app step library-first with the reviewed three-app demo fixture,
  source badges, version/description/icon fallback, secondary IPA import, and a
  read-only installed-iPhone list that never implies IPA extraction.
- Use a deterministic in-story reducer/harness around controlled presentational
  components for stateful interactions; no runtime calls or third-party mock
  service is needed.
- Add `@storybook/addon-vitest`/Vitest as dev-only test tooling because the repo
  currently has no runner for Storybook play/a11y gates; add
  `test:storybook`, a Storybook Vitest project, and set a11y violations to fail
  instead of `todo`.
- Add a deterministic `test:ci` script (lint plus Storybook tests) and point the
  Architrave/CI test gate at it so the new command cannot be skipped.

Out of scope: backend work, live credential submission/API bindings, new visual
system.

Dependencies: Phase 0 contract.

Gate:

- Human confirms the Storybook path at desktop and mobile widths.
- `npm --prefix src/Sideport.Admin run test:storybook` passes render,
  interaction, and a11y checks.
- `gates/checks.sh` and `gates/reconcile.sh` pass.
- Judge PASS on capability honesty and existing-design conformance.

### Phase 2 — Deployment foundation and operational status (`not-started`)

Scope:

- Restore persistent Sideport and anisette volumes in the example manifest.
- Restore optional read-only lockdown mount documentation.
- Fix image pin, Personal Apple ID config key, stable device-ID docs, and Compose.
- Add a small official-CLI Apple `container` 1.1+ launcher, a secret-free env
  example, and focused documentation. It starts native anisette plus the current
  amd64 Sideport image under Rosetta on one explicit network, uses persistent
  named volumes, a configured anisette FQDN, and the forwarded macOS usbmuxd
  socket. It does not translate Compose or add a third-party orchestrator.
- Add shallow `/readyz` plus authenticated operational checks.
- Add the protected Personal Apple connect/2FA contract and one encrypted,
  writable managed credential provider on the persistent Sideport state volume;
  keep environment/SOPS and Keychain providers as read-only preconfigured
  paths.
- Set `CredentialSource=managed` explicitly in official fresh Docker/Apple
  `container` examples while preserving the one-release unset=`environment`
  compatibility rule for existing deployments.
- Preserve or intentionally reconcile the existing scheduler guard and metrics
  changes; do not silently revert them.

Out of scope: live cluster apply, secret inspection/materialization,
certificate or device mutation, multi-account switching.

Dependencies: Phase 0; owner decision for overlapping dirty files.

Gate:

- API tests cover writable/unwritable state, provisioned/unprovisioned anisette,
  executable/missing signer, and auth posture.
- Credential tests cover rejection before body read in open/unsafe transport,
  same-origin/antiforgery and proxy spoofing, production CSP/framing denial with
  no third-party scripts, client/account rate limits, no-store/redaction,
  encrypted file permissions, authentication-before-commit, five-minute
  single-use 2FA under the same boundary, invalid/expiry/restart discard,
  same-account atomic rotation, and different-account conflict.
- Container runs as UID 1000 with writable mounted state.
- `docker compose config`, Kubernetes render, and kubeconform checks pass.
- The Apple `container` launcher is shell-validated, rejects runtime versions
  older than 1.1, never prints secrets, and has deterministic dry-run/status
  coverage. Runtime and physical-device acceptance remain Phase 8.
- Backend checks and judge PASS.

### Phase 3 — Device trust and acceptance (`not-started`)

Scope:

- Carry lockdown/trust evidence through device transport and known inventory.
- Stop treating enumeration as trust.
- Make read paths non-pairing; add the bounded enrollment coordinator that
  waits, pairs, verifies, and accepts from one explicit Add iPhone session;
  keep its waiting outside the install/refresh worker; migrate legacy records
  unverified; and expose USB-first capability through the contract.

Out of scope: UI binding, pairing from GETs, Wi-Fi pairing/install fixes, MDM.

Dependencies: Phases 1–2.

Gate:

- Untrusted/locked/enumeration-only devices cannot satisfy onboarding or install
  preflight.
- Pairing starts only inside an authenticated enrollment session; passive
  discovery and Wi-Fi never initiate it, and multiple USB candidates stop for
  selection before any Trust prompt.
- Trusted USB acceptance persists across API restart.
- Unit/API/migration checks and backend judge gate pass.

### Phase 4 — Apple team selection and safe signer cutover (`not-started`)

Scope:

- Persist selected returned team and signing configuration.
- Add signing preflight and a globally serialized durable cutover operation.
- Replace revoke-all behavior with exact, acknowledged certificate actions plus
  unknown/recovery reconciliation.
- Map Apple errors to structured responses.

Out of scope: multiple Apple accounts in one wizard, credential removal,
paid-team provisioning beyond current connector capability, UI binding.

Dependencies: Phases 1–2.

Gate:

- No certificate is revoked in any unacknowledged or stale-preflight test.
- Valid local identity reuse has no destructive confirmation.
- Durable selection/cutover evidence survives API restart; crash-after-revoke
  recovery never repeats an unacknowledged revoke.
- Secret-redaction, API, recovery, and backend judge gates pass.

### Phase 5 — Pending registration, full preflight, install, and verification (`not-started`)

Scope:

- Add backward-compatible registration lifecycle fields.
- Preserve catalog provenance and add allowlisted GitHub release discovery and
  durable import through the existing upload/inspection pipeline. Extract a
  trusted icon when practical; otherwise keep the generated fallback.
- Extend preflight for installation.
- Add durable install stages, timeout handling, device-side verification, and
  verify-only reconciliation/activation.
- Include `finishOnboarding=true` in preflight `planVersion` and the durable
  operation intent so the later finalizer cannot be attached after confirmation.
- Add verify-existing migration for legacy registrations.
- Route every manual/legacy/scheduled refresh through the same safe preflight
  and operation service.
- Ensure the scheduler ignores pending or unverified registrations.

Out of scope: arbitrary remote URLs, signing directly from GitHub or an iPhone,
launch verification, Wi-Fi first install, distributed execution.

Dependencies: Phases 3–4.

Gate:

- A saved registration alone never completes onboarding.
- A queued operation is never described as finished.
- Install success without device verification remains failed/unknown and does
  not activate the registration.
- Cooperative cancellation is proven, or the install stage uses a killable
  helper boundary; an active abandoned task never releases its lease.
- Migration, operation idempotency, timeout, restart reconciliation, API, and
  judge tests pass.

### Phase 6 — Scheduler truth and onboarding aggregate (`not-started`)

Scope:

- Add durable scheduler settings/status and bounded evaluation history.
- Always host the scheduler but gate evaluation by settings.
- Derive due state from durable verified operations.
- Implement the V2 onboarding aggregate, V1 compatibility fields, and immutable
  completion receipt.
- Add the idempotent post-verification finalizer in the fixed order activate →
  enable scheduler → compute next evaluation → write receipt last, including
  startup/explicit retry that cannot reinstall.

Out of scope: wall-clock windows, notification delivery, per-app custom
schedules.

Dependencies: Phases 2–5.

Gate:

- Scheduler enablement is blocked before verified active registration/cutover.
- UI and API report hourly due-only policy consistently.
- Completion monotonicity, restart persistence, cross-store reconciliation, and
  scheduler dependency/USB-guard tests pass.
- Backend checks and judge PASS.

### Phase 7 — Runtime onboarding integration (`not-started`)

Scope:

- Bind the approved Storybook flow to V2 endpoints.
- Bind the library to live server catalog/upload data, planned configured
  GitHub sources when available, and read-only installed-app matching.
- Replace manual Team ID and registration dead-end.
- Poll the single Install operation through verification and finalization;
  advance to Ready only after its receipt, then route to device detail.
- Keep Setup accessible after completion and route later outages to
  Overview/Diagnostics.

Out of scope: unrelated navigation redesign or global CSS rewrite.

Dependencies: Phases 1 and 3–6.

Gate:

- Playwright completes the mocked fresh-deployment journey and all major failure
  branches without a new dependency.
- Keyboard, focus, screen-reader, responsive screenshot, lint, build, reconcile,
  and judge gates pass.
- No UI label claims launch, trust, install, scheduling, or completion without
  matching backend evidence.

### Phase 8 — Physical-device and restart acceptance (`not-started`)

Scope:

- Build the release candidate image.
- On an approved test deployment, execute the first-run path with a dedicated
  Apple account and a physical unlocked USB iPhone.
- Verify installed app/profile over USB, persistence across restart, identity
  reuse, and scheduler evaluation.
- On Apple silicon/macOS 26 with Apple `container` 1.1+, prove Rosetta Sideport,
  native anisette, named-volume persistence, non-root macOS usbmuxd socket
  forwarding, and a first USB install. Then unplug USB and run a bounded paired-
  Wi-Fi refresh with device-side verification; an uncertain wireless result
  must terminate, release/quarantine the lease safely, and offer USB fallback.
- Capture redacted operation IDs, timestamps, versions, and outcomes.

Out of scope: production apply, raw USB passthrough, native arm64 packaging,
third-party Compose emulation, exposing secret values.

Dependencies: Phases 2–7 and explicit runtime mutation approval.

Gate:

- USB install completes with proven cooperative termination or the approved
  helper-process bound, and device-side verification matches the app.
- Restart preserves catalog, IPA, registrations, known device, Apple
  configuration, signing identity, operation history, scheduler settings, and
  anisette trust.
- No extra certificate is minted on the first post-restart refresh.
- Apple `container` restart preserves both volumes; Sideport reaches the phone
  through the macOS usbmuxd socket without raw device passthrough. The bounded
  Wi-Fi refresh either verifies success or fails safely and succeeds through the
  documented USB fallback.
- Full deterministic gates and judge PASS.

### Phase 9 — Release and rollout handoff (`not-started`)

Scope:

- Pin the newly built release by immutable tag/digest in plan-reviewed IaC.
- Document backup, rollout observation, rollback, and known limitations.
- Produce final gate and run artifacts.

Out of scope: unapproved GitOps reconcile or cluster mutation.

Dependencies: Phase 8.

Gate:

- Release/version endpoint matches the image pin.
- Render/policy/secret scan and full repo gates pass.
- A human approves and performs rollout.
- Post-rollout read-only observation confirms the expected image, storage,
  readiness, and UI/API contract.

## Migration and Rollback

### JSON state

Use additive, backward-compatible JSON fields:

- Missing registration `lifecycle` deserializes as `active`.
- Existing known devices deserialize as `legacy-unverified`.
- New Apple signing, scheduler/evaluation, and immutable onboarding-completion
  stores are separate atomic JSON files under `Sideport:State:Directory`.
- Existing known-device and operation fields are preserved.
- Every write uses temp-file + atomic replace behind the existing process-local
  lock pattern.
- Corrupt JSON returns a structured readiness/API failure and is never silently
  replaced.

There is no cross-file transaction. Safety comes from write-ahead operation
evidence, a fixed commit order, and idempotent startup reconciliation:

1. A signer-cutover operation durably records its confirmed inventory and
   running stage before the first revoke. Identity bytes are atomically
   persisted before Apple configuration and the operation record the new
   fingerprint. An interrupted cutover blocks the account/team and follows the
   explicit reconciliation rules above.
2. An install operation durably records successful device verification before
   writing the registration's `active`, `activatedAt`, and
   `lastVerifiedOperationId` fields.
3. For `finishOnboarding=true`, activation is followed by scheduler enablement
   and persisted next-evaluation computation. The immutable onboarding receipt
   is written last; only then is the operation terminal succeeded and Ready
   available.
4. On startup or explicit finalization retry, a verified operation resumes from
   the first unfinished idempotent finalization boundary. Neither repair repeats
   Apple authentication, signing, upload, installation, or verification.

Reconciliation itself is serialized before the scheduler starts evaluating.
Each repair emits a redacted event and testable result; ambiguous evidence
remains blocked instead of being guessed.

Before a release rollout, the human-operated deployment process backs up:

- `/var/lib/sideport`
- the anisette ADI volume

No run artifact or example manifest contains their contents.

### Deployment rollout

1. Resolve the current dirty manifest changes.
2. Render and validate the manifest; do not apply from the implementation agent.
3. Build and publish a versioned image containing the current SPA and APIs.
4. Confirm state/anisette volume ownership for UID/GID 1000.
5. Keep one replica and `Recreate`.
6. Start with scheduler disabled in the seed configuration.
7. Observe shallow readiness and operational status.
8. Complete signer/device/install setup, then enable the scheduler from the UI.

### Rollback

- Treat rollback as a gated operation, not only an image change. First persist
  scheduler disabled and prepare the deployment-level
  `Sideport__Scheduler__Enabled=false` override, because the older image does not
  understand the new settings store.
- Cancel only V2 operations that are still safely queued. Wait for running work
  to become terminal; reconcile `unknown`/`recovery-required` work. Do not start
  the old image while any pair, cutover, install, refresh, or reconciliation
  record is queued/waiting/running/unknown.
- Resolve or remove `pending-install` registrations before rollback: the older
  scheduler ignores lifecycle fields and would otherwise treat them as due.
- Re-pin the prior image without deleting either persistent volume.
- Additive registration fields keep prior readers compatible; the prior version
  ignores separate new stores only after the drain rules above are satisfied.
- If a prior image cannot read a modified JSON record, restore the pre-rollout
  state backup and retain the newer copy for diagnosis.
- Never “roll back” Apple certificate revocation automatically. The cutover
  operation identifies exactly what changed; recovery requires an explicit
  signer decision.

## Observability and Operational Evidence

Emit structured, redacted events for:

- operational check result changes;
- Apple sign-in outcome and 2FA-required state, never code/password/token;
- team selection;
- signing preflight and cutover operation transitions;
- certificate reuse/mint/revoke by serial suffix only;
- device discovery/trust/acceptance;
- each operation stage and duration;
- device verification;
- scheduler settings/evaluation.

Every mutation receipt includes actor, timestamp, request/correlation ID,
target IDs, outcome, and structured error code. Existing operation history owns
install evidence; Apple configuration, known-device, and scheduler records own
their own actor/timestamp receipt. A generic new audit subsystem is not required
for this slice.

Restore and retain `/metrics` unless the owner explicitly accepts its removal.
Useful metrics include operation stage duration/outcome, install timeout,
trust-state counts, scheduler evaluation outcome, and readiness check state.
Labels must not contain Apple IDs, full UDIDs, tokens, certificate contents, or
unbounded error text.

## Test Matrix

| Layer | Required cases |
| --- | --- |
| Unit | Completion truth table and immutable receipt; `finishOnboarding` included in semantic plan/durable intent; fixed verify→activate→scheduler→next-evaluation→receipt ordering; finalization-only retry; ready-now regression without completion reset; trust/state mapping; selected-team validation; signing plan/inventory hash; stale install/cutover; registration/known-device migration; scheduler ignores pending; device verification/reconciliation. |
| Developer API | Reuse identity; mint with no cert; exact acknowledged replacement; no revoke on missing/stale acknowledgement; crash before/after each cutover stage; exact-ID 404; unexpected inventory; linked recovery; Apple failure mapping; PII/secret redaction. |
| Device | GET never pairs; bounded enrollment wait/success/denial/timeout/recovery and multiple-candidate selection; lockdown success/untrusted/locked/exception; USB vs Wi-Fi capability; cooperative install cancellation or helper-process termination; active-task quarantine; installed-app/profile verification. |
| API integration | Fresh/read-only/corrupt state with UI still ready; managed/preconfigured credential source precedence; connect and connect-2FA secure-boundary rejection before secret/code processing; CSP/framing policy; auth-before-commit; invalid/expired/replayed candidate discard; unprovisioned ADI; every mutation rejected in open mode; capability 403; Apple configured/sign-in/2FA/no-team/team; bounded enrollment wait/pair/Trust/verify/accept, multi-device selection, timeout and recovery; upload errors; pending/legacy registration verification; blocked/ready/stale inline preflight; queued/running/canceled/verified/unknown operations; finish-intent mismatch; finalization failure/retry without reinstall; verify-only reconciliation; legacy and operations refresh share guards; V1 compatibility plus V2 completion. |
| Persistence | Restart after every cutover/install/verification/activation/scheduler/next-evaluation/receipt write boundary; corrupt-store behavior; additive legacy registration and legacy-unverified device load; atomic write failure rollback; idempotent finalization/cross-store repair without external effects. |
| Scheduler | Safe-disabled legacy bootstrap; disabled/enabled; due-only from durable successful verification; startup catch-up without interval replay; no eligible registrations; operational/cutover/pairing/transport/unknown-operation guard; paired-Wi-Fi attempt with bounded failure and USB fallback; system actor; bounded history; next evaluation; restart; pending and legacy-unverified registrations excluded. |
| Storybook | `test:storybook` renders every UI state-matrix row at desktop/mobile and covers direct credential entry, unsafe-entry blocking, preconfigured-account skip, password clearing on submit/navigation, one-use invalid/expired 2FA recovery, team, cutover/recovery, one-action enrollment with automatic waiting/pairing/acceptance and Trust/Developer Mode guidance, six-step rail, inline blocked/stale preflight, one Install-and-finish action, progress/unknown/reconcile, finalization-only retry, and receipt-gated Ready with conditional iPhone guidance. |
| Accessibility | Keyboard-only journey; step/error focus movement; associated field errors; dialog focus trap/return; 2FA-expiry and stage-transition announcements; no poll spam; visible focus; semantic labels; no color-only status; reduced motion; 320px/200%-zoom reflow; WCAG AA. |
| Playwright | Full routed-API six-step happy path plus permission/open-mode mutation rejection, missing credential, Apple failure/throttle/no teams, 2FA expiry, stale team/cutover/install preflight in place, enrollment wait/Trust/selection/untrusted/Wi-Fi/reload-resume, upload/slot/legacy-verification failures, canceled/unknown/reconcile, verify failure, scheduler/receipt finalization failure and no-reinstall retry, reconnect/resume, partial API. |
| IaC/packaging | Current SPA/API present in image, non-root writable mounts, persistent volumes, correct config keys, compose config, Kubernetes render/kubeconform, Apple `container` 1.1+ launcher validation, no secrets. |
| Physical acceptance | Unlocked USB iPhone; one explicit enrollment intent with automatic pairing/acceptance; Trust denial, multiple-device, timeout, and recovery checks; proven cooperative timeout or killable helper; USB installed-app/profile proof; unknown reconciliation drill; restart persistence; identity reuse; due-only scheduler evaluation; bounded paired-Wi-Fi refresh and USB fallback; Apple `container` Rosetta/anisette/volume/usbmux proof. |

No live Apple/device test runs without explicit approval and a designated test
account/device. The operational runbook's bounded-call and USB verification
rules remain mandatory.

## File-Level Change Map

Contract/design:

- `docs/sideport-backend-contract.md`
- `docs/architecture/adr-0001-roadmap-foundations.md` only if readiness or
  certificate authorization changes require a lasting decision
- `docs/ui/sideport-ui-design-spec.md`
- `docs/ui/sideport-ui-data-contract.md`
- Storybook design-map/token pointers if the owner establishes them

Backend/API:

- `src/Sideport.Api/Program.cs`
- `src/Sideport.Api/AppleAccess/PersonalAppleAccess.cs` for candidate-session,
  connect-challenge, and credential-provider seams
- new focused contracts/services under:
  - `src/Sideport.Api/Readiness/`
  - `src/Sideport.Api/AppleAccess/`
  - `src/Sideport.Api/Onboarding/`
  - `src/Sideport.Api/Scheduling/`
- `src/Sideport.Api/DeviceInventory/KnownDeviceContracts.cs`
- `src/Sideport.Api/DeviceInventory/KnownDeviceService.cs`
- `src/Sideport.Api/Operations/OperationContracts.cs`
- `src/Sideport.Api/Operations/OperationService.cs`
- `src/Sideport.Api/Operations/OperationScheduler.cs`
- `src/Sideport.Api/Operations/OperationWorker.cs`

Developer/signing:

- `src/Sideport.DeveloperApi/AppleDeveloperPortal.cs`
- `src/Sideport.DeveloperApi/PortalSigningIdentityProvider.cs`
- `src/Sideport.DeveloperApi/PortalSigningOptions.cs`
- `src/Sideport.Core/IAppleDeveloperPortal.cs`
- `src/Sideport.DeveloperApi/ContainerAnisetteProvider.cs` only for bounded
  provisioned-header readiness

Device/orchestrator:

- `src/Sideport.Devices/IDeviceBackend.cs`
- `src/Sideport.Devices/NetimobiledeviceBackend.cs`
- `src/Sideport.Devices/NetimobiledeviceController.cs`
- `src/Sideport.Orchestrator/AppRegistration.cs`
- `src/Sideport.Orchestrator/FileAppRegistry.cs`
- `src/Sideport.Orchestrator/RefreshOrchestrator.cs`
- `src/Sideport.Orchestrator/OrchestratorOptions.cs`

Admin UI:

- `src/Sideport.Admin/src/api/sideportApi.ts`
- `src/Sideport.Admin/src/data/sideportTypes.ts`
- `src/Sideport.Admin/src/data/sideportFixtures.ts`
- `src/Sideport.Admin/src/App.tsx`
- extract only the touched onboarding/install composites into
  `src/Sideport.Admin/src/onboarding/` if that reduces the current monolith;
  do not create a parallel component system
- `src/Sideport.Admin/src/SideportAdmin.stories.tsx`
- focused new onboarding stories
- `src/Sideport.Admin/src/App.css` only through existing values/classes unless a
  token is first established
- `src/Sideport.Admin/tests/admin-screens.spec.ts`
- `src/Sideport.Admin/.storybook/main.ts`
- `src/Sideport.Admin/.storybook/preview.ts`
- `src/Sideport.Admin/package.json` and lockfile for the dev-only Storybook
  Vitest gate
- Storybook/Vitest configuration required by `test:storybook`
- `architrave.config.json` and the existing CI/gate entry point so `test:ci`
  is enforced

Deployment/docs:

- `deploy/Dockerfile`
- `deploy/compose.yaml`
- `deploy/k8s/deployment.yaml`
- `deploy/k8s/secret.example.yaml`
- `deploy/k8s/README.md`
- `deploy/apple-container/run.sh`
- `deploy/apple-container/sideport.env.example`
- `deploy/apple-container/README.md`
- `README.md`

Tests:

- `tests/Sideport.Api.Tests/ApiSmokeTests.cs`
- focused new API unit/integration test files rather than continuing to grow the
  smoke-test monolith where practical
- `tests/Sideport.DeveloperApi.Tests/`
- `tests/Sideport.Devices.Tests/`
- `tests/Sideport.Orchestrator.Tests/`

## Final Acceptance Criteria

Implementation is complete only when:

1. A versioned fresh deployment serves the current UI and V2 API with persistent
   Sideport and anisette storage.
2. On an authenticated secure connection, the UI can establish the first
   managed Apple credential in Step 2 without leaving onboarding; it clears the
   password immediately and never returns, redisplays, persists in browser
   storage, logs, or records it. Preconfigured environment/SOPS/Keychain
   custody skips entry.
3. A configured Personal Apple ID can sign in, complete an expiring single-use
   2FA challenge, and persist a returned team selection.
4. Sideport cannot revoke a certificate without a current, exact,
   server-validated impact acknowledgement and a globally serialized durable
   cutover operation; crash recovery never broadens that authorization.
5. An enumerated but untrusted/locked device is not called healthy or accepted.
6. Read-only discovery never pairs or accepts. One explicit **Connect iPhone**
   session can wait for a USB iPhone, pair automatically, observe Trust,
   verify lockdown, and accept it into durable inventory without more UI
   confirmations; ambiguous recovery never repeats pairing blindly.
7. A ready IPA can be selected/imported without retyping detected bundle/team
   facts.
8. The onboarding rail has exactly six steps: Check Sideport, Connect Apple,
   Connect iPhone, Choose app, Install, and Ready. Preflight, conditional
   post-install iPhone guidance, and automatic refresh are not separate steps.
9. Inline Install preflight shows real blockers, warnings, scarce limits, and
   mutations before execution; stale semantic changes require confirmation in
   place.
10. One **Install and finish** press submits `finishOnboarding=true` covered by
    `planVersion` and persisted in durable operation intent. The operation shows
    real stages and no false “finished” copy while queued/running. The device
    stage either proves cooperative termination or runs in a killable process;
    ambiguous state is quarantined and verify-only reconciled before rerun.
11. Completion follows verify → activate → enable scheduler → compute next
    evaluation → write receipt last. Finalization retry after verification never
    signs or installs again, and Ready never appears before the receipt.
12. Device-side bundle/profile verification is required and never claims a
    launch check. Conditional profile-trust/open-app guidance appears as a small
    non-blocking note on Ready.
13. The verified registration becomes scheduler-eligible; pending/failed
    registrations do not, and legacy registrations have a verify-only migration
    path without reinstall.
14. Scheduler status and enable/disable are live outside onboarding; the setup
    happy path enables the actual hourly due-only policy automatically and shows
    its next evaluation, single-flight state, paired-Wi-Fi path, and USB fallback.
15. An immutable completion receipt keeps setup history complete after a later
    outage or resource removal, while `readyNow` and recovery surfaces
    truthfully show current state.
16. Restart preserves all state and reuses the Sideport identity without new
    2FA/certificate churn when dependencies are unchanged.
17. Storybook is human-approved before runtime UI work; deterministic gates,
    physical acceptance, and Adversarial Judge all pass.
18. No user-owned work is overwritten, no secret is materialized, and no
    infrastructure change is applied by the implementation agent.
19. The legacy onboarding DTO remains usable for one compatibility release
    while the new UI reads the V2 workflow.
20. Cross-store restart reconciliation repairs only from durable evidence and
    never repeats an Apple/device side effect.
21. Legacy synchronous, manual operation, retry/rerun, and scheduler refresh
    entry points all enforce the same verified-registration, signer, pairing,
    transport, operational, timeout, and quarantine preflight.
22. Open-behind-proxy mode is read-only until Sideport has a verifiable identity;
    every onboarding mutation requires bearer or OIDC authentication.
23. Apple `container` 1.1+ on Apple silicon/macOS 26 can start the two required
    containers without Compose, persist both identities, run Sideport's amd64
    image under Rosetta, reach macOS usbmuxd as a non-root process, complete the
    first USB install, and execute or safely fall back from a bounded Wi-Fi
    refresh without exposing a secret.
