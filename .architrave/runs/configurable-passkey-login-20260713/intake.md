# Intake — Configurable OIDC and invitation passkey enrollment

Date: 2026-07-13

## Understanding

Keep Sideport provider-neutral while making invited-user sign-in simple:

- deployments configure the OIDC provider identity and user-facing login label;
- Authentik remains the currently implemented passkey-enrollment adapter;
- invited users receive **Create passkey** as the primary action when enrollment
  is available and a configurable existing-account login as the fallback;
- successful enrollment returns to the opaque Sideport invitation handoff.

## Acceptance Criteria

1. OIDC provider ID, display label, and login label are configurable.
2. Authentik passkey enrollment is offered only when its adapter is enabled.
3. The invitation screen shows **Create passkey** first and the configured
   existing-account login second.
4. Authentik enrollment returns to Sideport `/invite` without putting the
   Sideport invitation token in the provider URL.
5. A non-Authentik OIDC deployment does not claim passkey enrollment ownership.
6. API, UI, infrastructure-plan, screenshot, and secret gates pass.

## Grounding Sources

- `architrave.config.json`
- `docs/sideport-backend-contract.md`
- `docs/ui/sideport-ui-design-spec.md`
- `docs/architecture/adr-0002-family-access-authorization.md`
- existing `WorkspaceHandoff` and Authentik enrollment adapter

## Constraints

- Sideport does not implement WebAuthn credentials, password storage, account
  recovery, or a parallel local identity authority.
- Membership remains bound to the validated OIDC issuer and subject.
- Invitation tokens remain in URL fragments only until exchanged for the
  HttpOnly handoff cookie.
- No secrets enter browser responses, logs, or run artifacts.

## Assumptions

- Authentik remains the only account-provisioning adapter in this release.
- Other standards-compliant providers are supported for existing-account OIDC
  login when configured by the deployer.

## Blocking Questions

None. The user approved autonomous completion and the architecture boundary was
already decided.

## Out of scope

- A generic provisioning API for arbitrary OIDC providers.
- Direct Sign in with Apple.
- CalDAV credential validation.
- Replacing Authentik's existing Microsoft upstream login.
