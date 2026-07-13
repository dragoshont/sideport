# Recommended Plan

## Implementation Sequence

1. Add a root-only bootstrap redirect before the OIDC challenge middleware.
2. Read the existing workspace authority store; do not add another state seam.
3. Add empty/active workspace browser-entry tests.
4. Run deterministic and semantic gates, then release through GitOps.

## Test Strategy

Cover anonymous and authenticated empty-root redirects, active-root OIDC
challenge, private-link availability, and the existing full backend suite.

## Rollback / Recovery

Remove the middleware and tests; workspace data and OIDC configuration are
unchanged.

## Human Approval Needed

The user explicitly requested implementation and the existing task authorizes
release/deployment completion.
