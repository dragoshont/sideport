# Phase 12 Progress

## Completed slice

- Approved Storybook Owner signing flow: current account/team/identity,
  reauthentication, exact certificate/app/device/profile impact,
  acknowledgement, operation progress, verified result.
- Live Owner-only `signing-preflight` and `cutover` endpoints.
- Returned-team selection is preflighted rather than immediately saved.
- Exact certificate resource IDs only; no revoke-all API.
- Portal replacement and installs/refreshes share `SignerAuthorityGate`.
- Provider replacement uses its existing identity lock.
- Target identity is persisted and verified before atomic registration/team
  finalization.
- Durable `SigningCutoverIntentDto`, idempotent replay, recovery-required state,
  and persisted-identity recovery without repeat revocation/minting.
- File registry atomically rebinds only exact old account/team lineage.

## Green evidence

- Backend build: zero warnings/errors.
- API 470/470; Orchestrator 54/54; Developer API 101/101; Devices 64/64;
  GrandSlam 50/50.
- Storybook 85/85; runtime Playwright 14/14; `git diff --check` PASS.

## Review loop

The first independent review returned REVISE with two Blockers: the cutover
needed a process-wide authority gate spanning lineage finalization, and retry
after persisted identity could repeat replacement. Both were repaired. The
subsequent reviewer launcher read only file headers and emitted no verdict, so
it is not counted as a semantic gate.

## Remaining before Phase 12 can close

- Different-account candidate credential authentication and 2FA, held outside
  the active credential until exact cutover succeeds.
- Atomic active credential replacement plus registration/account/team lineage
  migration, with rollback/recovery tests.
- Focused HTTP authorization/CSRF/transport tests for both new endpoints.
- Final deterministic gates and a valid independent PASS.

No deployment, apply, secret read/materialization, commit, or push occurred.

## Different-account implementation update — 2026-07-13

- Added isolated, actor-bound replacement candidates with single-use 2FA and returned-team selection.
- Added an Apple authority cutover journal/coordinator. Registrations and account state are staged before the encrypted credential becomes active; failures while the old credential remains active roll lineage back, and startup recovery converges or fails closed.
- Bound the candidate to the preflight and consume it when cutover begins so it cannot authorize a second operation. Different-account recovery-required operations are not automatically re-run without a new reviewed candidate.
- Corrected the registry account-profile hash width to match the canonical Apple account profile ID.
- Focused candidate/2FA/coordinator tests pass; final full gates and independent review are pending.
