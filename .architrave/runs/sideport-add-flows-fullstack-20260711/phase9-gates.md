# Phase 9 Gate Evidence

Date: 2026-07-12
Scope: server-enforced workspace authorization only

## Implemented

- Durable workspace members, invitations, Owner claims, handoffs, receipts,
  idempotency, and allowlisted audit evidence.
- Exact OIDC issuer + subject membership; unknown, suspended, and offboarded
  principals fail closed.
- Owner/Family capability and resource-owner enforcement across HTTP reads,
  mutations, queued operations, retries, scheduler work, Apple authority, and
  GitHub setup callbacks.
- Cookie mutation CSRF + exact-Origin enforcement and public-shell security
  policy.
- Canonical fixed-layout authority tokens; Base64 aliases/padding rejected;
  stable client/authority/actor rate limiting; bounded authority, handoff,
  idempotency, and audit histories.
- Recovery and offboarding exact replay before new impact work. New mutations
  verify exact impact inside the workspace-store mutation gate and persist the
  server-verified counts/version.
- Expired invitation PII tombstoning, handoff expiry/purge enforcement, null and
  inconsistent persisted-graph rejection, and structured fail-closed 503s.
- Lost-response HTTP replay for offboarding and Owner recovery.

## Deterministic evidence

- `./gates/backend-checks.sh`: PASS.
- Solution build: zero warnings/errors.
- Tests:
  - Sideport.Api.Tests: 461/461.
  - Sideport.Orchestrator.Tests: 53/53.
  - Sideport.DeveloperApi.Tests: 98/98.
  - Sideport.Devices.Tests: 64/64.
  - Sideport.GrandSlam.Tests: 50/50.
- Kubernetes plan/policy: 6/6 resources valid; no apply.
- Deploy secret scan: PASS.
- `git diff --check`: PASS.

## Semantic evidence

- Independent GPT/Copilot Architrave adversarial judge: PASS, zero Blockers and
  zero Majors. It suggested explicit Owner-recovery HTTP replay and accepted
  handoff-retention regressions; both were added and passed before the final
  deterministic gate.
- Native Claude launcher failed before repository inspection with local
  `No connected db`. The established read-only fallback used Copilot CLI locked
  to Claude Sonnet 4.5. Initial review reported a Minor expired-invitation PII
  concern based on stale line interpretation; focused re-review inspected
  `PruneRetention`, `ExpireInvitationAsync`, `ToInvitationTombstone`, and the
  regression assertion and returned PASS with no remaining findings.

## Boundary

No Authentik mutation, runtime-shell binding, physical-device action, image
publication, deployment, infrastructure apply, secret read, commit, or push was
performed in Phase 9.
