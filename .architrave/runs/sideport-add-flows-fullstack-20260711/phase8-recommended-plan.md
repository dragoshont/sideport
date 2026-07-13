# Phase 8 Recommended Plan for Phase 9

Date: 2026-07-12
Status: accepted as the Phase 9 implementation plan

## Outcome

Phase 9 replaces owner-equivalent OIDC access with durable Owner/Family
membership and server-enforced resource scope. At its end, an unknown OIDC
principal is powerless except for explicit Owner claim or invitation
acceptance, and a Family principal can safely use only approved apps and owned
devices. Authentik passkey/enrollment configuration remains Phase 10.

## Acceptance criteria

1. Exact validated `(issuer, subject)` is required for every OIDC actor; missing
   or unknown identity fails closed. The first login never becomes Owner.
2. One atomic versioned workspace store persists the sole Owner, Family
   members, hashed single-use invitations, idempotency, and redacted audit.
3. Recovery-bearer tooling explicitly mints a short-lived Owner claim that an
   OIDC claimant accepts; lost links are explicitly revoked before replacement
   and the long-lived bearer never enters the browser. Regular invitations are
   fixed Family, fragment-delivered, exchanged for an opaque HttpOnly handoff
   before OIDC, expiring, revocable, replay-safe, and explicitly accepted after
   an authenticated handoff preview confirms the actual account.
4. Every cookie mutation uses global same-origin/antiforgery protection; logout
   is POST and login return paths are local only.
5. Devices have stable member ownership; registration scope is resolved from
   its device as the single source of truth; operations snapshot it.
   Legacy/unassigned resources remain Owner-only.
6. Every API list/read/mutation implements the canonical endpoint matrix. A
   Family response never includes Apple/team/path/private-repository/raw-log or
   another member's private/resource data; the only cross-member projection is
   the approved household allowlist of display name, role, and coarse accepted-
   iPhone count.
7. Authorization is rechecked before queued/scheduled/retry external effects.
   Suspension immediately denies access and stops future scheduler work while
   running effects terminate/reconcile safely.
8. Migration is additive, retention bounds are explicit, corrupt state fails
   closed, backup restore rotates the session-security epoch and revokes pending
   authority, and rollback to owner-equivalent OIDC is blocked after Family
   admission until Family access is removed upstream.
9. Backend deterministic gates and independent security/semantic reviews pass
   with no Blocker or Major finding.

## Minimal implementation sequence

### 1. Durable workspace domain

Add one `WorkspaceAccess` area in `Sideport.Api` with records, validation, and a
single atomic JSON store. Reuse the current state-directory, process-lock, temp
file/atomic replace, clock injection, cryptographic RNG/SHA-256, constant-time
comparison, and GitHub single-use-state patterns. Do not add a database,
repository interface, token service, event bus, or dependency.

Gate: store tests cover empty/corrupt/future schema, exact issuer+subject
uniqueness, concurrent Owner claim, invitation create/replay/expiry/revoke/
parallel accept, tombstone restore rules, expected versions, idempotency, and
audit redaction.

### 2. One principal and browser-security boundary

Resolve a request actor once as recovery bearer, active Owner/Family, unknown
OIDC, disabled OIDC, or unverified. Read the issuer from validated OIDC token
context/configuration, never a request header or fallback string. Replace the
global any-OIDC authorization shortcut. Generalize the existing antiforgery and
origin checks to every cookie-authenticated unsafe method; add no-store CSRF
issuance to `/api/me`; make logout POST; constrain login return paths.

Gate: full route inventory tests missing/changed issuer/subject, unknown and
disabled principal access, bearer recovery, forged forwarded headers, CSRF for
every mutation group, logout, and open redirects.

### 3. Workspace endpoints

Implement scoped `/api/me` and `/api/workspace`, bearer-minted Owner claim,
explicit claim revoke/replacement, token-to-opaque handoffs, authenticated
post-login handoff previews, invitation create/revoke/explicit accept, Family
suspend/restore/offboard, after-restore epoch rotation, and owner-only audit
exactly as contracted. Generate share URLs only from the existing configured
`Sideport:PublicOrigin`. Never store or log raw link or recovery values.

Gate: HTTP integration tests assert status/error/DTO/no-store/rate-limit
semantics, one-time link disclosure, no raw token in browser storage/cookies,
handoff expiry, exact JSON-body exchange with pre-routing body-log suppression,
strict public-shell CSP/no-referrer headers, authenticated preview after a full
OIDC redirect, explicit-account acceptance, same-principal lost-response replay,
other-principal denial, claim revoke-before-replace, malicious OIDC presentation-
claim normalization/text rendering/log exclusion, replacement/offboarding
preflight staleness, concrete retention bounds, and no cross-member PII.

