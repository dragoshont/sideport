You are the independent adversarial judge for completed Sideport Phase 13.
Review current repo state and actual diff against AGENTS.md, gates/rubric.md,
phase13-intake/options/gates, phase-ledger.md, backend contract, and UI spec.

Acceptance criteria:
1. Accepted iPhone continues directly to the approved app library while Trust,
   pairing, and acceptance remain automatic after one explicit start.
2. Developer Mode/restart/reconnect is guidance only, not device evidence.
3. Library/global search prioritize ready apps and show name, description,
   version, provenance, and trusted same-origin icon or fallback.
4. Upload, configured storage, public GitHub, and selected private GitHub reuse
   existing bounded imports and exact read-only permissions; Member import is
   absent.
5. Installed-phone metadata is never treated as IPA source; unmatched apps say
   IPA file needed.
6. One action submits exact preflight/planVersion and renders durable operation
   stages/recovery; no simulated completion.
7. Chime and `Installed — you can unplug` occur only after result.success=true
   or immutable onboarding receipt. Audio is best effort and never proof.
8. No launch, Developer Mode, profile-trust, Wi-Fi success, or audio-delivery
   claim exceeds evidence.
9. Icon endpoint accepts only bounded in-bundle structurally-PNG bytes from the
   managed IPA, is capability-scoped/same-origin, and returns 404 otherwise.
10. Phase 14 transport work and deployment remain out of scope/not claimed.

Evidence: UI 86/86; Playwright 14/14; API 479/479; Orchestrator 54/54;
Developer API 102/102; Devices 64/64; GrandSlam 50/50; zero build warnings/
errors; Kubernetes 6/6; secret/diff PASS.

Return full rubric output and explicit VERDICT. PASS requires zero Blockers and
zero Majors. Do not edit files.
