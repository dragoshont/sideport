You are an independent adversarial reviewer for Sideport Phase 11 only.

Review the current repo state against `AGENTS.md`, `gates/rubric.md`,
`architrave.config.json`, the Phase 11 intake/options/gates artifacts, the
canonical phase ledger, the UI design/data contracts, and the actual diff.

Acceptance criteria:
1. Explicit incomplete first run renders setup outside the shell; completed or
   absent onboarding status renders Home without a permanent Onboarding route.
2. Runtime navigation and command menu expose exactly Home, Apps, Devices,
   People, Activity, Settings; detail/install pages remain contextual.
3. Apps, Devices, Activity, People, and Settings truthfully consolidate the
   approved jobs without inventing backend capability.
4. Authorization visibility uses server capabilities. A Member with
   `devices.enroll` can add an iPhone, but app import and Apple signer controls
   remain absent without their Owner capabilities.
5. Legacy routes, fictional roles, rejected prototype, unreachable onboarding
   and Teams UI, stale stories, and their dead CSS are removed.
6. Existing add/install polling, recovery, preflight, idempotency, and read-only
   behavior are preserved.
7. Mobile layout is usable at 390 px, has the six-item bottom navigation, no
   horizontal overflow, and retains keyboard/a11y behavior.
8. No deployment, infrastructure apply, Authentik mutation, secret read, commit,
   or push occurred.

Deterministic evidence: UI build PASS; Storybook 84/84; Playwright 14/14;
Architrave checks PASS; reconcile PASS by transparent not-configured skip;
backend build zero warnings/errors; API 468/468, Orchestrator 53/53, Developer
API 98/98, Devices 64/64, GrandSlam 50/50; Kubernetes 6/6 valid; secret scan and
`git diff --check` PASS.

Return the full rubric output and `VERDICT: PASS | REVISE | FAIL`. PASS requires
zero Blockers and zero Majors. Do not edit files.
