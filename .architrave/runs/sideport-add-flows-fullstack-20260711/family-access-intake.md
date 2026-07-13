# Family Access Intake and Recommended Plan

Date: 2026-07-12
Status: accepted requirement and contract; Phase 9 implementation in progress

The canonical planned contract is now the **Family Membership and
Authorization** section of `docs/sideport-backend-contract.md` plus ADR 0002.
Those stronger rules supersede the preliminary API sketch below where they
differ.

## Requirement

The Sideport owner can create a secure invitation for a family member. The
member accepts it after authenticating and gets a constrained family-member
workspace role. Apple Account login is preferred when an official provider is
available, but accepting an invitation must not depend on it.

## Capability truth

- Official Sign in with Apple for the web requires an Apple Developer Program
  membership, a Services ID associated with an eligible App ID, a domain, and
  signing key material. A free Personal Team cannot provide that web-login
  capability. Family members need ordinary Apple Accounts; they do not each
  need a paid membership.
- Authentik remains the OIDC identity provider. Sideport must not authenticate a
  portal user with the private GrandSlam/Developer Services signing session.
- Apple login and Apple signing are separate. The current signer is one
  process-wide owner-managed Apple account; per-member Personal Team signers
  require a separate multi-signer design and are out of this phase.
- Current Sideport behavior makes every authenticated OIDC principal
  owner-equivalent. Invitations cannot ship until server-side membership and
  capability enforcement replace that behavior.

## Tournament of options

### A. Authentik owns users, invitations, and authorization

Sideport calls Authentik administration APIs and trusts group/role claims.

- Advantage: reuses the deployment identity system.
- Cost: provider-specific secrets and APIs become required; Sideport's Invite
  UI cannot work with another OIDC provider; claim drift can silently change
  product authority.
- Verdict: not the default. A bounded delivery adapter may be added later.

### B. Sideport owns membership; Authentik proves identity — selected

Sideport creates one-time invitations and persists the resulting membership.
Acceptance requires an authenticated OIDC principal and atomically binds the
invite to immutable `(issuer, subject)` claims. Authentik may offer Apple,
passkey, or another login without changing Sideport authorization.

- Advantage: provider-neutral, testable authorization and an in-product invite
  experience without building another password system.
- Cost: requires a durable invite/member store and enforcement at every API
  boundary.
- Verdict: selected.

### C. Use Apple Developer authentication as Sideport login

Treat a successful Apple password/2FA GrandSlam session as the member identity.

- Advantage: appears to avoid a paid Sign in with Apple configuration.
- Cost: unsupported auth semantics, password/2FA custody, no standard relying
  party contract, account lockout risk, and catastrophic coupling between login
  and signing authority.
- Verdict: rejected.

### D. Sideport implements local passwords/passkeys

- Advantage: no external identity provider.
- Cost: duplicates Authentik and adds credential recovery, enrollment, session,
  and breach responsibilities unrelated to Sideport.
- Verdict: rejected. Authentik already supplies the no-fee fallback.

## Minimum contract

1. Persist members by immutable OIDC issuer and subject, never by email.
2. Create at least 256-bit random invitation tokens; store only their hashes.
   Tokens are single-use, expire, are revocable, and are returned only once as a
   copy/share URL. SMTP and Authentik administration integration are optional
   later delivery adapters.
3. An authenticated acceptance binds the invite atomically to the current OIDC
   principal. Apple Hide My Email cannot break or redirect membership because
   the contact email is display/delivery data, not authority.
4. Add a default `family-member` role limited to the shared app library and the
   member's own accepted devices, installs, refresh operations, and diagnostics.
   It cannot manage Apple credentials/certificates, GitHub sources, other
   members, scheduler-wide settings, or other members' devices.
5. Owner-only APIs create/revoke invitations and change/offboard members. The
   last owner cannot be removed or demoted.
6. Enforce capabilities and resource ownership on the server for every read and
   mutation. The UI is only a reflection of those authoritative capabilities.
7. Cookie mutations require same-origin/CSRF protection; invite endpoints are
   rate-limited, return generic failures, use `Cache-Control: no-store`, and
   record non-secret audit events.
8. Bootstrap the first owner explicitly; never grant owner to an arbitrary first
   OIDC login. The existing bearer token remains owner-equivalent for recovery.

## Proposed API surface

- `GET /api/workspace`
- `POST /api/workspace/invitations`
- `POST /api/workspace/invitations/{invitationId}/revoke`
- `POST /api/workspace/invitations/handoff`
- `POST /api/workspace/invitations/handoff` receives the fragment token once and
  replaces it with a short-lived opaque HttpOnly cookie before OIDC;
  authenticated `GET /api/workspace/invitations/handoff` returns the safe
  account/workspace/permission preview;
  `POST /api/workspace/invitations/accept` consumes that handoff after explicit
  post-login confirmation, so API/proxy logs never see the token as a path or
  query value
- `PATCH /api/workspace/members/{memberId}`
- `POST /api/workspace/members/{memberId}/offboard`
- `POST /api/workspace/owner-claims`,
  `/owner-claims/{claimId}/revoke`, `POST` and authenticated `GET`
  `/owner-claims/handoff`, and `/owner-claims/accept`
- `GET /api/workspace/audit`

The final contract must define idempotency, conflicts, expiry, actor evidence,
role/capability matrices, resource ownership, and bootstrap migration before
implementation begins.

## UI outcome

The owner chooses **Invite family member**, enters the email where they intend
to send the copied link, and
gets one clear **Share invitation** action. The acceptance screen says which
workspace and family-member permissions are being granted. Authentik owns the
login chooser. Sideport may label the verified provider as **Apple via
Authentik** only when OIDC claims prove that path; otherwise it uses neutral
account language. Technical details remain collapsed.

## Phase gate

Phase 8 requires the canonical contract, security tournament, threat model,
authorization matrix, independent review, and human sign-off before Phase 9
runtime implementation. Phase 9 then requires focused tests for token
replay/expiry, CSRF, privilege escalation, cross-member resource access,
last-owner safety, audit redaction, and fail-closed migration/rollback.
