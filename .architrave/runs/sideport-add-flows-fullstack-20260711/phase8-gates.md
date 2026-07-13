# Phase 8 — Family Security Contract Gate

Date: 2026-07-12
Status: PASS

## Delivered

- Accepted ADR and backend contract for exact OIDC issuer+subject membership,
  one active Owner, constrained Family access, stable device ownership,
  inherited registration ownership, snapshotted operation ownership,
  suspension/offboarding, recovery, audit, retention, restore, and rollback.
- Private Family and Owner links exchange their fragment token once through an
  exact bounded JSON body for an independent hashed HttpOnly handoff. The raw
  token never survives OIDC in browser storage.
- Post-login screens show the actual Authentik account and exact permissions or
  recovery impact before explicit acceptance.
- Public link shells require no-store/no-referrer, strict self-only CSP, no
  third-party content, and pre-routing request-body log suppression.
- OIDC display claims are untrusted presentation input: normalized, bounded,
  control-safe, text-rendered, and excluded from logs/audit/headers/paths.
- Canonical Storybook includes stable owner-setup and owner-recovery preview
  stories in addition to interaction coverage.

## Deterministic evidence

- Focused canonical Storybook: 23/23 PASS.
- `gates/checks.sh`: PASS; production build, lint, and 158/158 Storybook
  interaction/accessibility tests passed.
- `gates/backend-checks.sh`: PASS; build succeeded with 0 warnings/errors;
  487/487 tests passed (226 API, 96 Developer API, 64 Devices, 50 GrandSlam,
  51 Orchestrator); plan-only Kubernetes render passed; kubeconform reported
  6 valid/0 invalid; deploy secret scan passed.
- `gates/reconcile.sh`: PASS by transparent skip because tokens/tokenBuild are
  not configured; no design-generation capability is claimed.
- `git diff --check`: PASS.
- Browser QA: Family invitation entry/consent and Owner setup/recovery
  entry/consent inspected at desktop and exact 390×844; no horizontal overflow.
  A fresh Storybook console interval contained no warnings or errors.

## Non-blocking notes

- Vite retains the existing large-chunk advisory.
- The historical onboarding-prototype keyboard story retains its passing React
  async-`act` warning.
- The Architrave kit copy reports version 0.8.1 while the installed plugin is
  0.10.3; the gate reports this only as a non-blocking update nudge.

## Boundaries

- Phase 8 changed contract/run artifacts and Storybook-only proposal UI. It did
  not implement workspace authorization, mutate Authentik, publish an image,
  change the homelab, apply infrastructure, read secrets, stage, commit, or push.
- Physical USB/Wi-Fi acceptance remains Phase 14.

**VERDICT: PASS**
