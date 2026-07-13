# Recommended Plan
## Implementation Sequence
Generic contract, Owner endpoint, UI binding, Sideport-only Authentik flow, configured passkey-first action, existing-account fallback, tests, release.
## Test Strategy
Focused Owner/member enrollment, Storybook coverage for passkey-first plus Microsoft fallback and enrollment-disabled fallback, invalid/missing handoff rejection, full UI/backend/IaC/security gates, and reconciliation.
## Rollback / Recovery
Revert Sideport image and Authentik provider flow binding; shared authentication is unchanged.
## Human Approval Needed
User explicitly approved refactor and passkey configuration.
