# Phase 11 Intake

## Understanding

Replace the rejected operator console in the live React runtime with the
approved mobile-first multi-user Sideport shell. Setup exists only while the
server explicitly reports incomplete first run. After completion, the product
has exactly `Home`, `Apps`, `Devices`, `People`, `Activity`, and `Settings`.

## Acceptance criteria

1. Completed setup lands on Home and never exposes a permanent Onboarding
   destination.
2. The runtime navigation and command menu expose exactly the six approved
   destinations; device detail and install are transient contextual pages.
3. Apps remains the searchable shared library and existing live install/add
   flows remain bound.
4. Devices truthfully describes continuous USB monitoring and can reopen the
   existing add-iPhone assistant.
5. Activity consolidates operations, diagnostic issues, and workspace history.
6. Settings consolidates sign-in/recovery, automatic refresh/system posture,
   and Owner-only Apple signing.
7. Public roles are Owner and Member. Members may add an iPhone when granted
   `devices.enroll`, but cannot import apps or see signer controls without the
   corresponding capabilities.
8. Legacy routes, fictional roles, rejected prototype, unreachable onboarding
   and Teams UI, stale stories, and associated dead CSS are deleted.
9. Desktop/mobile/keyboard/accessibility checks, Architrave UI/backend gates,
   reconciliation, and semantic review pass.

## Grounding sources

- `AGENTS.md`
- `architrave.config.json`
- `docs/ui/sideport-ui-design-spec.md`
- `docs/ui/sideport-ui-data-contract.md`
- `docs/sideport-backend-contract.md`
- Phase 7 canonical Storybook shell and Phase 8–10 security contracts
- `.architrave/runs/sideport-add-flows-fullstack-20260711/phase-ledger.md`

## Assumptions and boundaries

- Existing backend contracts and Phase 6 add/install flows are reused.
- No device transport change, signer replacement architecture, deployment,
  Authentik apply, Kubernetes apply, secret read, commit, or push is in scope.
- Missing onboarding status must not force a signed-in user into setup; only an
  explicit incomplete server state does.

## Blocking questions

None. The owner approved autonomous implementation of Phases 10 and 11.
