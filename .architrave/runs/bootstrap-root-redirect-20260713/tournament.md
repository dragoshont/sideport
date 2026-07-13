# Tournament of Options

## Option A — Client-side redirect

Rejected: it flashes or loads the wrong shell and still triggers the server OIDC
gate first.

## Option B — Server middleware before OIDC challenge

Selected: one bounded store read on root navigation, a local temporary redirect,
and no new API or client state.

## Option C — Redirect every unknown route

Rejected: it would hide legitimate navigation and error behavior.

## Decision Matrix

| Option | Correct before OIDC | Scope | Loop risk | Decision |
|---|---|---|---|---|
| A | no | UI only | medium | reject |
| B | yes | root only | low | select |
| C | yes | broad | high | reject |

## Winner

Option B.
