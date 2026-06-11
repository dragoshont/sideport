# Sideport / MCP / RE-apps — Consolidated Auth & Secret Custody Plan

Date: 2026-06-11
Status: **AUTHORITATIVE** — supersedes `sideport-secret-custody-tasks.md` (which
assumed Vaultwarden + `bw serve`; Vaultwarden was decommissioned 2026-06-11).

## Why this exists
Auth/custody design churned across several sessions. This is the single source
of truth that ends the churn: one invariant, one custody model, applied to all
three surfaces (Sideport, homelab MCP, reverse-engineered mobile-API clients),
with a concrete path to `main` + deployed-at-home. After this lands, auth work
is **done** and feature development resumes.

## The invariant (the rule that stops the straying)
> **Applications never collect, prompt for, or store long-lived secrets through
> the UI, browser, or chat. The only human-supplied auth input is a 2FA / OTP
> code (plus the non-secret account identifier). Every password / API key /
> token is resolved server-side from runtime secret custody.**

Consequences:
- Sideport's onboarding has **no password field** — Apple ID + (if challenged) a
  2FA code, nothing else.
- The MCP needs **no human secret entry at all** (machine keys come from custody).
- RE/mobile-API clients (Regina Maria, …) read credentials from custody and only
  ever surface a **2FA/OTP** to the human if the upstream forces one.

## Custody model (revamped — Vaultwarden REMOVED)
| Context | Custody source | Mechanism |
|---|---|---|
| **Cluster (home) — default** | native K8s Secret ← **Azure Key Vault** via ESO (or SOPS) | app reads env/file; unchanged app code |
| **Local dev (macOS)** | **macOS Keychain** | `security find-generic-password` |
| **Local dev (alt)** | **Azure Key Vault direct** | `DefaultAzureCredential` / `az login` |
| **Bootstrap root** | **SOPS / age** | Flux decryption (still the turtle-floor) |
| ~~Vaultwarden + bw serve~~ | **REMOVED** | deleted 2026-06-11; `VaultBackedCredentialProvider` is dead code |

Cluster note: "SOPS vs Azure Key Vault" is **invisible to the app** — both end as
a K8s Secret → env var. So the in-cluster path needs no new code; the only choice
is which backend fills the Secret (ESO/KV is the new default, SOPS still works).

## Per-surface application

### Sideport (`sideport` repo, branch `feature/sideport-ui-experience-plan`)
- **Onboarding = 2FA-only.** `PersonalAppleAccess` already implements this:
  `SignInAsync(appleId)` → `two-factor-required` challenge → `CompleteTwoFactorAsync(challengeId, code)`.
  The password is pulled by `ISessionManager` from the credential provider; the
  portal never sees it. **No flow change needed — this is correct already.**
- **Credential providers** (`IAppleCredentialProvider`):
  - keep `EnvironmentCredentialProvider` (cluster: env from KV/SOPS Secret) — default
  - **add** `AppleKeychainCredentialProvider` (local macOS dev)
  - **add (optional)** `AzureKeyVaultCredentialProvider` (local direct-to-KV)
  - **remove** `VaultBackedCredentialProvider` + the `credentialSource == "vault"`
    wiring in `Program.cs` (Vaultwarden is gone)
- **Re-point custody labels** in `PersonalAppleAccess.SecretCustody()/Label()/
  MissingCredentialMessage()`: `vaultwarden-via-bitwarden-cli` →
  `kubernetes-secret` (cluster) / `macos-keychain` (local); drop the bw-serve text.
- **App Store Connect JWT** (`AppStoreConnectAccess`) is the **passwordless,
  no-2FA** path for *paid* Developer teams — document as preferred where the team
  has an API key; the personal-Apple-ID + 2FA path remains for free accounts.

### homelab MCP (`homelab` repo, `apps/platform/mcp-proxy`)
- Pure machine keys; **no human auth, no 2FA.** Already env-var based.
- Complete custody by activating the staged `ExternalSecret`s (KV via ESO):
  `agent-secrets` (brings the currently-manual secret under GitOps) and optionally
  `homelab-mcp-auth` (swap with `secret.sops.yaml`, one owner). One-line
  kustomization edits already prepared + committed (commented).

### RE / mobile-API clients (Regina Maria, future OLX/eMAG)
- `ReginaMariaClient` reads `RM_*` from env/SOPS; static logins already seeded in
  KV (`rm-username`, `rm-password`, `rm-*`).
- Invariant applies: the client maintains its **session via refresh** and only
  bubbles up a **2FA/OTP** to the human if the upstream forces re-auth — it never
  prompts for a stored password. Static creds = KV; any rotating session token
  stays **on-cluster** (not pushed to the cloud).

## Sequence to stable → main → deployed-at-home
1. **Commit in-flight branch WIP** (builds clean — verified) so there's a stable base. ✅ this turn
2. **Code reconcile (Sideport):** remove dead Vaultwarden provider; add Keychain
   (+ optional KV-direct) provider; re-point custody labels. `dotnet build` + tests.
3. **Docs:** this plan supersedes the stale custody doc; fix the `Program.cs`
   credential-source comment.
4. **Merge `feature/sideport-ui-experience-plan` → `main`** (sideport repo).
5. **Deploy at home (homelab repo, GitOps):** activate the SidePort + MCP
   `ExternalSecret`s (the prepared one-line swaps), `flux reconcile`, restart pods
   (env-var consumers), verify KV→ESO→Secret hash + pod health.
6. **Resume feature development** (onboarding UX, App Store Connect JWT probe, etc.).

## Explicitly OUT of scope (parked, do NOT reopen during stabilization)
- Workload Identity (scoped + gated in `homelab` repo; revisit only if multi-node).
- Local SP-rotation CronJob (parked).
- Encryption-at-rest (optional; secrets are base64 in dqlite today).
- Migrating the other ~16 SOPS files (opportunistic later; SOPS works + is GitOps-clean).
