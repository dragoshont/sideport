# Tournament of Options

## Option A — Minimal Safe Fix

Implement only known-device inventory next and defer the rest.

Pros: smallest blast radius and easiest tests.

Cons: does not satisfy the user's explicit request to finish the roadmap.

Risk/blast radius: low.

Verification burden: one JSON store, endpoints, UI labels.

Why it loses: too narrow for the approved autonomous roadmap continuation.

## Option B — Proper Architectural Fix

Implement every remaining roadmap slice as minimal foundations using existing
Sideport seams: JSON stores, single-process API, one hosted operation worker,
existing React admin surfaces, and plan-only Kubernetes evidence.

Pros: completes the roadmap without inventing a database/broker/local user
system; preserves single-flight signer safety; produces testable contracts.

Cons: broad diff and requires judge gates between phases.

Risk/blast radius: medium-high, but bounded to existing modules and storage
patterns.

Verification burden: API tests for each store/endpoint, admin build/lint,
screenshots where practical, backend/IaC gates, semantic judge.

Why it wins: it is the first YAGNI rung that satisfies "finish the roadmap".

## Option C — Defer / Ask More

Stop after planning and ask for explicit per-phase approval.

Pros: lowest implementation risk.

Cons: contradicts the user's approval to proceed autonomously.

Risk/blast radius: no code risk, high delivery risk.

Verification burden: docs only.

Why it loses: the user explicitly asked not to stop.

## Option D — Full Enterprise Replatform

Introduce a database, queue/broker, distributed locks, local workspace user store,
and multi-replica execution model now.

Pros: closer to a future SaaS/enterprise platform.

Cons: speculative, large migration surface, premature operational complexity,
and not necessary for single-replica homelab Sideport.

Risk/blast radius: very high.

Verification burden: migrations, rollbacks, authz, concurrency, deployment.

Why it loses: violates current evidence and YAGNI.

## Decision Matrix

Option B wins. It implements the roadmap as concrete foundations while stopping
short of speculative enterprise features. Sequence: known devices, upload/import,
workspace capabilities, async operations, diagnostics, UI binding, gates, judge,
merge/deploy handoff.

## Winner

Option B — minimal full-roadmap foundations on existing Sideport seams.
