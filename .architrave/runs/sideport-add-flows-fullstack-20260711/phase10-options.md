# Phase 10 Options

## A. Sideport implements local passkeys

Rejected. It duplicates Authentik user lifecycle, WebAuthn ceremony, recovery,
session management, device listing, and credential deletion.

## B. Sideport sends every invitee to normal Authentik login only

Safe but incomplete. Existing accounts work, but a nontechnical new person has
no invitation-scoped account/passkey creation journey.

## C. Optional server-side Authentik enrollment adapter — selected

After Sideport exchanges its private invitation for an opaque handoff, a
server-only adapter creates a separate single-use Authentik enrollment
invitation. Authentik creates the account and passkey; Sideport later binds the
validated OIDC issuer+subject through its own unchanged acceptance endpoint.

Benefits:

- one understandable invite flow for existing and new Authentik users;
- no browser-visible Authentik API token;
- no coupling of Sideport authorization to Authentik groups;
- adapter can be disabled without weakening normal OIDC sign-in;
- easy to test through a fake adapter and plan-only blueprint.

Risks and controls:

- two invitation authorities: copy explicitly distinguishes Sideport membership
  from the temporary Authentik enrollment link; both are single-use and short
  lived;
- server API token: least-privilege secret-store input, redacted fixed errors,
  no persistence or logging;
- provider drift: exact OIDC issuer+subject remains the only identity binding;
- partial Authentik failure: Sideport handoff remains pending and the user can
  retry or choose existing-account sign-in.

## D. Pre-create Authentik users and reset links

Rejected. It creates directory objects before a person consents and expands the
adapter to password/recovery administration.
