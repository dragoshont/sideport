# Phase 11 Deterministic Evidence

- `npm --prefix src/Sideport.Admin run build`: PASS. Production bundle built;
  the pre-existing Vite chunk-size advisory remains non-blocking.
- `npm --prefix src/Sideport.Admin run test:ci`: PASS. ESLint and 84/84
  Storybook interaction/accessibility tests passed.
- `npm --prefix src/Sideport.Admin run test:screens`: PASS. 14/14 desktop and
  mobile runtime checks passed for Home, Apps, Devices, People, Activity,
  Settings, and read-only mutation behavior.
- `./gates/checks.sh`: PASS.
- `./gates/reconcile.sh`: PASS by transparent not-configured skip; this repo has
  no `tokens`/`tokenBuild` entry.
- `./gates/backend-checks.sh`: PASS. Build: zero warnings/errors. Tests:
  API 468/468, Orchestrator 53/53, Developer API 98/98, Devices 64/64,
  GrandSlam 50/50. Kubernetes plan/policy: 6/6 valid. Secret scan: PASS.
- `git diff --check`: PASS.
- Browser/visual evidence: the 390 px Member runtime has exactly Home, Apps,
  Devices, People, Activity, Settings; no permanent Onboarding; Add iPhone is
  present; Add app and Apple signer headings are absent; no horizontal overflow
  in the Playwright captures. Runtime console had no current-story errors.

## Deletion evidence

- Deleted rejected first-run prototype TSX/story files.
- Deleted the unreachable legacy `OnboardingPage` and old `TeamsPage` plus
  their private helpers and CSS.
- Removed four stale Storybook aliases that represented Apple Access, Teams,
  Users, and Settings as separate/overlapping destinations.
- Runtime `RouteId` contains six destinations plus contextual `device-detail`
  and `install-app` only.

## Safety boundary

No deployment, infrastructure apply, Authentik mutation, secret
materialization/read, commit, or push occurred.
