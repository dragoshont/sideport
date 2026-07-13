# ADR 0002: Family Access Authorization

Date: 2026-07-12
Status: accepted — Phase 8 contract gate passed; Phase 9 implementation pending

## Context

Sideport must let one owner invite nontechnical family members who authenticate
through Authentik, connect their own iPhone once over the homelab cable, install
an owner-approved app, and receive later refreshes over paired home Wi-Fi when
that transport works. Authentik already owns identity proof, passkeys, sessions,
and account recovery. Sideport owns Apple signing, devices, apps, operations,
and the product-specific question of who may act on which resource.

The current runtime cannot admit family members safely. Any authenticated OIDC
principal passes the global API gate and is projected as an Owner. Device
ownership is editable free text, and most cookie-authenticated mutations do not
yet share one antiforgery boundary. This ADR must be accepted and implemented
before an invitation can be created or accepted.

The repository remains a single-process, single-replica service with atomic
JSON stores. ADR 0001 continues to govern that storage and execution model.

## Decision

### Identity and authority remain separate

- Authentik proves a human identity and owns passkey enrollment, login,
  sessions, and recovery.
- Sideport persists membership and authorizes every Sideport read and mutation.
- A human member is keyed by the exact validated OIDC `(issuer, subject)` pair.
  Email, username, display name, upstream provider, and Apple Hide My Email are
  presentation or delivery data, never authority.
- Sideport does not implement passwords, passkeys, CalDAV authentication, or a
  substitute for Sign in with Apple. GrandSlam, anisette, Apple Developer
  Services, and the owner signing credential are never portal-login evidence.

### Roles and bootstrap

- The first family release has exactly two roles: `owner` and `family`.
- Exactly one active human Owner is supported. Ordinary invitations always
  create Family members; there is no promotion or second-owner UI.
- The first Owner is never the first person who happens to log in. Claiming or
  recovering Owner requires a short-lived, hashed, single-use Owner-claim link
  minted by tooling authenticated with the deployment's existing recovery
  bearer, then explicit acceptance by the claimant's validated OIDC session.
  The long-lived bearer remains in the standard Authorization header and is
  never pasted into the browser UI.
- The Owner-claim fragment is exchanged once for an opaque HttpOnly handoff
  before OIDC. After sign-in, an authenticated preview names the actual account
  and exact setup/recovery impact before explicit acceptance. A lost one-time
  link is revoked, including its handoffs, before tooling mints a replacement.
- A family-capable OIDC deployment requires that recovery bearer. Missing proof
  is an explicit setup blocker; deployment tooling provides one local command
  that reads it without echoing and returns only the short-lived Owner link.
- An explicit recovery claim may replace an inaccessible Owner. The mutation
  names the existing Owner and exact impact, atomically activates the claimant,
  suspends the previous Owner, and never leaves the workspace with zero active
  Owners.
- The configured API bearer remains a non-human, Owner-equivalent break-glass
  actor. It is never represented as a family member and cannot accept an
  invitation.

### Invitation authority

- Possession of an unexpired private invitation link plus any valid Authentik
  OIDC identity is sufficient to claim the Family membership. There is no
  second owner approval and no email pre-binding; the owner must share the link
  through a private channel.
- Sideport creates a 256-bit random secret, stores only its SHA-256 hash, and
  returns the share URL once. The URL keeps the token in the fragment so it is
  not sent in HTTP paths, query strings, referrers, proxy logs, or OIDC state.
- The public shell immediately exchanges the in-memory fragment for a separate
  ten-minute opaque `Secure`/`HttpOnly` handoff cookie, clears the fragment, and
  never stores raw authority in browser storage. After OIDC, the member sees the
  workspace, actual signed-in account and permissions and explicitly chooses
  **Join Sideport** before membership changes.
- The approved first UI requires the email where the Owner intends to send the
  copied link. Sideport does not send mail; email remains owner-private delivery
  metadata and is never an identity constraint.
- Invitations are single-use, expire after seven days by default, may be
  revoked, and bind atomically to one immutable OIDC identity. Replays by the
  same accepted identity are idempotent; all other replay is denied.
- An existing suspended or offboarded identity cannot use a new invitation to
  bypass its status. The Owner must explicitly restore it.

### Resource scope and one-click installs

- The catalog is shared and read-only for Family. Importing or approving an IPA,
  changing GitHub sources, or changing the Apple signer remains Owner-only.
- Family may see the approved mock's minimal active-household directory: display
  names, roles, and coarse accepted-iPhone counts. It never receives another
  member's email, stable ID, status, last-active time, invitation, device ID, or
  activity.
- Every accepted iPhone has a stable `ownerMemberId`. A Family enrollment can
  claim only a new, unassigned physical phone and always assigns it to the
  current member. Family self-service is limited to its first active iPhone;
  the Owner may deliberately enroll additional phones for a named active member
  after normal capacity preflight. Ownership reassignment is deferred.
- Registrations inherit the device owner. Operations snapshot both the actor
  member and target owner. Family reads and actions are filtered to its own
  resources; legacy or unassigned resources are Owner-only.
- Family may run normal preflight/sign/install/refresh for an approved catalog
  app on its own phone without a second approval. It may use the already active
  signer and non-destructive provisioning within reported limits. It may not
  authenticate an Apple account, select a team, mint/replace/revoke a signing
  certificate, import an artifact, override a scarce-limit blocker, or perform
  signer cutover. Those states stop with `owner-action-required`.

