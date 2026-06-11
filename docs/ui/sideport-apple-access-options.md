# Sideport Apple Access Options

Date: 2026-06-09
Status: research-backed pilot plan

## Goal

Sideport should get as much Apple account/developer data automatically as possible without asking users to paste Apple ID passwords into the portal. The product should separate two different ideas:

1. Logging a human into Sideport.
2. Granting Sideport authority to read/mutate Apple Developer resources.

Those are not the same Apple API surface.

## Key Finding

Sign in with Apple/OIDC is excellent for Sideport user identity, but it does not grant Apple Developer API access. The official passwordless developer-resource path is App Store Connect API JWT with a **team API key**.

App Store Connect API has official provisioning resources for:

- certificates
- devices
- bundle IDs
- bundle ID capabilities
- profiles

That means JWT is not just a read-only app-store reporting option; it is a serious candidate for Sideport’s primary Apple access connector for paid Developer Program teams **after a read-only probe verifies provisioning access**.

Adversarial constraints:

- Individual API keys cannot use Provisioning endpoints; they are not sufficient for Sideport signing.
- A team API key is broad team access, not a user OAuth grant.
- Browser login may help the operator create/download an API key, but Sideport must not scrape Apple web cookies or sessions.
- Any action that creates/revokes certificates, registers devices, creates bundle IDs, or creates profiles needs preflight and explicit confirmation.

## Option Matrix

| Option | What Sideport can get | Developer resources? | Secret custody | Feasibility | Verdict |
| --- | --- | --- | --- | --- | --- |
| Sign in with Apple / OIDC | User identity, email, stable Apple subject, refresh token for portal login | No teams/devices/certs/profiles | No Apple password | High | Use for Sideport login only. Do not label as Developer Account connection. |
| App Store Connect API team key / JWT | Team-scoped App Store Connect and provisioning resources | Yes: devices, certs, bundle IDs, profiles if role/agreements permit | Server stores `.p8`, key ID, issuer ID | High for paid teams | Primary pilot only after read-only probe verifies access. |
| App Store Connect individual API key | User-scoped ASC data | No Provisioning endpoints | Server stores `.p8` | Medium | Useful for app/account inventory, not signing. |
| GrandSlam Apple ID session | Apple ID session, teams, developer-services signing resources | Yes via existing Sideport backend seams | Password/session sensitive; use Vaultwarden bridge or SOPS/age on Ubuntu | Already partly implemented | Default path for free/personal teams. |
| Vaultwarden via Bitwarden CLI bridge | UI-editable Apple credential read by Sideport through `bw serve` | Yes via Personal Apple ID connector | Vaultwarden stores the secret; bridge must stay private/loopback | High for homelab | Recommended Ubuntu homelab credential source. |
| SOPS/age Kubernetes Secret | Host-side Apple credential injected into the Ubuntu Sideport pod | Yes via Personal Apple ID connector | SOPS-encrypted in Git, decrypted by Flux/cluster | High for homelab | Bootstrap/fallback path. |
| Local macOS helper | Can hold ASC key or Apple ID session locally; can call Sideport/server | Depends on connector inside helper | macOS Keychain/local only | Medium-high effort | Local-dev or remote-helper fallback, not Ubuntu server default. |
| Browser-assisted login | Helps the operator navigate App Store Connect to create an API key | No direct connector | No browser-cookie capture | High risk if scraped | Use only as guided setup. Never scrape cookies/session. |
| DeviceCheck/App Attest | Device/app integrity signal, not account inventory | No | App/server keys | Not useful for developer data | Use later for companion app trust, not Apple account setup. |
| Developer MCP connector | A local tool surface wrapping ASC JWT, GrandSlam, probes, diagnostics | Depends on wrapped connectors | Depends on deployment | Good internal operator tool | Build as local read-only probe first, then controlled mutation tool. |

## Recommended Portal UX

### Step 1: Sideport Identity

Offer normal portal login options. Apple is optional here, never mandatory:

