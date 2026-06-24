# Recommended Plan

## Summary

Finish the Sideport operation/preflight SDD close-out with the smallest durable
contract fix and matching UI/deploy honesty updates.

## Implementation Sequence

1. Re-audit the current uncommitted Sideport operation slice and run baseline
	targeted checks.
2. Update `/api/renewals` so latest operation status/error stays honest while
	expiry falls back to latest durable successful operation history after API
	restart.
3. Add API restart tests for successful recovery and failed-after-success
	recovery.
4. Update the admin UI to label current-poll device presence and derived
	diagnostics honestly, and to render running operation stages from live
	operation records.
5. Update contract/docs and Kubernetes plan-only guidance for optional read-only
	`/var/lib/lockdown` pairing records.
6. Run targeted tests, full deterministic gates, run validation, and semantic
	judge.

## Test Strategy

- `dotnet test tests/Sideport.Api.Tests/Sideport.Api.Tests.csproj`
- `npm --prefix src/Sideport.Admin run build`
- `npm --prefix src/Sideport.Admin run lint`
- `gates/checks.sh`
- `gates/backend-checks.sh`
- `harness/validate-run.sh .architrave/runs/sideport-sdd-reaudit-20260624`
- Adversarial Judge post-implementation review.

## Rollback / Recovery

No schema migration or live infrastructure mutation is involved. Reverting this
slice restores the previous renewal behavior and UI labels. The operation store
file format remains unchanged.

## Human Approval Needed

None for this close-out. Live Kubernetes apply, secret access, runtime restarts,
background operation worker, and future product slices remain not-started and
require separate approval/scope.
