# Recommended Plan

## Implementation Sequence
1. Open incident issue with commands/logs/signals.
2. Read Sideport device inventory source and tests; produce RCA/probe matrix.
3. Add minimal, low-cardinality metrics around installed-apps and parser warnings.
4. Add ApprenticeOps high-CPU scenarios and regenerate docs.
5. Run focused/full checks and record lessons.

## Test Strategy
- Sideport focused device/API tests for metrics behavior.
- Full `dotnet test Sideport.slnx`.
- ApprenticeOps JSON parse, scenario book regeneration, unique IDs, and deterministic `run_checks` against new gold answers.
- `git diff --check` in both repos.
- Architrave run artifact validation.
