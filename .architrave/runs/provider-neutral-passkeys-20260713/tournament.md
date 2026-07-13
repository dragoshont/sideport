# Tournament of Options
## Option A — WebAuthn inside Sideport
Rejected: duplicates identity/session/recovery security.
## Option B — Hardcode Authentik
Rejected: prevents portable deployments.
## Option C — Generic contract, optional adapter
Selected: OIDC stays portable; Authentik is one adapter.
## Decision Matrix
| Option | Portable | Security ownership | Decision |
|---|---|---|---|
| A | yes | Sideport duplicates IdP | reject |
| B | no | Authentik | reject |
| C | yes | configured IdP | select |
## Winner
Option C.