- reverse proxy auth
- local owner account later
- Sign in with Apple as an optional portal identity provider

Copy:

`This signs you into Sideport. It does not grant Sideport access to Apple Developer resources.`

Product rule: users must be able to run Sideport behind reverse-proxy auth or a local owner account without enabling Sign in with Apple. Sign in with Apple is convenience identity, not a prerequisite for signing or refresh.

### Step 2: Connect Apple Access

Show a connector chooser:

1. `App Store Connect API key` - recommended for paid teams after probe verifies provisioning access.
2. `Personal Apple ID via Vaultwarden` - recommended for this Ubuntu homelab and free-account signing.
3. `Personal Apple ID via SOPS/age` - bootstrap/fallback when the vault bridge is unavailable.
4. `Local Apple ID helper` - optional local-dev/remote-helper fallback.
5. `Apple ID session` - advanced/self-hosted fallback.
6. `Manual Team ID` - metadata only, not verified.

For free-account Sideport on the Ubuntu homelab, the default path is `Personal Apple ID via Vaultwarden`, not App Store Connect JWT and not macOS Keychain. The operator enters only the Apple ID and 2FA code in the portal; the Apple password comes from host-side custody through Vaultwarden/`bw serve` or the SOPS fallback.

Each connector gets source/custody badges:

- `Official API`
- `Passwordless`
- `Stored on server`
- `SOPS/age encrypted in Git`
- `Mounted as Kubernetes Secret`
- `Stored in Vaultwarden`
- `Read through private bw serve bridge`
- `Stored in macOS Keychain` (local helper only)
- `2FA may be required`
- `Can mutate Apple Developer resources`
- `Read-only probe`

### Step 3: Capability Probe

Before any signing/install UI is enabled, run a read-only probe and show exact capability results.

Probe output should include:

- Apple account connector type
- issuer ID / key ID, redacted
- teams/providers visible to this connector
- selected team
- role or permission level if available
- can list devices
- can register devices
- can list certificates
- can create/revoke certificates
- can list bundle IDs
- can create bundle IDs
- can list profiles
- can create/download profiles
- current cert count/expiry if readable
- current device registration status for connected iPhone
- current bundle ID/App ID status for selected IPA
- rate-limit/backoff state if returned

UI states:

- `Verified`
- `Missing permission`
- `Requires Account Holder/Admin`
- `Agreement or role blocked`
- `2FA required`
- `Unsupported by connector`
- `Not checked yet`

## Pilot Plan

### Pilot A: ASC JWT Read-Only Probe

Build a small backend seam and CLI/API endpoint that accepts an App Store Connect API key reference from environment/SOPS or local file, signs a short-lived ES256 JWT, and probes:

- `GET /v1/devices`
- `GET /v1/certificates`
- `GET /v1/bundleIds`
- `GET /v1/profiles`

Optional probes:

- list apps
- list users/access if available to the key
- inspect provider/team metadata if exposed

Success criteria:

- We can list devices, certificates, bundle IDs, and profiles for the team.
- We can map Apple provider/team context to a user-facing connector row when Apple exposes enough metadata.
- Errors classify missing role, bad key, expired token, permission denied, and rate limit.
- No mutations are performed and no private key material is echoed in logs or responses.

### Pilot B: ASC JWT Safe Mutation Probe

Only after Pilot A succeeds and an operator explicitly approves destructive-risk testing, test safe controlled mutations in a disposable bundle/device context:

- register a test device if not already present
- create or find a test bundle ID
- create a development certificate from CSR if allowed
- create/download a development profile

Success criteria:

- Sideport can prepare all signing inputs via official API without Apple ID password.
- Mutations are auditable and reversible where Apple allows reversal.
- Certificate creation/revocation behavior is understood before exposing it in UI.
- Certificate revocation is never bundled into a routine refresh action without a separate cutover acknowledgement.

### Pilot C: Sign in with Apple for Portal Identity

