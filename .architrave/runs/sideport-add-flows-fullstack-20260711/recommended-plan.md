# Historical Recommended Plan — Phases 1–6

Status: superseded on 2026-07-12. Retained only as the audit record for the
original add-flow implementation. It is not the current execution order.

The canonical current plan is
[`phase-ledger.md`](./phase-ledger.md). Family authorization is specified by
[`phase8-recommended-plan.md`](./phase8-recommended-plan.md), and Apple Container
work is explicitly deferred to Phase 15. Step 7 below was not executed under
this historical plan.

The canonical cross-tier details remain in
`docs/ui/sideport-onboarding-implementation-plan.md` and
`docs/sideport-backend-contract.md`. This run executes the smallest safe subset
needed for always-available add flows.

1. Complete Storybook and signed-in fixture mockups, accessibility interactions,
   responsive checks, and semantic review.
2. Create `codex/apple-like-add-flows`; preserve the dirty tree; transition the
   ledger from mockup to runtime implementation.
3. Stop passive pairing, represent trust honestly, and implement durable,
   idempotent Add iPhone enrollment/acceptance.
4. Unify catalog ingestion into managed durable storage; harden upload and
   configured-root server imports.
5. Implement public release discovery/import and selected-repository private
   authorization with ephemeral server-side credentials.
6. Bind the approved UI to live endpoints and server-enforced capabilities,
   including reload/resume, errors, and recovery.
7. Add the secret-free Apple `container` launcher/docs and run plan/policy only.
8. Run UI, backend, security, reconciliation, semantic, and available physical
   seam gates. Do not claim USB/Wi-Fi or Apple runtime acceptance without the
   required host/device evidence.
