# Phase 4 — Managed App Imports Gate

Date: 2026-07-11
Verdict: PASS

## Delivered

- Additive path-free V2 catalog list, upload, import-root list, and rooted
  inspect/import endpoints; V1 endpoints remain available.
- Configured `{rootId, relativePath}` imports reject rooted paths, traversal,
  case ambiguity, and static symlink escape, then copy into managed storage.
- Browser and root imports enforce compressed-size and bounded ZIP inspection
  limits before publication.
- Managed artifacts publish to immutable versioned paths; catalog persistence
  is atomic and failed updates restore the previous record and artifact.
- Catalog-version compare-and-swap, actor-bound durable idempotency receipts,
  exact replay, and multi-source provenance merging.

## Deterministic evidence

Command: `dotnet test Sideport.slnx --no-restore --disable-build-servers`

- Sideport.Api.Tests: 97 passed
- Sideport.DeveloperApi.Tests: 89 passed
- Sideport.Devices.Tests: 63 passed
- Sideport.GrandSlam.Tests: 50 passed
- Sideport.Orchestrator.Tests: 45 passed
- Total: 344 passed, 0 failed, 0 skipped
- `git diff --check`: PASS

## Semantic evidence

Independent adversarial Phase 4 judge: PASS; no release blockers.

Physical-device acceptance is not part of this catalog phase and remains
unclaimed.
