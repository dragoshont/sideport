# Phase 8 Family Security Threat Model

Date: 2026-07-12
Status: accepted Phase 8 contract; runtime implementation begins in Phase 9

## Protected assets

- Owner Apple credential, active team, signing identity, certificates, profiles,
  and scarce free-account resources.
- Family membership and the mapping from a human identity to owned iPhones.
- Pairing records, UDIDs, installed-app/registration state, IPA paths and bytes.
- Private GitHub repository identity/credentials and managed catalog artifacts.
- Operation, diagnostic, and security-audit evidence.
- Invitation and recovery bearer secrets.

## Trust boundaries

1. Browser ↔ Authentik: Authentik owns passkey/login/recovery and returns a
   validated OIDC session. Sideport must not infer identity from headers or
   display claims.
2. Reverse proxy ↔ Sideport: forwarded scheme/host/client data is trusted only
   from configured proxy addresses/networks. The public Sideport URL is
   configured, not request-host derived.
3. Browser/API ↔ authorization store: every request resolves a durable member
   and capability/resource scope; a cookie is not authority by itself.
4. API ↔ queued worker/scheduler: authority can change after submission, so it
   is checked again immediately before external Apple/device effects.
5. Sideport ↔ physical iPhone/usbmux: cable presence and lockdown trust prove
   device reachability, not human ownership. The authenticated enrollment
   operation supplies the owner member.
6. Sideport ↔ Apple/GitHub: external credentials and raw errors never enter
   member-facing projections, operations, audit, or logs.

## Actors considered

- Unauthenticated network client.
- Authenticated Authentik principal with no Sideport membership.
- Active Family member attempting horizontal or vertical privilege escalation.
- Suspended/offboarded member with a still-valid OIDC cookie.
- Person who obtained an invitation link.
- Browser/XSS or history/referrer observer.
- Holder or thief of the recovery bearer.
- Owner making a race, retry, rollback, or destructive offboarding mistake.
- Corrupt state file or second process racing a single-replica store.

## Current critical exposure

The following evidence is from the dirty branch and blocks Family admission:

- Any authenticated OIDC session authorizes every `/api/*` request; there is no
  member lookup (`src/Sideport.Api/Program.cs:515-523`).
- A missing issuer falls back to `configured-oidc`, and workspace identity uses
  the subject directly (`Program.cs:519-522`, `Program.cs:2910-2916`).
- `/api/workspace` makes every OIDC principal an active Owner with mutation
  capabilities (`Program.cs:2910-2966`).
- Device `Owner` is mutable free text, and registration/operation records have
  no durable member owner (`KnownDeviceContracts.cs:28-34`,
  `Sideport.Orchestrator/AppRegistration.cs:10-23`,
  `Operations/OperationContracts.cs:157-181`).
- CSRF protection is limited to selected Apple credential endpoints; logout is
  a state-changing GET (`Program.cs:561-655`, `Program.cs:726-732`).
- Legacy app/catalog DTOs expose Apple ID, Team ID, or host IPA paths, and raw
  logs accept unredacted formatted exception messages (`Program.cs:1955-1977`,
  `Catalog/AppCatalog.cs:37`, `Diagnostics/OperationLogStore.cs:29`).

## Threats, required controls, and verification

