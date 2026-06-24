# ADR 0001: Sideport Roadmap Foundations

Date: 2026-06-24
Status: accepted for SDD implementation

## Context

Sideport is still a single-process .NET API with React admin UI, one active signer, and PVC-backed JSON state. The operation/preflight slice has established a durable operation history under `Sideport:State:Directory` without adding a database or broker.

The remaining roadmap needs persisted known devices, browser IPA import, workspace role/capability vocabulary, background operation execution, and durable diagnostics. Those features must not break the free-account single-signer invariant or claim capabilities the backend cannot perform.

## Decision

Use existing repo seams and small JSON-backed stores for the roadmap foundation:

- Known devices live in `known-devices.json` and merge durable inventory with the current `/api/devices` reachable poll.
- Uploaded/imported IPAs are copied into the existing PVC-backed IPA store and recorded in the existing catalog shape, extended with upload/import provenance.
- Workspace roles are initially a read contract over current bearer/OIDC identity and static capability rules. It does not own invitations, passwords, sessions, or local users yet.
- Background operations use the existing `OperationStore` plus one in-process hosted worker. Sideport remains single-replica and single-flight. No database, broker, distributed lock, or multi-replica queue in this phase.
- Cancel is only safe for queued/waiting operations. In-flight Apple/device mutation stages stay non-cancelable until a stage-specific safe boundary is proven.
- Retry/rerun create new operations after fresh preflight; they do not mutate historical operation records.
- Durable diagnostics issues are grouped from real operation/readiness/device/log evidence. No fake OpenTelemetry links are emitted until a real exporter exists.

## Consequences

- The Kubernetes deployment must remain one replica with `Recreate` while JSON stores and process-local locks own execution safety.
- All new stores use the existing atomic temp-file replace pattern.
- API/UI contracts must label source and timestamp. Current poll evidence is not durable inventory; derived issues are not durable diagnostics.
- Future multi-replica or enterprise RBAC work will require a new ADR for database/queue/identity migration.

## Non-Goals

- No local password system or invitation lifecycle yet.
- No chunked/resumable upload or external malware scanning yet.
- No in-flight Apple/device operation cancellation yet.
- No distributed scheduler or broker yet.
- No live Kubernetes apply in this SDD run.
