# Tournament of options

## A — Build passkeys directly in Sideport

Rejected. It duplicates credential ceremony, recovery, session security, and
cross-platform WebAuthn policy inside the app.

## B — Couple all identity and copy to Authentik

Rejected. It makes normal OIDC login unnecessarily deployment-specific and
would mislabel deployments that use another provider.

## C — Provider-neutral OIDC with an optional Authentik enrollment adapter

Selected. Sideport configures the provider label and login action, retains
issuer-plus-subject membership authority, and exposes the Authentik-specific
passkey action only when its least-privilege enrollment integration is enabled.

This is the smallest truthful change and preserves a clean seam for another
provider without claiming that a generic account-provisioning contract exists.

## Decision Matrix

| Option | Simplicity | Security ownership | Provider portability | Capability honesty | Decision |
|---|---|---|---|---|---|
| A | low | duplicates WebAuthn in Sideport | medium | low | reject |
| B | medium | Authentik | low | medium | reject |
| C | high | OIDC provider / Authentik adapter | high for login | high | select |

## Winner

Option C.