### Suspension and offboarding

- Membership status is resolved from the durable store on every request. A
  cookie cannot preserve access after suspension or offboarding.
- Queued workers, scheduler execution, retries, and the last pre-effect device
  or Apple boundary recheck membership and ownership; submission-time authority
  cannot outlive a later suspension.
- Suspension immediately denies new reads and mutations, cancels safe queued
  work, and prevents the scheduler from starting another refresh for the
  member's devices. Running Apple/device work is not blindly canceled; it is
  allowed to reach a safe terminal or unknown/reconciliation boundary and is
  visible to the Owner.
- Offboarding is a soft, audited state transition after an exact impact
  preflight. Devices, registrations, operation evidence, and audit history are
  retained and frozen until the Owner restores the member or removes supported
  resources.
- Sideport never claims remote uninstall, Apple credential revocation, profile
  revocation, or device wipe as an effect of suspension or offboarding.

### Browser and audit boundary

- All cookie-authenticated `POST`, `PUT`, `PATCH`, and `DELETE` requests require
  exact same-origin validation and ASP.NET antiforgery validation. Bearer-only
  machine requests do not use cookie CSRF state.
- Login return paths are local-path allowlisted. Logout becomes a protected
  POST; OIDC and GitHub callbacks retain their independent signed state checks.
- Workspace and invitation responses use `Cache-Control: no-store`. Handoff
  token bodies are small, JSON-only, rate-limited, never included in generated
  string output, and accepted only over effective HTTPS or the existing
  explicit loopback development exception. Recovery bearer use stays in the
  standard Authorization header.
- Membership, invitation, recovery, suspension, and offboarding mutations write
  an allowlisted audit event in the same atomic workspace transaction. Events
  contain stable member/invitation IDs and outcomes, never raw OIDC subject,
  email, IP address, invitation token/hash, recovery value, or Apple secrets.

## Alternatives considered

### Trust Authentik groups as Sideport authorization

Rejected as the primary authority. It couples the product to one identity
provider and lets claim drift silently change access to Apple signing and
physical devices. Authentik remains the identity provider; Sideport persists
the resulting product membership.

### Promote the first OIDC login to Owner

Rejected. A race, proxy mistake, or newly reachable login page would grant full
signing authority to an arbitrary principal.

### Pre-bind invitations to email or require a second approval

Rejected for the first release. Email is not an immutable OIDC identifier and
Apple relay addresses can change the visible address. A second approval makes
the family journey unnecessarily technical. The private bearer link is the
authority and the acceptance screen must say so clearly.

### Permit multiple Owners

Deferred. It adds promotion, demotion, owner-to-owner visibility, quorum and
recovery policy without being required by the current single-admin family
scenario. Recovery supports an explicit Owner replacement instead.

### Require owner approval for every Family install

Rejected. It defeats the approved-app, one-action experience. Server preflight,
resource scope, catalog approval, scarce-limit checks, and the Owner-only signer
cutover boundary provide the required control.

### Continue refresh after suspension or remotely remove apps

Rejected. Continuing uses the Owner's signing resources for an offboarded
principal; remote removal/revocation is not a capability Sideport can truthfully
guarantee. Future refreshes stop, while installed apps expire naturally unless
the Owner performs a separately supported action.

## Storage and migration consequences

- `workspace-access.json` is a new versioned atomic store under
  `Sideport:State:Directory`. It contains members, invitation hashes,
  idempotency records, and bounded security-audit events so a security mutation
  and its audit record commit together.
- Missing workspace state means `bootstrap-required`; it never means the current
  OIDC principal is Owner. Corruption fails closed with a structured 503 and is
  never overwritten.
- Known devices expand with `ownerMemberId`; the existing free-text `owner`
  remains display-only for one compatibility release. Registrations derive
  scope from their device. Historical records without a stable owner are
  Owner-only and are never matched by name or email.
- The migration is expand-only and idempotent. Existing single-owner resources
  remain visible to the Owner without being silently assigned to a Family
  member.
- Rollback to a build that treats every OIDC principal as Owner is prohibited
  after any Family member is admitted unless Authentik/ingress access is first
  restricted back to the Owner. State and pairing volumes are retained; no
  rollback deletes member, invitation, operation, signer, or device evidence.
- Restoring an older workspace snapshot is performed with Family access closed,
  rotates a random workspace security epoch, revokes all pending invitation/
  claim/handoff authority, invalidates local cookies, and requires Owner review
  of restored member status before access reopens.

## Consequences

- Family authorization remains provider-neutral and testable without building
  another credential system.
- A leaked live invitation can be claimed by any Authentik identity, so private
  delivery, short expiry, one-time use, owner revocation, fragment handling,
  CSP, no-store responses, and audit are required controls.
- The recovery bearer remains a high-value break-glass secret. Possession
  already grants Owner-equivalent API authority; Owner claim does not broaden
  that trust but must still be transport-protected, rate-limited, and audited.
- The single-replica JSON-store constraint remains. Multi-owner governance,
  multi-replica authorization, SMTP delivery, directory sync, MDM, and native
  desktop relay are separate future decisions.