| Threat | Abuse/failure | Required control | Required verification |
| --- | --- | --- | --- |
| Arbitrary OIDC privilege escalation | Any Authentik user becomes Owner | Exact validated issuer+subject lookup; unknown principal allowlist limited to `/api/me`, opaque-cookie handoff preview, and handoff acceptance; no issuer fallback | Invalid/missing issuer/subject has no authenticated session and returns 401; valid unknown or same-subject/different-issuer returns 403 outside the exact preview/accept allowlist |
| First-login race | Attacker reaches empty workspace first | Recovery-bearer tooling mints a hashed short-lived Owner claim; empty store or OIDC login alone grants nothing | Concurrent mint/handoff/accept: only one valid claim commits the sole Owner |
| Recovery takeover | Stolen bearer replaces Owner | Bearer already has break-glass authority; keep it in Authorization header, require HTTPS/rate limits/exact impact/version, return one short-lived link, explicit revoke-before-replace and OIDC acceptance, audit | Wrong/replayed/stale/revoked claim, lost-response replacement, and replacement-preflight tests; bearer never enters browser/API response/log |
| Invite disclosure | Token appears in path/query/referrer/proxy/OIDC state, request-body telemetry, or remains JS-readable through login | 256-bit token in fragment, immediate in-memory bounded JSON POST/history stripping, endpoint-specific body-capture suppression before routing, independent hashed ten-minute handoff, opaque Secure/HttpOnly cookie, authenticated handoff-preview read after OIDC, explicit acceptance, strict self-only CSP/no analytics/no-store/no-referrer | Full-redirect browser/navigation/storage/cookie/preview tests; captured request, exception, metric, and trace logs contain no raw token/hash/body |
| Invite brute force/status oracle | Random token probes enumerate invites | Independent random invitation ID and 256-bit secret, constant-time hash comparison, client+invite rate limit, generic unknown/mismatch response | Malformed/unknown/mismatch equivalence and 429 tests |
| Invite replay/race | Two identities claim one link | Single locked atomic invitation-consume + member-create transaction; same-identity accepted-handoff replay only | Parallel acceptance leaves one member; same identity recovers a lost response; other identity gets unavailable |
| Suspended identity re-enrolls | New invite bypasses offboarding | Retained identity tombstone/status checked before acceptance; explicit Owner restore only | Suspended/offboarded acceptance denied without consuming another member's data |
| CSRF/session riding | Cross-site page invokes install, invite, offboard, signer change, or logout | Exact same-origin plus ASP.NET antiforgery on every cookie unsafe method; POST logout; no credential CORS | One CSRF-negative integration test for every mutation route group |
| Login/open redirect | Crafted return URL leaves Sideport | Normalize and allow only local `/` paths; reject scheme-relative, backslash, control/encoded external forms | Return-url table tests |
| Cross-member IDOR | Family guesses UDID, bundle, operation or member ID | Capability plus stable ownerMemberId; filter before pagination/count; 404 for unknown and out-of-scope | Cross-member list/find/action/search/count tests are indistinguishable from missing |
| Vertical signer/source escalation | Family calls Apple, upload, import, GitHub, scheduler or onboarding endpoints | Owner-only endpoint matrix; safe V2 catalog only; legacy path/Apple DTOs Owner-only | Family endpoint matrix tests return 403/404 and no sensitive fields |
| Scarce-resource exhaustion | Family claims many phones, causes certificate churn, or overrides quotas | Family self-service limited to first active iPhone; later phone is Owner-enrolled; approved catalog + preflight; established signer only; cutover/mint/replace/revoke and override block as owner-action-required | Second self-enrollment and every destructive/stale/scarce case block before pairing/Apple mutation; Owner path still preflights capacity |
| TOCTOU after suspension | Queued install runs after access removal | Persist suspension first; worker/scheduler/retry/final pre-effect boundary reloads member+owner; safe running work reconciles | Pause member after queueing; no not-yet-started external call occurs |
| Wrong phone claimed | Family claims another accepted/reachable phone | Family enrollment may claim only unassigned device; existing owner check and atomic assignment; Owner target explicit | Multi-device/accepted-device race and ownership assignment tests |
| Scheduler continues after offboarding | Owner resources keep refreshing disabled member's apps | Scheduler skips non-active owner; queued work canceled when safe; registrations/resources retained frozen | Scheduler and restart reconciliation tests |
| Audit/log secret or PII leak | Tokens, subject/email, Apple/GitHub/path data reaches Activity/logs | Allowlisted atomic audit fields; centralized log redaction; Family uses distinct allowlisted preflight/operation/renewal/diagnostic projections, never Owner DTO redaction or raw logs | Snapshot/regex tests over every Family DTO plus API, audit, operation and captured logs |
| Crafted identity display claim | Authentik name/email/provider text injects script, terminal controls, misleading bidi text, log lines, headers, paths, or future spreadsheet formulas | Treat every presentation claim as untrusted; normalize and bound it, reject control/newline/bidi override characters, validate email, serialize/render as text only, and exclude claims from logs/audit/headers/URLs/paths; no CSV export in Phase 9 | Malicious Unicode/HTML/newline/formula-prefix claim table proves fixed fallback or safe text rendering and zero log/header/path/audit propagation |
| Store corruption or race | Empty overwrite, duplicate Owner/member, lost audit | Versioned single atomic store, process lock, expected versions, fail-closed unknown/corrupt schema | Corruption, concurrent mutation, crash/reload and idempotency tests |
| Rollback authorization regression | Old binary makes admitted Family an Owner | After first Family admission, old binary rollback forbidden until Authentik/ingress restricts access to Owner; preserve state volumes | Release runbook and rollback rehearsal assert family access disabled first |
| Backup restore resurrects authority | Older state revives invitation or predates suspension while cookies survive | Restore with Family ingress closed; rotate workspace security epoch; revoke pending invites/claims/handoffs; invalidate local cookies; Owner reviews restored members before reopen | Restore rehearsal proves old cookie/handoff fails and pending authority is revoked |
| Proxy spoof / insecure cookie | Direct client forges HTTPS/host or OIDC callback | Known proxy/network allowlist, effective HTTPS, secure cookie, configured public URL, callback acceptance test | Direct forged-forwarded-header tests and deployed HTTPS callback proof |
| Capability overclaim | UI calls passkey “Apple login” or Wi-Fi/install success without evidence | Neutral Authentik sign-in copy; no CalDAV/GrandSlam identity; operation/device verification remains authoritative | Contract/UI content tests and physical acceptance gate |

