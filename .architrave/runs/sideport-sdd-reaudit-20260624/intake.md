# Intake

## Understanding

Resume Sideport from the existing uncommitted SDD operation/preflight slice,
redo the deep audit, and autonomously close the highest-value implementation
gaps without jumping into later product slices.

The re-audit found the core operation foundation already implemented and green.
The remaining close-out work is: renewal restart durability, UI provenance
honesty for operation/device/diagnostics evidence, and plan-only deployment
pairing-record documentation.

## Acceptance Criteria

1. `/api/renewals` recovers the last known expiry from durable successful
	operation history after API restart, including when a newer failed attempt is
	the latest operation.
2. Renewal status, blocker, and operation ID describe the latest operation
	attempt honestly.
3. The Renewals single-flight strip renders running stages from `/api/operations`
	records, not from a fixed client-side pipeline.
4. Current reachable-device timestamps are labeled as current-poll derived
	evidence, not durable known-device last-seen history.
5. Admin-derived diagnostics from failed fetches or app `lastError` fields are
	labeled as derived snapshot evidence.
6. Kubernetes docs/manifests describe optional `/var/lib/lockdown` pairing
	records as read-only host trust material, with no live apply.
7. No background queue, cancel/rerun, database, workspace API, known-device
	inventory, browser upload, or durable diagnostics issue store is added in
	this close-out.

## Grounding Sources

- `architrave.config.json`
- `.github/instructions/sideport-ui.instructions.md`
- `docs/sideport-backend-contract.md`
- `docs/sideport-sdd-implementation-plan.md`
- `docs/ui/sideport-ui-data-contract.md`
- `src/Sideport.Api/Operations/OperationService.cs`
- `src/Sideport.Api/Operations/OperationStore.cs`
- `tests/Sideport.Api.Tests/ApiSmokeTests.cs`
- `src/Sideport.Admin/src/api/sideportApi.ts`
- `src/Sideport.Admin/src/App.tsx`
- `deploy/k8s/deployment.yaml`
- `deploy/k8s/README.md`
- Service Architect and Operations UX read-only reviews from this run.

## Assumptions

- The current uncommitted SDD operations slice is intentional work to preserve,
  not work to revert.
- A renewal row may need both latest-operation truth and latest-success expiry
  truth. Status/error should follow the latest operation attempt; expiry should
  fall back to the latest durable successful result when process-local state is
  absent.
- Storybook preview is not required for this pass because this is a narrow
  existing-UI honesty correction, not a new screen or major visual design.
- Runtime ops are not needed; no live cluster mutation is allowed.

## Blocking Questions

None. The re-audit findings were specific enough to proceed autonomously.
