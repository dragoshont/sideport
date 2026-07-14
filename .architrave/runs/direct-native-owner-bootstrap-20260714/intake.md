# Intake — Direct Native Owner Bootstrap

## Problem

The first native-passkey Owner experience depends on a fragment link emitted to
startup logs. Lost cookies, expired links, restarts, and repeated setup attempts
can leave a nontechnical user at an error screen even though the deployment is
still empty.

## Approved outcome

- An unclaimed native-passkey deployment redirects `/` to `/owner-claim`.
- The page immediately offers Name, Email, and **Create passkey**.
- The browser never receives a raw bootstrap claim or recovery bearer.
- A lost browser cookie can retry without waiting for expiry.
- Existing recovery-bearer claims and active Owners remain authoritative.
- OIDC bootstrap, invitations, and later Owner recovery retain private links.

## Constraints

- Same-origin, WebAuthn user verification, rate limits, no-store, and durable
  audit records remain mandatory.
- An unclaimed deployment must remain loopback/LAN-only until claimed.
- Do not touch the unrelated device-recovery worktree.

## Grounding sources

- `docs/sideport-backend-contract.md`
- `docs/ui/sideport-ui-design-spec.md`
- `src/Sideport.Admin/src/WorkspaceHandoff.tsx` and its Storybook stories
- `src/Sideport.Api/WorkspaceAccess/*`
- `architrave.config.json`, the web/backend knowledge packs, and the repository
  phase/gate rules in `AGENTS.md`

## Assumptions

- Sideport remains single-replica as required by the current workspace store and
  device-operation architecture.
- Operators can keep an unclaimed origin private/loopback/LAN-only; Sideport
  cannot infer that topology behind arbitrary ingress.
- Native and OIDC identity modes remain mutually exclusive for a workspace.

## Blocking questions

None. The user explicitly approved replacing the startup-log link with direct
native first-Owner bootstrap and preserving private links for recovery and
invitations.
