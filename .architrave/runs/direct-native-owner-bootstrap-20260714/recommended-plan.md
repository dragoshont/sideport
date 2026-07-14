# Recommended Plan

1. Remove startup-log setup-link generation and documentation.
2. Add a non-secret, no-store native bootstrap status endpoint.
3. Make passkey options atomically obtain an opaque bootstrap handoff, replacing
   only an abandoned System-owned bootstrap attempt.
4. Drive the Owner page from `available`, `private-link-required`, or `claimed`.
5. Align the canonical Storybook setup mock with the direct Name/Email/passkey
   experience.
6. Prove retry, recovery-link preservation, active-workspace denial, UI states,
   accessibility, and full regression gates.
7. Run independent semantic review, merge, deploy by digest through GitOps, and
   verify a truly fresh production visit.
