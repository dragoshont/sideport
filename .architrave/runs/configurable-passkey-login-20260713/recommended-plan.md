# Recommended plan

## Implementation Sequence

1. Extend authentication options with configurable provider ID, display label,
   and login label.
2. Give Authentik enrollment a fixed, same-origin Sideport return URL.
3. Present **Create passkey** first for an invitation only when enrollment is
   enabled; retain the configured OIDC login as fallback.
4. Cover URL construction, option projection, and the invitation UI with tests.
5. Run UI, backend, reconciliation, leak, and semantic gates before release.

## Test Strategy

- API unit test for the Authentik return URL.
- HTTP contract test for configured provider labels and honest disabled
  enrollment state.
- Storybook interaction tests for passkey-first and fallback-only invitations.
- Full UI/backend/IaC/screenshot/leak gates.

## Rollback / Recovery

Rollback removes only the optional labels and enrollment action. Existing OIDC
sessions, Sideport membership records, invitation records, and Authentik
credentials are unaffected.

## Human Approval Needed

The user already approved implementation, merge, and homelab deployment.
