# Repo Lessons

## Candidate — ASP.NET Identity passkey storage needs schema version 3

When using .NET 10 passkeys with `IdentityDbContext`, configure
`IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3` before
creating the database. Failure/validation-only tests do not touch the passkey
table and can miss this; keep one successful fake-`IPasskeyHandler` enrollment
and discoverable-assertion integration test. Validate this lesson again on a
second identity-schema change before proposing promotion into durable repo docs.

## Candidate — First-run authority must not depend on startup-log retrieval

For a self-hosted native-passkey deployment, requiring a nontechnical Owner to
retrieve a short-lived setup URL from container logs creates avoidable expiry,
restart, and lost-cookie failures. Prefer a direct same-origin bootstrap only
while the workspace is unclaimed, keep raw authority server-side, serialize
concurrent attempts, and retain private links for recovery and invitations.
Document network privacy as a deployment precondition instead of pretending the
application can infer LAN topology behind arbitrary proxies. Validate this
lesson on another first-run authority flow before proposing broader promotion.
