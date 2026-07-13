# Intake

## Understanding

When no active Sideport Owner exists, navigation to `/` must lead to the public
`/owner-claim` bootstrap surface instead of the signed-in product shell or OIDC
challenge.

## Acceptance Criteria

1. `GET` and `HEAD /` redirect temporarily to `/owner-claim` while the workspace
   is missing or bootstrap-required.
2. The redirect is no-store and does not loop through OIDC.
3. `/owner-claim`, APIs, probes, assets, and OIDC callbacks keep existing rules.
4. Once an active Owner exists, anonymous `/` resumes the normal OIDC challenge
   and authenticated `/` serves the product shell.

## Grounding Sources

- `docs/sideport-backend-contract.md`
- `docs/ui/sideport-ui-design-spec.md`
- `src/Sideport.Api/Program.cs`
- `tests/Sideport.Api.Tests/BrowserEntryEndpointTests.cs`

## Assumptions

“No account” means no active Sideport Owner, not absence of an upstream OIDC
account.

## Blocking Questions

None.