## Membership and resource invariants

1. One exact OIDC identity maps to at most one retained member record.
2. Exactly one active human Owner exists after bootstrap; recovery replacement
   commits the new Owner and previous-owner suspension atomically.
3. Invitation role is always Family and acceptance never changes an existing
   suspended/offboarded identity.
4. Device ownership is a stable member ID. Registration ownership derives from
   the device; operation ownership is snapshotted for historical filtering.
5. Family may read the safe shared V2 catalog, its own resource projections, and
   the approved minimal household directory: another active member's display
   name, role, and coarse accepted-iPhone count only. It never receives another
   member's stable ID, email, status, last-active time, invitation, device ID,
   activity, Apple ID, Team ID, host path, private repository, or raw log data.
6. Authorization occurs before idempotency lookup or resource existence is
   disclosed, and again before a queued external effect.
7. Security mutations and their redacted audit event are one workspace-store
   transaction.

## Suspension/offboarding truth

Suspension immediately blocks the member and future scheduling. Safe queued
work is canceled; running Apple/device work is not blindly interrupted and
must terminate or reconcile under existing operation rules. Offboarding is a
soft retained state with an impact preflight and immutable receipt. It does not
uninstall apps, wipe a device, revoke passkeys/Authenik sessions, revoke Apple
credentials/certificates/profiles, or delete history. The Owner can later
restore the member or remove supported resources explicitly. Ownership
reassignment is not claimed in the first release.

## Migration and rollback threat boundary

- A missing store yields bootstrap-required, not an implicit Owner.
- Existing free-text owners, display-name actor hashes, registrations, and
  historical operations are never identity-matched. Their member owner is null
  and they are Owner-only.
- Corrupt/future state returns a structured 503 and is never replaced with an
  empty store.
- The new fields are additive for one compatibility window. The old binary may
  ignore them, which is why rollback becomes an authorization hazard rather
  than a data-format hazard.
- Before a family-aware release, back up the state volume. After a Family
  principal is admitted, rollback begins by removing Family OIDC reachability,
  then draining/reconciling work; it never deletes state, pairing, signer, IPA,
  or anisette volumes.
- Restoring an older workspace snapshot also occurs with Family access closed.
  Recovery rotates a random workspace security epoch, revokes every pending
  invitation/Owner claim/handoff, invalidates local Sideport cookie tickets,
  and requires the Owner to review/reapply member status before reopening.

## Residual accepted risks

- Anyone with both a live private invite and an Authentik account can claim it.
  This is the chosen low-friction authority model; private delivery, expiry,
  single use, revocation and audit bound it.
- A recovery-bearer holder can become Owner. The bearer already grants
  Owner-equivalent API access, so this does not add authority; it makes the
  break-glass path explicit and observable.
- The JSON store and process lock support one replica only. Multi-replica
  membership is not claimed.
- Sideport suspension cannot revoke an Authentik passkey/session. Per-request
  membership denial makes that session useless to Sideport; Authentik recovery
  and credential revocation remain separate provider actions.
