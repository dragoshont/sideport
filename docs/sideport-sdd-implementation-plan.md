# Sideport SDD Implementation Plan

Date: 2026-06-24
Status: active plan; re-audit close-out in progress

This plan turns the 2026-06-23 status audit into a spec-driven implementation
sequence. The governing contract is `docs/sideport-backend-contract.md`.

## 2026-06-24 Re-Audit Outcome

The operation/preflight slice is the right foundation and should be finished
before starting larger product slices. The close-out work is deliberately small:

- Recover renewal expiry and risk from the latest durable successful operation
  record after an API restart, not only from process-local refresh state.
- Render Renewals' running pipeline from `/api/operations` stage records rather
  than a fixed authorize/provision/sign/install preview.
- Treat current-poll device presence and admin-derived diagnostic issues as
  `derived` evidence until persistent device inventory and durable diagnostics
  exist.
- Document the optional read-only `/var/lib/lockdown` pairing-record mount for
  Kubernetes hosts that own Wi-Fi pairing material.

## Goal

Make Sideport's admin UI, API, docs, and deploy story converge around truthful
operations: every refresh/sign/install action has preflight, an operation ID,
stage evidence, and a durable record before the UI presents it as operationally
complete.

## Non-Goals For This Run

- No live Kubernetes/Flux apply, restart, reconcile, or secret access.
- No browser IPA upload.
- No background queue/cancel worker.
- No persisted known-device inventory beyond the existing reachable snapshot.
- No real workspace user/role administration.

## Phase 0 — Contract And Drift Cleanup

Deliverables:

- Add `docs/sideport-backend-contract.md`.
- Point `architrave.config.json` `backend.contracts` at the contract.
- Update stale UI data-contract/docs to reference the canonical contract.

Acceptance:

- Contract lists current live endpoints and planned operation endpoints.
- Docs no longer claim old paths such as `/api/refresh/{udid}/{bundleId}` as the
  active API.
- UI capability rules distinguish source from availability.

## Phase 1 — Durable Operation Foundation

Backend deliverables:

- Add a small JSON-backed operation store under `Sideport:State:Directory`.
- Add operation DTOs: operation record, target, stage, result, issue/warning,
  actor, stage timing/error, result, issue/warning, scarce limit.
- Add endpoints:
  - `POST /api/operations/preflight`
  - `POST /api/operations/refresh`
  - `GET /api/operations`
  - `GET /api/operations/{operationId}`
  - `GET /api/renewals`
- Keep legacy `POST /api/apps/{udid}/{bundleId}/refresh` working.
- Explicitly keep legacy refresh and scheduler refresh outside operation history
  in this first slice; later route them through an operation service once the
  background execution model exists.

YAGNI boundary:

- Operations are durable records around the current synchronous refresh loop.
- No background queue abstraction until cancel/rerun/scheduler UX truly requires
  it.
- Use the existing durable JSON-file pattern with process-local locks and atomic
  replace. Do not add a database or message queue.

Tests:

- Preflight blocks missing registrations.
- Preflight reports existing registration + planned mutations.
- Refresh operation records blocked preflight.
- Refresh operation records success/failure from orchestrator result.
- Idempotency key returns the existing record.
- Duplicate idempotency submissions do not run refresh twice.
- Operation history persists across API restart.
- Operation-history load failure returns a structured API error instead of
  silently losing history.
- Renewal expiry, risk, and operation ID recover from latest durable successful
  operation history after an API restart.

## Phase 2 — UI Binding And Honesty

Frontend deliverables:

- Fetch `/api/operations` and `/api/renewals` when available.
- Use live operation IDs/stages in Renewals/Diagnostics where present.
- Render running operation stages from operation records instead of a fixed
  client-side pipeline.
- Label current device presence as a current-poll derived fact until known-device
  inventory exists.
- Label admin-synthesized diagnostic cards as derived from API/app snapshots, not
  as durable diagnostic issue records.
- Keep cancel/rerun disabled unless backend capability flags allow them.
- Treat `/api/workspace` failure as planned/delegated auth, not a live system
  fault.
- Fix source-label misuse for missing catalog/empty live results.

Tests:

- TypeScript build and ESLint.
- Storybook/screens continue to render.
- Add or update Playwright coverage for renewals/operation state if the UI diff
  is substantial.

## Phase 3 — Deploy Readiness Hardening

Deploy deliverables:

- Add plan-only Kubernetes changes for persistent Sideport state and anisette
  identity.
- Document `/var/lib/lockdown` read-only pairing-record mount.
- Keep secrets out of manifests; examples only.

Tests:

- `kubectl kustomize deploy/k8s`.
- `kubeconform -summary`.
- deploy secret scan.

Human approval:

- Required before any live cluster mutation or secret access.

## Phase 4 — Later Product Slices

- Known-phone inventory.
- Browser IPA upload/import.
- Background operation worker with safe cancel/retry/rerun.
- Durable diagnostics issue store with trace/log links.
- Workspace users/roles contract.

## Verification Gates

- `gates/checks.sh`
- `gates/backend-checks.sh`
- `harness/validate-run.sh <run-dir>`
- Semantic judge before and after implementation for contract/capability honesty.