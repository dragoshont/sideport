# Intake

## Understanding

Implement Sideport's next development slice using spec-driven development: write the contract/spec first, then implement the smallest operation/preflight foundation that resolves the audit's top backend/UI drift.

## Acceptance Criteria

1. Create or update an authoritative Sideport backend/UI contract.
2. Produce a full implementation plan with phases, dependencies, tests, and rollout notes.
3. Implement the first high-value operation/preflight slice autonomously where safe.
4. Keep UI and docs honest about live versus planned capabilities.
5. Run deterministic gates and record artifacts.

## Grounding Sources

- architrave.config.json
- docs/sideport-backend-contract.md
- docs/sideport-sdd-implementation-plan.md
- docs/ui/sideport-ui-data-contract.md
- docs/ui/sideport-end-to-end-install-refresh-plan.md
- src/Sideport.Api/Program.cs
- src/Sideport.Orchestrator durable JSON patterns
- src/Sideport.Admin/src/api/sideportApi.ts
- tests/Sideport.Api.Tests/ApiSmokeTests.cs

## Assumptions

Autonomous implementation is approved for repo code/docs/tests. Runtime/Kubernetes mutations, secret access, and live deploy changes remain out of scope without explicit approval.

## Blocking Questions

None.
