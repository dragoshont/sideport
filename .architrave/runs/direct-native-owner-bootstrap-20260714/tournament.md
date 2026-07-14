# Tournament of Options

| Option | Usability | Security | Recovery | Low complexity | Decision |
| --- | ---: | ---: | ---: | ---: | --- |
| Keep startup-log link and improve copy | 1 | 4 | 1 | 2 | Reject: low implementation cost and narrow authority, but preserves lost/expired-link recovery failures. |
| Store a reusable setup token in the browser/server UI | 3 | 1 | 3 | 3 | Reject: easier retry, but expands durable plaintext authority and browser exposure. |
| First-visitor native bootstrap while unclaimed | 5 | 4 | 5 | 4 | Select: removes user ceremony, keeps authority server-side, and reuses existing claim/handoff storage with a small concurrency gate. |

The selected design creates and exchanges a short-lived System-owned bootstrap
claim inside the server only after an exact-origin WebAuthn options request. A
retry may revoke only a pending System-created bootstrap claim while the
workspace remains unclaimed. It cannot replace a recovery-bearer claim or act on
an active workspace.

Deferring was rejected because production acceptance had already reproduced the
failure on an empty deployment and the user explicitly approved direct native
bootstrap. Scores are ordinal: 1 is worst and 5 is best for the named axis;
complexity scores reward the smaller implementation.
