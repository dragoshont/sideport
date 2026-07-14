# Threat Model — Direct First-Owner Bootstrap

## Authority boundary

The direct flow is intentionally first-visitor setup only while the workspace is
unclaimed. The WebAuthn options request requires exact same-origin evidence,
user verification, and rate limits. The server creates the raw bootstrap claim,
exchanges it internally, persists only hashes/audit records, and sends the
browser only an opaque Secure/HttpOnly/SameSite handoff cookie.

## Network exposure

Sideport cannot safely infer “LAN” from a request address behind arbitrary
reverse proxies, tunnels, NAT, or Kubernetes ingress. It therefore does not
pretend to enforce a network topology it cannot prove. The deployment contract
requires the origin to remain loopback/private/LAN-only until claimed; the
Docker, Apple Container, and Kubernetes documentation state this explicitly.
The public status endpoint contains only `available`,
`private-link-required`, or `claimed`; it grants no authority.

If an operator exposes an unclaimed origin publicly, first-visitor takeover is
possible by design. The safe alternatives are to keep ingress private during
bootstrap or use the existing recovery-bearer-created private claim path.

## Retry and recovery isolation

- Retry revokes only a pending Bootstrap claim created by the System actor while
  the workspace is still `BootstrapRequired`.
- It cannot revoke a RecoveryBearer-created claim.
- It cannot create or revoke direct bootstrap authority after an active Owner
  exists.
- Root redirect, exact-origin rejection, rate limiting, recovery-claim
  preservation, system audit records, lost-cookie retry, and active-workspace
  denial have deterministic tests.
