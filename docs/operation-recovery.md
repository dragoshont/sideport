# Recover an expired, outcome-unknown refresh

For the Sideport Owner or recovery-bearer operator. This procedure renews an
existing app in place without pretending its earlier install succeeded.

## Preconditions

- Keep one Sideport API process on the state directory. Production uses one
  replica with `Recreate`; never run a second signer against a restored copy.
- Back up the state, keyring, signing identity, IPA files and trusted device
  records. Verify the backup before deploying a recovery release.
- Keep automatic refresh disabled until the uncertain operation is resolved.
- The saved signing certificate must be reusable. Recovery cannot create or
  revoke a certificate, change Apple accounts/teams, uninstall an app, or pair
  a new device.
- The iPhone must be accepted, previously paired, reachable and trusted. USB is
  preferred when Wi-Fi transfer is unreliable; trusted Wi-Fi refresh is supported.

## 1. Observe the existing installation

Read `GET /api/operations/{id}` for the unknown refresh. Retain its target
device, bundle, account profile, team, version, artifact SHA-256 and expected
expiry. Do not overwrite its saved IPA with a newer catalog version.

Submit `POST /api/operations/{id}/reconcile` with a fresh idempotency key.
This operation reads the device; it does not authenticate to Apple, sign or install.

A matching version whose expected profile has elapsed can produce:

- `result.renewalEligible = true`;
- `result.success = false`;
- `result.safeToRerun = false`;
- the observed expiry, including `null` if it is unavailable.

An unavailable expiry is not proof that iOS pruned the profile. A differing
known expiry, version, identity, artifact, or ownership remains blocked.
The original operation stays unknown and the scheduler remains quarantined.

## 2. Authorize one new, bounded renewal

Within five minutes of that observation, the authenticated Owner/recovery caller
may POST to the original operation's `/rerun` route using:

```json
{
  "idempotencyKey": "a-new-unique-recovery-key",
  "supersedingRenewal": {
    "receiptOperationId": "the-reconcile-operation-id",
    "confirm": true,
    "deviceUdid": "exact-source-target-device",
    "bundleId": "exact-source-target-bundle",
    "version": "exact-source-target-version",
    "teamId": "exact-source-target-team",
    "accountProfileId": "exact-source-target-account-profile",
    "catalogSha256": "exact-source-target-sha256",
    "expectedExpiresAt": "the-source-result-expiry-in-ISO-8601"
  }
}
```

Use values returned by Sideport, not guessed identifiers. Never put credentials
in this JSON or command-line arguments. Family callers cannot authorize this
exception. Current ownership and authority are checked again at execution.

One receipt authorizes one child. The same key and intent replay that child's
result, including after completion; a changed intent conflicts. A different key
cannot reuse the receipt. A failed attempt requires fresh observation and
explicit new authorization; it is not an automatic retry.

Only this child may pass its named predecessor's quarantine. Other unknown or
active device work still blocks it.

## 3. Verify the result

Poll the returned operation ID. Acceptance or queueing is not completion.

Before installation, Sideport hashes a unique private copy of the original IPA
and persists its SHA-256, snapshot identifier and prepared expiry. Authority,
registration and source lineage are rechecked at this mutation boundary.
Failure to save the checkpoint prevents the install.

Success requires a fresh readback of the exact bundle/version and the newly
prepared future expiry. Only then does Sideport save the verified registration
link and complete the child. The original record is never rewritten as successful:
its quarantine is resolved by the verified successor relationship.

If the result is unknown or verification fails, preserve its checkpoint and
quarantine. Do not delete records, clear locks, uninstall the app, or fabricate
success. Reconnect the phone and use evidence-based reconciliation.

## 4. Restore automatic renewal

After all device uncertainty is resolved, enable the persisted scheduler through
the protected scheduler-settings API. Check `/api/scheduler/status`, not the
deployment environment variable. Verify a due-only evaluation and an actual
successful device-verified renewal. Confirm the app opens and its data remains.

## Restart and rollback

Startup recovery finishes before the worker/scheduler accept mutations.
Previous-process running operations are handled immediately, not after a
30-minute age threshold. Uncertain writes are never replayed; already durable,
verified finalization can resume without another install.

Legacy operation histories remain readable. Once a recovery observation, intent
or checkpoint is stored, history uses a `schemaVersion: 2` envelope. Older images
expect an array and deliberately fail to load it rather than discard the new
safety fields. Before the first envelope write, Sideport automatically preserves
the prior array as `operations.json.pre-envelope.bak` with owner-only permissions.
This is an emergency migration artifact, not permission for an automatic downgrade.
Do not roll back to an older image after starting recovery without a compatible
recovery plan. Prefer a forward fix; a backup restore requires
assessing the current device state and keeping scheduling paused. Do not hand-edit
the envelope or remove its recovery fields to make an older image accept it.
