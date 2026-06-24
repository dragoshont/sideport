# Recommended Plan

## Summary

Finish Sideport's remaining roadmap as minimal, contract-honest foundations on
the current single-process API and admin UI. No database, broker, local user
system, multi-replica queue, or unsafe in-flight cancellation.

## Implementation Sequence

1. Contract/ADR: write the remaining endpoint/data contracts and ADR 0001.
2. Known devices backend: add durable JSON known-device store, endpoints,
	reachable merge, and tests.
3. Upload/import backend: add multipart IPA upload into durable storage/catalog,
	validation, conflict/replace behavior, and tests.
4. Workspace capability backend: add live read contract for current identity,
	roles, capabilities, delegated-auth limits, and tests.
5. Background operations backend: add in-process queued refresh worker,
	queued-only cancel, retry/rerun new-operation semantics, scheduler enqueue
	path, and tests.
6. Durable diagnostics backend: add grouped issue store derived from
	operations/readiness evidence, issue endpoints, triage transitions, and tests.
7. Admin UI bindings: update existing screens and add Operations surface against
	the backend contracts.
8. Run full deterministic gates, semantic judges, artifact validation, and
	prepare commit/merge/deploy handoff.

## Test Strategy

- Backend: xUnit API tests for each endpoint's empty, success, blocked,
  persistence/restart, corrupt-store, and adversarial edge cases.
- UI: TypeScript build, ESLint, existing Storybook/Playwright screen checks where
  possible, plus targeted fixture/state updates.
- IaC: `kubectl kustomize deploy/k8s`, `kubeconform -summary`, deploy secret scan.
- Semantic: Adversarial Judge after contract and after implementation phases.

## Rollback / Recovery

Stores are additive JSON files under `Sideport:State:Directory`; no destructive
migration is planned. Reverting the phase removes endpoints/UI while leaving
existing registrations and operation history intact. Uploaded IPAs are copied
under durable state and can be removed manually if a rollback requires cleanup.

## Human Approval Needed

No approval needed for local code/tests/docs. Required before any live
Kubernetes/Flux apply, restart, reconcile, network mutation, or secret access.
