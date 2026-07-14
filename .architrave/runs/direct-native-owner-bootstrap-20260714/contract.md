# Direct Native Owner Bootstrap Contract

## Presentation status

`GET /api/workspace/owner-claims/native-passkey/status`

- Access: public only when native passkey mode is configured.
- Cache: `Cache-Control: no-store`.
- Success body:

```json
{ "mode": "passkey", "state": "available|private-link-required|claimed" }
```

- `available`: no active Owner and no unexpired RecoveryBearer-created pending
  bootstrap claim.
- `private-link-required`: an unexpired non-System pending Owner claim exists;
  the direct flow must not replace it.
- `claimed`: workspace has an active Owner; direct first-Owner setup is closed.
- The response contains no identity, member, claim, handoff, recovery, or token
  material.

## Passkey options

`POST /api/workspace/owner-claims/native-passkey/options`

- Access: public native mode, exact same-origin request only, rate-limited.
- Input: `{ "displayName": string, "email": string }`.
- Fresh/unclaimed behavior: create a 15-minute System-owned Bootstrap claim,
  exchange it entirely server-side, and set only the opaque
  `__Host-sideport.owner-claim-handoff` Secure/HttpOnly/SameSite cookie.
- Retry behavior: inside the single-replica concurrency gate, reuse a valid
  browser handoff or revoke only a pending System-created Bootstrap claim while
  the workspace remains `BootstrapRequired`, then create a replacement.
- Success body: `{ "mode": "passkey", "creationOptions": string }`.
- Errors include `403 origin-or-antiforgery`, `404 owner-claim-unavailable` after
  claim, `409 owner-claim-pending` when RecoveryBearer authority exists, and
  `429 passkey-rate-limited`.
- Raw `spown1_` authority and the recovery bearer never appear in the response
  body, headers, cookies, logs, or durable plaintext.

## Completion

`POST /api/workspace/owner-claims/native-passkey/complete` retains the existing
contract: exact origin, opaque handoff cookie, bound Name/Email ceremony,
user-verified WebAuthn attestation, transactional Identity user/passkey creation,
atomic Owner acceptance, session sign-in, cookie cleanup, and rollback of the
new Identity user if Owner acceptance fails.

OIDC bootstrap, native/OIDC Owner recovery, and invitations continue to require
their existing private claim/invitation links and opaque handoff cookies.
