# Runtime and Visual Evidence

## Incident

Live operation `op_enroll_7df93232f53d4d44b45f5149fda069b0` recorded:

- USB detected successfully;
- pairing requested once;
- `UsbmuxException` at the lockdown transition;
- installed-app reads succeeded five seconds later, indicating a transient
  pairing-to-lockdown result rather than a durable Trust denial.

## UI

Screenshot: `waiting-for-dragos-390x844.png`.

The 390 × 844 Storybook dialog shows:

- **Waiting for Dragos’s iPhone…** as the primary state;
- one concise cable/unlock instruction;
- Connect/Trust/Ready progress;
- best-effort audio disclosure;
- disabled gray Continue;
- technical details collapsed.

Storybook separately proves the still-waiting attention state and the detected,
accepted state. Audio calls are injected in tests, so listening/detected/
attention cues are deterministic without claiming that a browser actually
played sound.
