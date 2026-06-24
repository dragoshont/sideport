# Runtime Evidence

GitHub issue: https://github.com/dragoshont/sideport/issues/2

Key observed facts:
- sideport pod `default/sideport-789c7d689f-sqpwz` at ~999m CPU.
- one `.NET TP Worker` thread near 99% CPU.
- installed-apps endpoint called 30 times in 30m.
- 1110 provisioning-profile shape warnings in 30m.
- live device connection to `192.168.1.153:62078`.
- scaled deployment to 0; no pods; temp fell to ~65C.
