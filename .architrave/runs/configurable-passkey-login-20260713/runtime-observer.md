# Runtime observer

Phase 1 made no production mutation.

## Phase 2 evidence

- Sideport PR #8 merged at commit `29b60c5037c60115f0f96193c9a51037797a5c3e`.
- Release `v0.2.3` CI passed Linux, macOS, admin UI, screenshot, and container
  publication jobs.
- GHCR index digest:
  `sha256:f89d709432a0c2cb766fb4a241107efb728531256db659eec1071de036b4efc4`.
- Homelab GitOps revision `2dbc78bfda7a764c718e06c1e00701c2d016f708`
  pinned that digest and configured provider ID/labels.
- Flux applied the revision. The deployment rolled out with one replica, pod
  `2/2`, zero restarts, `/healthz` healthy, and `/readyz` ready.
- Public authentication options reported OIDC enabled, enrollment enabled,
  provider `authentik`, configured provider/login labels, passkey owner
  `authentik`, and official Sign in with Apple false.
- A new bootstrap Owner claim exchanged from a URL fragment into the handoff
  cookie, scrubbed the fragment, showed the authenticated account preview, and
  required explicit **Finish owner setup** acceptance.
- The resulting workspace contains exactly one active Owner and no invitations.
  Onboarding now shows the live managed Apple credential form as the next action.

No API token, claim token, OIDC issuer/subject, Apple credential, or Authentik
service token is stored in this artifact.
