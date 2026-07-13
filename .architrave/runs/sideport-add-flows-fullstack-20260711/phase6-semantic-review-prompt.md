# Phase 6 Adversarial Semantic Review

Review the current `codex/apple-like-add-flows` working tree as an independent
Architrave Adversarial Judge. Do not edit files or run mutations.

Use these sources of truth:

- `AGENTS.md`, `architrave.config.json`, and `gates/rubric.md`
- `.architrave/runs/sideport-add-flows-fullstack-20260711/{intake.md,tournament.md,recommended-plan.md,phase-ledger.md,phase6-gates.md}`
- `docs/sideport-backend-contract.md`
- `docs/ui/sideport-ui-{design-spec,data-contract}.md`
- `docs/ui/sideport-onboarding-implementation-plan.md`
- relevant current production and test files under `src/Sideport.Admin`,
  `src/Sideport.Api`, `src/Sideport.Orchestrator`, and `tests`
- `knowledge/web.md`, `knowledge/backend.md`, `knowledge/operations-ux.md`,
  and `knowledge/yagni.md`

Judge Phase 6 only. Phases 7–11 are explicitly not-started. Do not fail Phase 6
for the deferred navigation/family redesign, homelab release, Apple container
launcher, or unavailable physical-device acceptance, provided capability truth
and the deferrals are explicit.

Derive and check at least these acceptance criteria:

1. A fresh authenticated owner can enter the first Apple credential in the UI,
   complete 2FA, and choose only a team returned by Apple; secrets are cleared
   from browser state and never returned or logged.
2. Explicit Add iPhone waits for USB/Trust and accepts automatically after
   verified trust. Ordinary reads do not pair. Paired Wi-Fi remains supported
   truthfully, with USB presented as the reliable fallback.
3. The chosen catalog artifact/account/team/device is durably bound before the
   first install. A registration remains pending until exact on-device
   verification and cannot enter unattended refresh early.
4. The one install action drives preflight, queued operation, polling, device
   verification, registration activation, scheduler enablement, and immutable
   onboarding receipt. Reload/restart resumes without repeating external
   effects.
5. Unknown transfers reconcile through a linked child; active transfers are
   rejected, mismatched evidence remains blocked, and retryable finalization is
   distinguishable from terminal lineage failure.
6. Onboarding completion is gated by live `onboarding.complete`; automatic
   refresh is a smart default backed by live scheduler settings/status rather
   than fixed copy.
7. Signed-in users can add another iPhone or app from the shared Add flows;
   runtime empty/error/pending/reload states are honest and accessible.
8. Authentication actor identity, idempotency, scarce three-app limits,
   recovery, and secret/IaC safety match the backend contract.
9. Deterministic evidence is green and no deployment/apply/commit/push or
   Phase 7 implementation occurred.

Return the exact rubric structure: acceptance-criteria checklist, dimension
table with cited evidence and required fixes, blockers, concerns, uncovered
specs, then `VERDICT: PASS | REVISE | FAIL`. PASS requires zero Blockers/Majors
and green deterministic gates. Mention non-blocking warnings separately.
