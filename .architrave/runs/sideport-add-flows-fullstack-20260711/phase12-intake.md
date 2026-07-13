# Phase 12 Intake

## Objective

Give the active Owner one safe Settings/Signing flow to reauthenticate the
server-custodied Apple account, select only a team returned by Apple, review
exact registration/certificate/profile impact, and deliberately replace the
single active signer authority without creating multi-signer architecture.

## Acceptance criteria

1. Owner-only and same-origin/effective-HTTPS protected.
2. Fresh Apple authentication is required before teams/certificates are read.
3. Team IDs are selected only from Apple's current response.
4. Preflight names exact certificate IDs and counts exact affected
   registrations/devices/profiles.
5. Cutover accepts the exact current preflight ID, inventory version,
   certificate IDs, impact codes, actor, and idempotency key.
6. Revalidation and replacement share the existing signer lock; no revoke-all.
7. Persisted operation intent supports replay/recovery and never expands the
   authorized certificate set.
8. Target-team identity is verified before registrations and team selection are
   rebound.
9. Different-account candidate credentials and 2FA remain isolated until the
   full atomic account cutover is implemented; the old account stays active on
   failure.

## Boundaries

- No simultaneous signers, per-member signer, arbitrary Team ID, deployment,
  certificate revocation outside an exact preflight, or infrastructure apply.
- Phase remains in progress until different-account cutover and recovery tests
  pass.
