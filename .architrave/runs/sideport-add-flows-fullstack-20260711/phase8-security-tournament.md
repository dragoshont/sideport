# Phase 8 Security Tournament

Date: 2026-07-12
Status: completed and approved for the Phase 9 implementation boundary

## Decision criteria

The selected design must make an unknown OIDC principal powerless, keep
Authentik as the credential/session authority, give a nontechnical Family
member a private-link-to-install path, protect the one Owner signer/team, scope
every device/app/operation read and mutation, survive retry/restart, and retain
a safe rollback. Lower implementation novelty is preferred when security and
recovery are equal.

Scores are 1 (poor) through 5 (strong).

## 1. Product authorization authority

| Option | Security | Provider independence | UX | Change size | Verdict |
| --- | ---: | ---: | ---: | ---: | --- |
| Trust Authentik groups/role claims directly | 2 | 1 | 4 | 4 | Reject: claim drift can silently grant signer/device authority. |
| Sideport membership keyed by validated OIDC issuer+subject | 5 | 5 | 5 | 3 | **Select**: Authentik proves identity; Sideport owns stable product authority. |
| Sideport local password/passkey store | 3 | 5 | 3 | 1 | Reject: duplicates Authentik credentials, recovery, and sessions. |
| Apple Developer/GrandSlam or CalDAV login | 1 | 1 | 2 | 1 | Reject: not a supported web identity contract and couples login to signing secrets. |

## 2. Owner bootstrap and recovery

| Option | Security | Owner usability | Recovery | Change size | Verdict |
| --- | ---: | ---: | ---: | ---: | --- |
| First OIDC login becomes Owner | 1 | 5 | 2 | 5 | Reject: an arbitrary first principal wins full authority. |
| Deployment config pre-binds issuer+subject | 5 | 2 | 3 | 4 | Safe, but obtaining and typing the immutable subject is too technical for routine setup. |
| Paste existing recovery bearer into browser Owner form | 3 | 4 | 5 | 4 | Reject: moves the long-lived API secret into JavaScript/browser state. |
| Recovery-bearer tooling mints a short-lived hashed Owner-claim link; OIDC claimant accepts | 5 | 4 | 5 | 4 | **Select**: explicit dual proof, existing authority remains in its standard header, and the browser sees only expiring one-time authority. |
| Authentik group claim bootstraps Owner | 3 | 4 | 3 | 4 | Reject: provider-specific drift remains authoritative at the most sensitive boundary. |

The selected mint call is bearer-authenticated over effective HTTPS and returns
one short-lived fragment link once. The OIDC browser exchanges it for an opaque
HttpOnly handoff and explicitly accepts after login. Exactly one active human
Owner is supported; recovery replacement is allowed, but ordinary promotion or
a second Owner is deferred. Missing recovery proof blocks family-capable setup.

## 3. Invitation claim rule

| Option | Security | Family usability | Apple relay compatibility | Change size | Verdict |
| --- | ---: | ---: | ---: | ---: | --- |
| Pre-bind to contact email | 3 | 3 | 1 | 3 | Reject: email is not immutable authority and relay addresses undermine matching. |
| Private bearer link + valid Authentik identity | 4 | 5 | 5 | 5 | **Select**: one clear action; risk is bounded by entropy, expiry, single use, revocation, fragment handling, and audit. |
| Private link then Owner approves claimant | 5 | 2 | 5 | 2 | Reject for first release: adds waiting and a second admin action to the intended family path. |
| Authentik-admin-created invitation only | 4 | 2 | 4 | 3 | Reject: no provider-neutral Sideport invitation experience. |

The selected link grants only fixed role `family`; it never grants Owner or
signer-management authority. Contact email/name are owner-private delivery
metadata, not an identity constraint.

## 4. Family app-install authority

| Option | Security | One-action UX | Scarce-resource control | Verdict |
| --- | ---: | ---: | ---: | --- |
| Owner approves every install | 5 | 1 | 5 | Reject: defeats the agreed self-service approved-app flow. |
| Family imports/signs arbitrary IPAs | 1 | 5 | 1 | Reject: bypasses catalog approval and owner signer boundaries. |
| Family installs shared approved catalog apps on own device after preflight | 5 | 5 | 4 | **Select**. |

The selected preflight blocks on stale/missing signer state, certificate
mint/replacement/revocation, account/team changes, destructive mutations, or
scarce-limit overrides with `owner-action-required`. Normal use of an already
active signer and non-destructive provisioning within reported limits remains
one Family action.

## 5. Ownership model

| Option | Isolation | Migration safety | Simplicity | Verdict |
| --- | ---: | ---: | ---: | --- |
| Continue free-text device owner | 1 | 1 | 5 | Reject: editable presentation text is not authorization. |
| Per-operation actor only | 2 | 2 | 4 | Reject: scheduler/owner actions do not establish durable target ownership. |
| Stable device `ownerMemberId`; registrations/operations inherit or snapshot it | 5 | 5 | 4 | **Select**. |

Legacy/unassigned objects remain Owner-only. Name/email/actor display hashes are
never used to infer ownership.

## 6. Suspension and offboarding

| Option | Access safety | Capability truth | Recovery | Verdict |
| --- | ---: | ---: | ---: | --- |
| Continue automatic refresh | 1 | 3 | 4 | Reject: keeps spending Owner resources for a disabled member. |
| Remote uninstall/revoke everything | 2 | 1 | 1 | Reject: Sideport cannot guarantee those effects and could destroy shared signing state. |
| Deny immediately, stop future scheduling, safely settle running work, retain/freeze resources | 5 | 5 | 5 | **Select**. |

Offboarding is soft and audited. It does not imply Authentik/passkey revocation,
remote uninstall, profile/certificate revocation, or device wipe.

## 7. Invite secret transport

| Option | Proxy/log exposure | OIDC survival | Complexity | Verdict |
| --- | ---: | ---: | ---: | --- |
| Token in API path/query | 1 | 5 | 5 | Reject: current request logging records API paths. |
| Raw token in session storage through login | 3 | 4 | 5 | Reject: same-origin JavaScript retains membership-granting authority too long. |
| Fragment → one POST → independent opaque HttpOnly handoff → explicit post-login acceptance | 5 | 5 | 4 | **Select**: raw authority is cleared before OIDC and the user confirms the actual account and permissions. |

## Selected package

1. Authentik owns identity/passkeys/sessions; Sideport owns membership and
   authorization.
2. One active Owner is explicitly claimed through a bearer-minted, short-lived
   Owner link plus OIDC acceptance; ordinary invitations are Family-only.
3. A private single-use fragment link plus any valid Authentik identity is
   enough to accept; there is no email binding or second approval.
4. Family sees the approved household directory and shared catalog, but only its
   own devices, registrations, operations, renewals, and sanitized diagnostics.
   Self-service enrollment covers its first active iPhone; the Owner can
   deliberately add another after capacity preflight.
5. Family can install an approved app in one action when preflight is safe;
   Owner-only signer/scarcity changes block with a clear reason.
6. Suspension is checked per request and stops future scheduler work; no remote
   removal capability is invented.
7. One atomic workspace store commits membership/invitation/audit changes;
   migration fails closed and rollback to owner-equivalent OIDC is blocked after
   Family admission unless family access is first removed at Authentik/ingress.

## Explicitly deferred

Multiple Owners, local credentials, SMTP, directory synchronization, MDM,
per-member signers, family IPA import, arbitrary repository access, and
multi-replica authorization are not required for the stated family journey.
