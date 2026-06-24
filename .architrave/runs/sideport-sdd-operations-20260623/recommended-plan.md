# Recommended Plan

## Summary

Create the contract first, judge it, implement the smallest operation/preflight backend slice, bind the UI honestly, and run gates.

## Implementation Sequence

1. Add `docs/sideport-backend-contract.md`.
2. Add `docs/sideport-sdd-implementation-plan.md`.
3. Point `architrave.config.json` backend contracts at the contract.
4. Judge the contract/plan.
5. Add operation store/DTOs/endpoints/tests.
6. Update UI API binding and capability labels.
7. Update stale docs.
8. Run frontend/backend gates and validate run artifacts.

## Test Strategy

- API tests for preflight missing registration, ready preflight, refresh operation records, idempotency, history persistence.
- Frontend build/lint.
- Backend build/test and IaC plan/policy.

## Rollback / Recovery

All operation records are additive JSON state under the Sideport state directory. Legacy refresh endpoint remains intact. If needed, remove operation endpoints/UI binding without changing existing app registration or refresh state.

## Human Approval Needed

Required only for runtime/Kubernetes mutations, secret access, or live deploy changes. None planned in this implementation run.
