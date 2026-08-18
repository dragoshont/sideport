# Deterministic Gates

Date: 2026-07-14

- Focused device-enrollment tests: 23/23 PASS.
- Sideport device tests, including exact usbmux framing: 80/80 PASS.
- Full API tests: 528/528 PASS.
- Other .NET suites: 287/287 PASS.
- Admin lint + Storybook interaction/accessibility: 108/108 PASS.
- Canonical simplified product stories: 26/26 PASS.
- Admin production build: PASS.
- Desktop/mobile runtime screen suite: 14/14 PASS.
- `gates/checks.sh`: PASS.
- `gates/reconcile.sh`: PASS with the repository's documented token-build skip.
- `gates/backend-checks.sh`: PASS.
  - backend build: 0 warnings, 0 errors;
  - Kubernetes render/policy: 6 valid, 0 invalid/errors;
  - deployment secret scan: PASS.
- `git diff --check`: PASS.

Focused behavior evidence covers:

- explicit `sideport|host` pairing ownership configuration;
- host-owned mode never invokes the backend Pair request;
- enumeration-only presence discovery does not open lockdown;
- enrollment candidate selection and recovery use transport enumeration before
  one explicit Trust probe;
- typed denial, awaiting-Trust, host-managed, USB-required, transport, and
  repair-required outcomes;
- damaged saved Trust becomes non-retryable owner repair rather than a blind
  pairing loop;
- partial usbmux stream reads and writes are accumulated exactly;
- EOF during a frame fails explicitly instead of returning zero-filled data;
- four primary Storybook destinations plus secondary Activity and Settings;
- actionable Home, Your apps/Browse, owner sources, and drillable app/device/
  person rows and detail surfaces;
- exact attention-row navigation from Home/Activity to Sam's device detail;
- 390px visual QA and correction of the wrapped mobile account control.