### 4. Expand stable resource ownership

Add nullable `ownerMemberId` to known-device records and resource-owner/actor
snapshots to operation JSON while retaining legacy fields for one compatibility
release. Do not persist a second registration owner: authorization resolves the
registration's existing device UDID through the known-device store. Enrollment
assigns the authenticated Family member or Owner-selected active target. Family
self-service stops after its first active accepted iPhone; additional devices
require the Owner path. Every operation snapshots actor and resolved device
owner. Never infer from free-text
owner, display name, email, actor hash, Apple ID, or Team ID.

Gate: legacy null-owner records remain Owner-only; enrollment races cannot steal
an accepted phone; the first release exposes no ownership reassignment; device
and operation serialization remains backward readable.

### 5. Enforce the endpoint and projection matrix

Centralize capability plus resource-scope checks, then apply them to every
route group from `Program.cs`. Owner/recovery sees all. Family sees only safe V2
catalog plus distinct allowlisted owned device/registration/operation-preflight/
operation/renewal/diagnostic DTOs; never serialize an Owner DTO and redact it
afterward. Legacy catalog/registration creation, Apple, GitHub, raw devices,
system, scheduler policy, onboarding, diagnostics triage, and logs remain
Owner-only. Authorize before lookup/idempotency/existence disclosure;
out-of-scope IDs are 404.

Gate: table-driven Owner/Family/unknown/disabled/recovery tests cover every
mapped route; cross-member list/search/count/find/action responses reveal
nothing; Family DTO snapshots contain no forbidden fields.

### 6. Execution-time revocation and redaction

Before worker, scheduler, retry/rerun, enrollment acceptance, signing, install,
refresh, or reconciliation performs an external effect, reload membership and
resource ownership. Scheduler skips disabled owners. Suspension commits first,
then requests only safe queued cancellation. Apply one sanitizer before the
in-process log ring retains formatted text; Family Activity remains operation
projection only.

Gate: suspend-after-queue and ownership-change races cause no new external call;
in-flight work follows the existing safe terminal/unknown boundary; restart
reconciliation is idempotent; captured API/audit/operation/log output passes
secret/PII/path/identifier redaction tests.

### 7. Migration, deterministic gates, and semantic review

Document required recovery proof, the pre-release state backup,
bootstrap-required state, legacy unassigned visibility, one-replica
requirement, family-aware rollback, and closed-ingress restore with
security-epoch rotation/pending-authority revocation. Run the focused tests,
full solution build/test,
`gates/backend-checks.sh`, secret scan, IaC plan/policy only, and independent
Copilot/GPT, Claude-family, and Codex security reviews. No infrastructure apply,
image publication, homelab change, or Authentik mutation occurs in Phase 9.

## Expected file ownership

- `src/Sideport.Api/WorkspaceAccess/*`: store, contracts, authorization, rate
  limiting, and redacted audit.
- `src/Sideport.Api/Program.cs`: centralized principal/CSRF boundary and route
  enforcement; remove hard-coded Owner/Operator/Viewer projection.
- `src/Sideport.Api/DeviceInventory/*`: stable owner expansion and enrollment
  assignment.
- Existing app-registration shape stays the source of device UDID only; no
  parallel member-owner field is added.
- `src/Sideport.Api/Operations/*`: actor/resource-owner snapshots, scoped
  queries/actions, and execution-time recheck.
- `src/Sideport.Api/Diagnostics/*`: centralized log sanitizer and scoped
  diagnostic projection.
- `tests/Sideport.Api.Tests/*` and affected orchestrator tests: contract,
  authorization, migration, concurrency, CSRF, redaction, and TOCTOU coverage.
- `docs/sideport-backend-contract.md` and ADR 0002: mark only actually live
  behavior after gates pass.

## Explicit non-goals

No Authentik blueprint/API mutation, passkey flow, official Sign in with Apple,
CalDAV, SMTP, second Owner, local credential system, per-member signer, UI shell
replacement, new device transport, Docker/Apple Container work, deployment, or
live infrastructure mutation belongs to Phase 9.

## Rollback

Before implementation/release, back up the Sideport state volume. The schema
expansion keeps legacy fields and does not delete old data. Before any Family is
admitted, rollback can restore the prior image while retaining state. After a
Family is admitted, first restrict Authentik/ingress to the Owner, drain and
reconcile operations, then roll back; otherwise the old binary grants Family
principals Owner authority. Never delete state, signer, pairing, IPA, or
anisette volumes as rollback.
