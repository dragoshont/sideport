# Phase 10 Intake — Authentik Passkeys and Proxy Trust

Date: 2026-07-13
Status: active

## Outcome

Allow a person holding a valid private Sideport invitation to either sign in to
an existing Authentik account or create an Authentik account with a passkey,
then return to Sideport for explicit membership acceptance. Sideport remains the
membership authority; Authentik remains the credential, passkey, session, and
recovery authority.

## Acceptance criteria

1. Sideport never stores, validates, resets, or receives a WebAuthn credential.
2. A raw Sideport invitation is exchanged first for the existing opaque,
   HttpOnly Sideport handoff cookie. Only then may the public shell request an
   Authentik enrollment URL.
3. The optional server adapter creates one single-use Authentik invitation with
   bounded expiry through the documented API; its API token is server-only and
   never persisted in `workspace-access.json`, logs, audit, HTML, or browser
   storage.
4. Existing Authentik users can use ordinary OIDC sign-in without enrollment.
5. New users are guided through an Authentik invitation-only enrollment flow
   that creates an external account and requires WebAuthn/passkey setup before
   login completion. Platform and synced passkeys are supported; Sideport does
   not promise a specific vendor or device.
6. Recovery remains Authentik-owned and links to an explicitly configured
   recovery flow; Sideport does not infer credential state from OIDC claims.
7. Sideport accepts forwarded scheme/host/client IP only from exact configured
   proxy addresses or CIDRs and requires an HTTPS `PublicOrigin` whose OIDC
   callback exactly matches the Authentik provider redirect URI.
8. Repo infrastructure changes are plan-only. No Authentik API mutation,
   secret materialization, cluster apply, or homelab change occurs in Phase 10.

## Grounding

- Existing Sideport OIDC relying party and trusted-forward-header boundary in
  `src/Sideport.Api/Program.cs`.
- Phase 8 threat model and ADR 0002 identity/authority separation.
- Authentik official invitation API fields: `pk`, `expires`, `fixed_data`,
  `single_use`, `flow`; invitation stage defaults to fail closed when no
  invitation is present.
- Authentik official WebAuthn setup stage and current default authentication
  flow's passwordless WebAuthn support.

## Blocking questions

None for repository implementation. Actual Authentik hostname, API token,
provider client secret, and trusted Traefik pod/network values remain deployment
inputs at the human apply boundary.
