# Phase 9 implementation map

Status: `in-progress`

## Implemented slices awaiting the full phase gate

- Durable `workspace-access.json` store with exact OIDC identity keys, one
  active Owner, hashed claims/invitations/handoffs, idempotency, receipts,
  bounded audit, security epoch rotation, and fail-closed corruption handling.
- Nullable stable `ownerMemberId` on accepted devices plus immutable
  `actorMemberId` / `ownerMemberId` operation snapshots. Legacy/unassigned
  records remain Owner-only and are never inferred from free text.
- Typed request-principal resolution in bearer, Owner, Family, bootstrap,
  unknown, suspended, offboarded, stale-epoch, and unavailable-store states.
  The OIDC issuer comes only from the validated security token and is copied to
  an internal cookie claim; request `iss` and configured fallbacks are ignored.
- Explicit current-route policy inventory with default deny and global exact
  Origin plus ASP.NET antiforgery enforcement for cookie mutations. Public
  token-to-handoff exchanges remain HTTPS/loopback-development, JSON-only,
  bounded, rate-limited, no-store exceptions that grant no membership.
- Owner bootstrap/recovery, Family invitation, member status/offboarding,
  after-restore, audit, `/api/me`, and enforced `/api/workspace` HTTP surfaces.
- Persistent Data Protection keys for OIDC cookies even when the Apple
  credential source is read-only; workspace security epoch invalidates stale
  or restored local sessions.
- Private-link shell entry hardening: fragment-safe login returns, POST-only
  protected logout, strict shell headers, and narrowly public existing static
  assets so the anonymous shell can hydrate.
- Admin API client obtains `/api/me` antiforgery state and attaches it to every
  unsafe same-origin cookie request; bearer requests remain CSRF-free.

## Active integration work

- Family resource filtering and allowlisted projections for devices,
  registrations, catalog, operations, renewals, and diagnostics.
- Execution-time membership and ownership rechecks before queued Apple/device
  effects, scheduler refreshes, retries, reruns, and enrollment acceptance.
- End-to-end HTTP tests for bootstrap, invitation replay/expiry/revocation,
  CSRF, privilege boundaries, cross-member isolation, suspension, offboarding,
  audit, restore epoch, and corrupt state.

## Gate still required before Phase 10

- Zero-warning backend build and complete solution tests.
- Full admin build/tests plus browser private-shell and CSRF checks.
- Architrave backend checks, plan-only Kubernetes render/policy, reconciliation,
  and diff/secrets checks.
- Independent security/semantic review with all Blockers/Majors repaired and a
  final PASS recorded in the canonical ledger.

No invitation or Family principal may be enabled in a deployment until this
gate is recorded. No image publication, GitOps mutation, or homelab rollout is
part of Phase 9.
