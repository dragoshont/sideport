# Deterministic Gates

Date: 2026-07-14

- Focused native passkey, browser-entry, and workspace policy tests: PASS,
  160/160 after GitHub review remediation. This slice explicitly covers fresh `/` → `/owner-claim`, exact
  origin, no-store status, direct-bootstrap rate limiting, lost-cookie retry,
  concurrent attempts, wire-level absence of raw bootstrap/recovery authority,
  System audit records, recovery-claim preservation, and active-workspace denial.
  The public bootstrap-status presentation is rate-limited before store access.
- Admin lint + Storybook interaction/accessibility suite: PASS, 102/102.
  Native invitation, native recovery-private-link, claimed workspace, direct
  Owner setup, generic OIDC enrollment/login, and OIDC-without-private-link
  states are explicit stories.
- Admin production build: PASS.
- Full .NET solution tests: PASS:
  - Sideport.Api.Tests 522/522 after GitHub review remediation
  - Sideport.DeveloperApi.Tests 102/102
  - Sideport.Devices.Tests 65/65
  - Sideport.Orchestrator.Tests 55/55
  - Sideport.GrandSlam.Tests 50/50
- `gates/checks.sh`: PASS.
- `gates/reconcile.sh`: PASS (tokens/tokenBuild not configured; documented skip).
- `gates/backend-checks.sh`: PASS.
  - Build: 0 warnings, 0 errors.
  - Kubernetes render: PASS.
  - kubeconform: 6 valid, 0 invalid/errors.
  - deployment secret scan: PASS.
- `git diff --check`: PASS.

## GitHub review remediation

CodeRabbit completed with two Minor findings and one Trivial maintainability
nit. All were accepted and fixed:

- escaped state separators in the endpoint contract markdown table;
- rate-limited the public native bootstrap status endpoint before store access;
- moved bootstrap serialization from a process-static semaphore to an
  application-scoped DI singleton, preserving the single-replica guarantee
  without coupling independent test hosts.

The expanded focused slice and the 102-story UI suite pass after remediation.
