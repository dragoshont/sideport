# Sideport Secret Custody Tasks

Date: 2026-06-10
Status: **SUPERSEDED** by `sideport-auth-custody-consolidated-plan.md` (2026-06-11).
> This doc's default path (Vaultwarden + `bw serve`) is **obsolete** — Vaultwarden
> was decommissioned 2026-06-11 in favour of Azure Key Vault + External Secrets
> Operator. Kept for history only; follow the consolidated plan for current design.

## Decision

For the Ubuntu homelab, Sideport Apple credentials use a two-tier model:

- Static secrets live in Vaultwarden and are read by Sideport through a private Bitwarden CLI `bw serve` bridge.
- SOPS/age remains the bootstrap root and fallback path for GitOps/Kubernetes secrets.
- macOS Keychain helper is only a local-dev or remote-helper fallback.

Default path:

```text
Vaultwarden item -> private bw serve bridge -> VaultBackedCredentialProvider -> Personal Apple ID connector
```

Fallback path:

```text
secret.sops.yaml -> Flux/SOPS decryption -> Kubernetes Secret -> Sideport env/file -> Personal Apple ID connector
```

The browser must never collect Apple passwords. The portal may collect Apple ID and 2FA code, but the password comes from runtime secret custody.

## Tasks

### 1. Vaultwarden Bridge Configuration

Document the preferred runtime configuration:

```text
Sideport:Apple:CredentialSource=vault
Sideport:Vault:BaseUrl=http://127.0.0.1:8087
Sideport:Vault:ItemNameTemplate={appleId}
Sideport:Vault:ApiKey=<optional bearer token>
```

The Vaultwarden item can match by item name or `login.username`.

### 2. bw serve Sidecar / Private Bridge

Add deployment docs for a private Bitwarden CLI bridge:

- `bw serve` must not be internet-exposed.
- Prefer loopback/private pod network only.
- If reachable beyond loopback, protect with reverse proxy bearer token or mTLS.
- The bridge must run with an unlocked Bitwarden session.
- Sideport must only receive the retrieved credential, never expose it to the browser.

### 3. SOPS Deployment Secret Fallback

Create a Sideport secret template:

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: sideport-apple-credentials
  namespace: default
stringData:
  SIDEPORT_PERSONAL_APPLE_ID: you@example.com
  SIDEPORT_APPLE_PW_YOU_EXAMPLE_COM: replace-me
```

Then document encryption:

```bash
sops --encrypt --in-place apps/platform/sideport/secret.sops.yaml
```

### 4. Kustomize/Flux Wiring

Add the encrypted secret to the Sideport kustomization after it exists:

```yaml
resources:
  - deployment.yaml
  - service.yaml
  - secret.sops.yaml
```

Mount into the deployment as environment variables or, preferably later, secret files.

### 5. API File-Based Secret Support

Current implementation supports `SIDEPORT_APPLE_PW_*` env vars. Add support for:

```text
SIDEPORT_APPLE_PW_<APPLEID>_FILE=/run/secrets/apple-password
SIDEPORT_PERSONAL_APPLE_ID_FILE=/run/secrets/apple-id
```

Reason: file mounts are easier to constrain and rotate than broad environment variables.

### 6. Portal Custody Badges

Apple Access page should show:

- `Stored in Vaultwarden`
- `Read through private bw serve bridge`
- `SOPS/age encrypted in Git`
- `Mounted as Kubernetes Secret`
- `Browser never sees password`
- `2FA entered locally`
- `No Apple mutations without preflight`

### 7. Rotation Runbook

Document Vaultwarden rotation:

1. Update the Apple password/app-specific credential in Vaultwarden.
2. Ensure `bw serve` has a fresh unlocked session.
3. Restart or refresh Sideport if needed.
4. Run Personal Apple connector status/sign-in probe.

Document SOPS fallback rotation:

1. Update Apple password/app-specific credential.
2. Edit `secret.sops.yaml` with `sops edit`.
3. Commit encrypted change.
4. Reconcile Flux.
5. Restart Sideport deployment.
6. Run Personal Apple connector status/sign-in probe.

### 8. Safety Tests

Add tests for:

- env secret present
- file secret present
- vault item present
- vault outage throws and is surfaced as a blocker, not "missing credential"
- missing secret
- secret redaction in status/logs
- browser cannot submit password
- Personal connector returns `credential-configured` without exposing value

### 9. Future Optional Helper

Only after SOPS path is complete, consider a helper for non-server custody:

- macOS Keychain helper for local dev
- 1Password/Bitwarden/Vault integrations beyond the current Vaultwarden bridge
- remote helper must use pairing token/mTLS, bind to loopback by default, and expose operations only

## Anti-Patterns

- Raw Apple password in browser form.
- Raw Apple password in README examples.
- Raw Apple password in Git, even private Git.
- Docker image `ENV`/`ARG` for credentials.
- Long-lived helper token without scope/revocation.
- Browser cookie scraping for Apple websites.
- Certificate revocation without explicit cutover acknowledgement.
