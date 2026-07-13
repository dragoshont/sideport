# Phase 12 Options

| Option | Result | Decision |
| --- | --- | --- |
| Let normal install revoke an existing certificate | Hidden destructive mutation and free-tier churn | Rejected |
| Build a second signer/credential subsystem | Parallel architecture and ambiguous authority | Rejected |
| Extend existing portal, signer provider, registry, operation store, and Personal Apple access with exact cutover contracts | Smallest coherent implementation; same lock and persistence boundaries | Selected |

The implementation proceeds in two bounded slices: active-account returned-team
and identity maintenance first; different-account candidate credential/2FA and
atomic credential+registration cutover second.
