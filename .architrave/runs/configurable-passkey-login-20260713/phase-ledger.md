# Phase ledger

| Phase | Name | Status | Scope | Gate | Result |
|---:|---|---|---|---|---|
| 1 | Configurable sign-in and passkey invitation | completed | Contract, API, invitation UI, docs, tests | deterministic + semantic | PASS |
| 2 | Release and production verification | in-progress | commit, merge, image, GitOps, runtime invitation | CI + runtime evidence | pending |

## Phase 1 — Configurable sign-in and passkey invitation

Status: completed

Scope:

- configurable OIDC presentation metadata;
- Authentik enrollment return to `/invite`;
- invitation handoff primary and fallback actions;
- contract and automated tests.

Out of scope:

- generic non-Authentik enrollment provisioning;
- direct WebAuthn inside Sideport;
- production deployment before repository gates are green.

Gate:

- focused tests, `gates/checks.sh`, `gates/backend-checks.sh`,
  `gates/reconcile.sh`, diff validation, and semantic review pass.

Result: PASS. All deterministic gates passed and the independent Copilot
Adversarial Judge returned PASS with no blockers or concerns.

## Phase 2 — Release and production verification

Status: in-progress

Scope:

- commit/push/merge, immutable release image, GitOps deployment, and a physical
  invitation-flow verification.

Gate:

- production authentication options are correct and a fresh invitation reaches
  passkey enrollment, returns to `/invite`, and requires explicit acceptance.
