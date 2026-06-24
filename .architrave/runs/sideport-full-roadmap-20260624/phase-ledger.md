# Phase Ledger

| Phase | Name | Status | Scope | Gate | Result |
|---:|---|---|---|---|---|
| 0 | Roadmap Re-Grounding | completed | Re-read Sideport config, SDD plan, contract, UI instructions, code seams, working tree, and specialist reviews. | Grounding sources captured | Completed. |
| 1 | Full Roadmap Contract | completed | ADR and canonical contract for known devices, upload/import, workspace capabilities, operations worker, diagnostics. | Judge PASS on contract | Completed after one revision; judge PASS. |
| 2 | Known Devices Backend | completed | Durable known-device JSON store, endpoints, reachable merge, tests. | API tests + backend-checks + judge | Completed after one revision; judge PASS. |
| 3 | Upload Import Backend | completed | Multipart IPA upload/import into durable catalog, validation/conflict tests. | API tests + backend-checks + judge | Completed after one revision; judge PASS. |
| 4 | Workspace Capability Backend | completed | Live read contract for current member, roles, capabilities, delegated limits. | API tests + backend-checks + judge | Completed; judge PASS with minor close-outs applied. |
| 5 | Background Operations Backend | completed | Queued refresh worker, cancel queued, retry/rerun new operations, scheduler enqueue. | API tests + backend-checks + judge | Completed after one revision; judge PASS. |
| 6 | Durable Diagnostics Backend | completed | Grouped issue store from real evidence, issue endpoints, triage transitions. | API tests + backend-checks + judge | Completed after one revision; judge PASS. |
| 7 | Admin UI Bindings | completed | Bind current admin surfaces plus Operations page to backend contracts. | UI build/lint/screens + judge | Completed after three revisions; judge PASS. |
| 8 | Merge/Deploy Readiness | completed | Docs, IaC plan-only evidence, final gates, run validation, merge/deploy handoff. | full gates + artifact validation | Completed; all gates pass, no live apply performed. |

## Phase Transition Log

- Starting Phase 0 - Roadmap Re-Grounding: scope was evidence gathering and phase
	enumeration. Out of scope: implementation.
- Completed Phase 0 - Roadmap Re-Grounding: remaining phases confirmed from
	`docs/sideport-sdd-implementation-plan.md` and specialist reviews.
- Starting Phase 1 - Full Roadmap Contract: scope is contract/ADR only. Out of
	scope: backend/UI implementation until judge passes the contract.
- Phase 1 judge returned REVISE for underspecified mutation/state contracts and
	too-broad implementation checkpointing. Contract updated with request/response,
	status-code, idempotency, state transition, auth-scope, and per-slice gates.
- Completed Phase 1 - Full Roadmap Contract: judge PASS after one revision;
	deterministic gates recorded.
- Starting Phase 2 - Known Devices Backend: scope is durable known-device JSON
	store, GET/POST/PATCH/DELETE endpoints, reachable merge, corrupt-store and
	registration-conflict tests. Out of scope: MDM actions, pairing management,
	ownership workflows beyond owner/notes metadata, and UI bindings.
- Phase 2 judge returned REVISE for durable last-seen/current-poll semantics and
	save-failure rollback. Fixed with overlay semantics, rollback, and tests.
- Completed Phase 2 - Known Devices Backend: backend-checks PASS, judge PASS.
- Starting Phase 3 - Upload Import Backend: scope is multipart IPA upload/import
	into durable catalog storage, size/content/conflict/replace validation, and API
	tests. Out of scope: UI dialog, install/sign side effects, resumable upload,
	malware scanning, and version-history management.
- Phase 3 judge returned REVISE for request/form limits and replace rollback.
	Fixed with Kestrel/FormOptions limits, durable IPA backup/restore, and tests.
- Completed Phase 3 - Upload Import Backend: backend-checks PASS, judge PASS.
- Starting Phase 4 - Workspace Capability Backend: scope is `GET /api/workspace`
	live read contract for current principal, roles, capabilities, advisory role
	enforcement, and delegated-auth limits. Out of scope: invitations, local users,
	passwords, token management, owner transfer, offboarding mutation, and UI.
- Completed Phase 4 - Workspace Capability Backend: backend-checks PASS, judge
	PASS. Applied minor close-outs: `/api/workspace` moved to live endpoint table,
	retry/rerun capability assertions added, artifacts synced.
- Starting Phase 5 - Background Operations Backend: scope is in-process queued
	refresh worker, queued-only cancel, retry/rerun as new operations after fresh
	preflight, and scheduler enqueue path. Out of scope: in-flight cancellation,
	distributed queue/locks, multi-replica execution, and UI bindings.
- Phase 5 judge returned REVISE for scheduler bypass and cancel atomicity. Fixed
	with store transitions, API operation scheduler, and tests.
- Completed Phase 5 - Background Operations Backend: backend-checks PASS, judge
	PASS.
- Starting Phase 6 - Durable Diagnostics Backend: scope is grouped issue store
	from operation/readiness/device/log evidence, issue endpoints, and triage
	transitions. Out of scope: fake OpenTelemetry trace links, artifact downloads,
	UI bindings, and external log storage.
- Phase 6 judge returned REVISE for unredacted evidence. Fixed with diagnostics
	redaction/capping and tests for redaction plus reopen behavior.
- Completed Phase 6 - Durable Diagnostics Backend: backend-checks PASS, judge
	PASS.
- Starting Phase 7 - Admin UI Bindings: scope is existing admin surface bindings
	for known devices, upload/import, workspace capabilities, operation actions,
	durable diagnostics, and one Operations page. Out of scope: new visual system,
	MDM actions, local user CRUD, fake traces, and live deployment.
- Phase 7 judge returned three REVISE loops for workspace mapping and known-device
	provenance edge cases. Fixed extended workspace mapping, durable/current-poll
	split, offline/unknown connection mapping, and no-evidence last-seen display.
- Completed Phase 7 - Admin UI Bindings: checks PASS, screens PASS 20/20, judge
	PASS.
- Starting Phase 8 - Merge/Deploy Readiness: scope is final deterministic gates,
	run artifact validation, diff hygiene, status summary, and safe merge/deploy
	handoff. Out of scope: live Kubernetes apply, Flux reconcile, secret access, or
	forced git operations.
- Completed Phase 8 - Merge/Deploy Readiness: final `gates/checks.sh`,
  `gates/backend-checks.sh`, screen tests, editor diagnostics, k8s plan/policy,
  deploy secret scan, run validation, and diff hygiene pass. No live apply,
  restart, reconcile, or secret access was performed.
