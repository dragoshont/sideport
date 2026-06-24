# Tournament of Options

## Option A — Minimal Safe Fix

Close only the proven gaps: renewal restart fallback, UI provenance labels, real
operation-stage rendering, and optional pairing-record docs.

Pros: small blast radius, follows existing operation store and UI types, directly
addresses reviewer findings, keeps gates focused.

Cons: does not advance larger product roadmap items.

Risk/blast radius: low to medium; touches one backend service, one API test file,
one UI adapter, one UI shell, and deploy/docs.

Verification burden: targeted API tests, admin build/lint, full Architrave gates,
semantic judge.

Decision: wins because it satisfies the current acceptance criteria without
inventing future infrastructure.

## Option B — Proper Architectural Fix

Route legacy refresh and scheduler through a full operation worker with durable
queue, cancel/rerun, retry, and diagnostics linkage.

Pros: eventually the right product shape for long-running Sideport operations.

Cons: larger contract, new worker semantics, migration/rollback questions,
cancel safety boundary, and UI workflow changes that exceed this close-out.

Risk/blast radius: high; crosses scheduler, orchestration, API, UI, and deploy.

Verification burden: broad backend and UI tests, race/cancel tests, more design
review.

Decision: loses for this phase. It belongs in a later SDD slice.

## Option C — Defer / Ask More

Stop after re-audit and document gaps for a future pass.

Pros: zero code risk.

Cons: leaves a known contract durability gap and UI honesty drift in place.

Risk/blast radius: low immediate change risk, medium product risk.

Verification burden: docs only.

Decision: loses because the gaps are bounded, well understood, and testable now.

## Decision Matrix

Option A is the first YAGNI ladder rung that satisfies the acceptance criteria:
reuse existing operation records, existing UI types, existing Kubernetes manifest,
and existing gates. Option B is too much architecture for this close-out. Option C
does not meet the user's autonomous implementation request.

## Winner

Option A: implement the narrow SDD close-out and keep later product slices
not-started.