Add Sign in with Apple as a possible login provider for Sideport itself, not Apple Developer access.

Success criteria:

- Portal can identify user and email.
- UI copy clearly separates portal identity from Apple Developer authority.

### Pilot D: Local Apple Helper

Build a local-only helper concept for free/personal-team compatibility:

- stores Apple ID credential/session in macOS Keychain, or stores ASC key locally
- performs GrandSlam or ASC probe locally
- exposes a narrow localhost API to Sideport
- asks for explicit approvals before Apple mutations

Success criteria:

- Sideport server never sees Apple ID password.
- Helper can complete 2FA locally if needed.
- Helper can return teams/capabilities/status to portal.

This is not the default for the Ubuntu server. Use it only when Sideport must borrow credentials held on a Mac instead of storing them in the homelab secret system.

### Pilot F: Personal Apple ID Connector

Use the existing GrandSlam + anisette backend seams to support free-account signing setup without paid App Store Connect access.

Implemented first slice:

- `GET /api/apple-access/personal/status`
- `POST /api/apple-access/personal/sign-in`
- `POST /api/apple-access/personal/2fa`

Rules:

- Browser never sends Apple password.
- Password is resolved from host-side secret custody. In the homelab, prefer Vaultwarden through the `Sideport:Apple:CredentialSource=vault` bridge; SOPS/age env or mounted file stays the bootstrap/fallback path.
- The portal may send Apple ID and 2FA code locally over the protected Sideport API.
- Teams are listed only after authentication succeeds.
- Signing mutations still require a separate preflight and cutover confirmation.

Next tasks:

- Persist selected personal team.
- Expose signing identity status for the selected personal team.
- Add preflight for cert mint/reuse, App ID reuse/create, profile creation, device registration, and install readiness.
- Add Sideport deployment manifests for Vaultwarden/`bw serve` bridge configuration.
- Add Sideport deployment manifests for a SOPS-encrypted Apple credential Secret fallback.
- Add an optional local helper only for non-homelab/local-dev custody.

### Pilot E: Developer MCP Connector

Create an internal MCP-style connector for operator workflows:

- read-only Apple account inventory
- list devices/certs/profiles/bundle IDs
- run preflight probes
- optional mutation tools gated by explicit confirmation

This is not an Apple-provided account type. It is a Sideport/local operator tool that wraps official ASC JWT and/or existing GrandSlam seams.

Success criteria:

- Useful for development and diagnostics without adding UI complexity first.
- Provides the same structured read model the portal will later consume.

## Implementation Notes

- Never paste `.p8` private key, Apple passwords, 2FA codes, or sessions into chat/logs.
- Store Apple passwords in Vaultwarden for the Ubuntu homelab when the private `bw serve` bridge is available. Store bootstrap/fallback secrets as SOPS/age encrypted Kubernetes Secrets. macOS Keychain is only a local-helper option.
- Generate JWTs with ES256, `kid`, `iss` or `sub`, `aud=appstoreconnect-v1`, and short `exp` windows.
- Prefer scoped JWTs for read-only probes where possible.
- Treat API keys as destructive credentials if they can create/revoke certificates or profiles.
- Redact key ID/issuer ID in logs except short suffixes.
- Do not store Apple ID passwords in browser storage.
- Do not accept Apple ID passwords from the browser; use Vaultwarden/Bitwarden bridge or SOPS/age host-side secret custody on Ubuntu, or an explicitly paired local helper.

## Product Recommendation

Make `Personal Apple ID via Vaultwarden` the first-class connector for this Ubuntu homelab and free-account signing, with SOPS/age as bootstrap/fallback. Make `App Store Connect API team key` the paid-team connector only after the probe verifies provisioning access. Make `Sign in with Apple` optional portal login only and never a signing prerequisite. Keep a local helper as an optional safety valve for users who want credentials held on a Mac, not as the server default.

The immediate next build should be Pilot A: ASC JWT read-only probe and an Apple Access page that shows connector status and capability results.