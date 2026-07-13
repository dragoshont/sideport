You are the independent adversarial judge for completed Sideport Phase 12.
Review actual current repo state and diff against AGENTS.md, gates/rubric.md,
phase12-intake.md, phase12-options.md, phase-ledger.md,
docs/sideport-backend-contract.md, UI design/data contracts, and phase12-gates.md.

Acceptance criteria:
1. Only the active Owner over the existing HTTPS/origin/CSRF boundary can
   authenticate candidates, preflight, or cut over signing authority.
2. Candidate different-account passwords/2FA remain memory-only, actor-bound,
   short-lived, sanitized, and do not overwrite the working credential.
3. Only teams from the current authenticated Apple response are selectable.
4. Exact certificate IDs, inventory version, impact codes, actor, target and
   idempotency key are bound; no revoke-all or loose boolean acknowledgement.
5. Install/refresh and cutover share one authority gate; identity replacement
   uses the existing provider lock.
6. Target identity is verified before team/account/registration finalization.
7. Same-account returned-team changes atomically rebind exact registrations.
8. Different-account cutover uses one durable journal; startup rolls back when
   the old credential is active and completes when the replacement credential
   is active. Passwords never enter journal/operation/logs.
9. Crash after identity persistence resumes without repeat revocation/mint;
   candidate loss before credential swap requires reauthentication.
10. Runtime UI clears passwords immediately, handles 2FA, lists only returned
    teams, shows exact impact, and hides all signer controls from Members.
11. No multi-signer/per-member signer/arbitrary Team ID/deployment/apply.

Deterministic evidence: Storybook 85/85; Playwright 14/14; API 480/480;
Orchestrator 54/54; Developer API 101/101; Devices 64/64; GrandSlam 50/50;
backend build zero warnings/errors; Kubernetes 6/6; secret scan and diff PASS.

Return the full rubric format and explicit VERDICT. PASS requires zero Blockers
and zero Majors. Do not edit files.
