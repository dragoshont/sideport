# Architrave Repo Lessons

Candidate lessons learned while implementing in this repo. Keep this file short.
Each entry needs evidence and validation before promotion. Do not store secrets.
Promote repeated, stable lessons into `architrave.config.json`, `AGENTS.md`, `.github/instructions/`, or docs after review.

## Candidate Lessons

| Lesson | Evidence | Occurrences | Validated | Proposed Target | Status |
|---|---|---:|---|---|---|
| Treat `/api/devices/{udid}/installed-apps` as an expensive device-inventory read model, not a cheap list call; it performs installation_proxy browse plus misagent provisioning-profile retrieval/parse, so high CPU needs per-operation metrics and polling scrutiny. | Incident sideport#2; run `.architrave/runs/sideport-high-cpu-20260624`; commit `ab87923` added metrics split by `installation_proxy_browse` vs `misagent_profiles`. | 1 | Yes, validated by source review and `dotnet test Sideport.slnx` | docs/runbook or Sideport UI/backend instructions if it recurs | candidate |

