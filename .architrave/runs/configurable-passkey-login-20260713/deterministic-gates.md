# Deterministic gates

Date: 2026-07-13

- Focused API: PASS — 99 tests.
- Storybook interactions: PASS — 88 tests, including two invitation handoff
  states.
- `gates/checks.sh`: PASS — production UI build, lint, and Storybook suite.
- `gates/backend-checks.sh`: PASS — zero-warning build; 55 orchestrator, 50
  GrandSlam, 65 device, 102 Developer API, and 481 API tests; Kubernetes render
  and kubeconform policy checks; deploy secret scan.
- `gates/reconcile.sh`: PASS/SKIP — this repository has not configured token
  reconciliation yet.
- `npm --prefix src/Sideport.Admin run test:screens`: PASS — 14 desktop/mobile
  screenshot checks. The harness logged expected local API proxy connection
  failures because the screenshot fixtures intentionally render the unavailable
  runtime state without a backend.
- `git diff --check`: PASS.
- `gitleaks detect --no-git --redact`: PASS — no leaks found.

Non-blocking notice: the repository's copied Architrave kit is version 0.8.1
while the installed plugin is 0.10.3. Updating the delivery kit is outside this
feature and was not mixed into the release.
