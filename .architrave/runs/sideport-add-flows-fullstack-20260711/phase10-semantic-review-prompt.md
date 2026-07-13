You are an independent adversarial reviewer for Sideport Phase 10 only.
Review current repo state against AGENTS.md, gates/rubric.md,
.architrave/runs/sideport-add-flows-fullstack-20260711/phase10-intake.md,
phase10-options.md, phase-ledger.md, ADR 0002, and the backend contract.

Inspect the actual diff, especially src/Sideport.Api/Authentik,
WorkspaceAccess enrollment endpoint/security/policy, Program OIDC/proxy config,
deploy/authentik, deploy/k8s, and Authentik/Workspace tests.

Acceptance criteria:
1. Authentik owns users, WebAuthn/passkeys, sessions and recovery. Sideport never
   handles credential ceremony or stores a credential.
2. Enrollment-link minting requires a valid pending opaque Sideport invitation
   handoff, is bounded/rate-limited/effective-HTTPS/same-origin, and grants no
   membership.
3. Authentik API token is server-only, never browser/persistence/log/audit/IaC
   plaintext, and errors are redacted.
4. Adapter is disabled by default, retry-safe for a lost response, creates only
   single-use short-lived invitation objects for the configured flow, and
   constructs URLs only from configured HTTPS Authentik origin/slug.
5. Existing-account OIDC sign-in still works without the adapter; safe auth
   options truthfully report provider/enrollment/recovery and no official Apple
   login claim.
6. Forwarded headers are consumed only for exact known proxies/networks;
   PublicOrigin/OIDC callback/provider contract is HTTPS and exact.
7. Blueprint is invitation-only and requires a discoverable, user-verified
   WebAuthn credential while supporting client-device/hybrid/security-key hints.
8. Infrastructure is plan-only; no API apply, cluster apply, secret
   materialization, deployment, commit, or push occurred.

Deterministic evidence: backend-checks PASS; API 467/467; Orchestrator 53/53;
Developer API 98/98; Devices 64/64; GrandSlam 50/50; zero build warnings/errors;
Kubernetes 6/6 valid; secret scan PASS.

Return full rubric output and VERDICT PASS/REVISE/FAIL. PASS requires zero
Blockers and zero Majors. Do not edit files.
