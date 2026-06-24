# Intake

## Understanding

Continue Sideport beyond the completed operation/preflight SDD close-out and
finish the remaining roadmap phases autonomously: known-device inventory,
browser IPA upload/import, workspace role/capability contract, background
operation worker controls, durable diagnostics issues, and merge/deploy-ready
evidence.

## Acceptance Criteria

1. Remaining roadmap phases are derived from current Sideport docs/contracts.
2. The canonical backend contract defines each phase before implementation.
3. Implementation uses existing single-process .NET API, React admin UI, JSON
	 stores, and single-flight signer patterns.
4. No speculative database, broker, local user system, multi-replica worker, or
	 in-flight Apple/device cancellation is added.
5. Each phase has tests, deterministic gates, and adversarial judge review.
6. No live Kubernetes/Flux mutation or secret access is performed without an
	 exact approval step.

## Grounding Sources

- `architrave.config.json`
- `.github/instructions/sideport-ui.instructions.md`
- `docs/sideport-sdd-implementation-plan.md`
- `docs/sideport-backend-contract.md`
- `docs/architecture/adr-0001-roadmap-foundations.md`
- `knowledge/backend.md`, `knowledge/operations-ux.md`, `knowledge/yagni.md`
- `src/Sideport.Api/Program.cs`
- `src/Sideport.Api/Operations/*`
- `src/Sideport.Api/Catalog/AppCatalog.cs`
- `src/Sideport.Orchestrator/FileAppRegistry.cs`, `IpaStore.cs`, `RefreshScheduler.cs`
- `src/Sideport.Admin/src/App.tsx`, `api/sideportApi.ts`, `data/sideportTypes.ts`
- Service Architect and Operations UX reviews from Phase 1.

## Assumptions

- The current uncommitted SDD operation slice is intentional and remains the base.
- Sideport remains single-replica while JSON stores and process-local locks own
	safety.
- Workspace roles are initially a read/capability contract over bearer/OIDC
	identity; no local invitations/users yet.
- Browser upload stores and inspects IPAs only; install/sign remain separate
	operations.

## Blocking Questions

None for contract and local implementation. Live cluster apply and branch merge
will be reported as a final handoff unless an exact remote/runtime target is safe
and unambiguous.
