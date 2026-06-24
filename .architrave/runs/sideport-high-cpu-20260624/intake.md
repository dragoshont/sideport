# Intake

## Understanding
Sideport high CPU on home pegged a worker thread and made the server fan loud. The follow-up preserves the incident evidence, adds observability, and adds ApprenticeOps eval scenarios.

## Acceptance Criteria
1. GitHub issue with exact investigation commands and log excerpts.
2. Source-grounded RCA with hypotheses/probes.
3. Sideport metrics for installed-apps latency/count/result and provisioning-profile parser warnings.
4. Tests and deterministic checks.
5. ApprenticeOps scenarios for high-CPU ops triage and mitigation.

## Grounding Sources
- `architrave.config.json`
- `knowledge/backend.md`
- `knowledge/yagni.md`
- `knowledge/learning-loop.md`
- Sideport source/tests
- Runtime SSH evidence captured in sideport#2
- ApprenticeOps `data/scenarios.json`

## Assumptions
Issue belongs in `dragoshont/sideport`; runtime observation uses SSH, not Homelab MCP. Homelab alert tracking belongs in `dragoshont/homelab`.

## Questions
None blocking.
