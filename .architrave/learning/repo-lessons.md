# Architrave Repo Lessons

Candidate lessons learned while implementing in this repo. Keep this file short.
Each entry needs evidence and validation before promotion. Do not store secrets.
Promote repeated, stable lessons into `architrave.config.json`, `AGENTS.md`, `.github/instructions/`, or docs after review.

## Candidate Lessons

| Lesson | Evidence | Occurrences | Validated | Proposed Target | Status |
|---|---|---:|---|---|---|
| Treat `/api/devices/{udid}/installed-apps` as an expensive device-inventory read model, not a cheap list call; it performs installation_proxy browse plus misagent provisioning-profile retrieval/parse, so high CPU needs per-operation metrics, bounded caching, and polling scrutiny. | Incident sideport#2; run `.architrave/runs/sideport-high-cpu-20260624`; commit `ab87923` added metrics split by `installation_proxy_browse` vs `misagent_profiles`; `f16e647`/tag `v0.1.8` added the installed-apps cache; `ea653b1`/tag `v0.1.9` stopped scheduler startup from enqueueing doomed jobs before Anisette readiness; homelab deployed `0.1.9` in `2650ec0`; final live verification: first installed-apps call 1.688s, immediate cache hit 0.0015s, CertCountdown refresh operation `op_20260624_182022_45b24efbb5f5` succeeded, final Sideport CPU ~4m. | 1 | Yes, validated by source review, `dotnet test Sideport.slnx` (267/267), homelab `/healthz`/`/readyz`/`/metrics` 200, and live phone refresh success | docs/runbook or Sideport UI/backend instructions if it recurs | candidate |

