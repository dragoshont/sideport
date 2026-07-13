# Intake
## Understanding
Make passkeys primary for Owner and member onboarding without making Sideport's identity contract Authentik-specific.
## Acceptance Criteria
1. Generic identity/enrollment API and configuration. 2. Owner and member passkey enrollment. 3. Existing-account fallback. 4. Sideport-only passkey login flow. 5. Shared Authentik flows unchanged.
## Grounding Sources
Backend contract, Workspace handoffs, Authentik blueprint, Storybook.
## Assumptions
OIDC remains the immutable identity authority; provider provisioning needs adapters.
## Blocking Questions
None.
