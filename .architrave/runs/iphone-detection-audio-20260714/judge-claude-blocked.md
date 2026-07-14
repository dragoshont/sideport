# Claude-family Judge

The required launcher was invoked, but the local Claude CLI reported:

`Not logged in · Please run /login`

Authentication was later restored, but the bounded read-only judge call failed
before reading the repository with `API Error: 400 No connected db.` No verdict
is fabricated. The Copilot-family Architrave judge was rerun against the final
diff and returned PASS; the separate Claude-family verdict remains unavailable
until its provider connection is repaired or a human explicitly accepts the
alternative independent review evidence.
