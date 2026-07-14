# Runtime / Visual Evidence

- Rendered the updated canonical Owner setup story at 390 × 844.
- Screenshot: `owner-setup-390x844.png`.
- Visible content is limited to First setup, Name, Email, and one disabled until
  valid **Create passkey** action.
- No startup log, setup token, copied URL, Authentik, API key, or recovery-key
  instruction appears in the native first-run mock.
- The Owner setup story uses labeled Name/Email inputs, a native button disabled
  until both fields are valid, and role-based alert/status semantics. The full
  Storybook interaction/accessibility suite passed. Focused story assertions
  verify labeled textboxes, disabled/enabled primary-action semantics, alert
  announcements for missing private links, and absence of hidden alternative
  actions in blocked states.
- Production remains on the previous image and is intentionally not claimed as
  fixed until the reviewed patch is merged and reconciled through GitOps.

The repository's Architrave config currently has no `tokens` or `tokenBuild`
pointer, as also recorded in `.architrave/learning/repo-profile.md`; therefore
`gates/reconcile.sh` performed its designed documented skip. No new CSS values or
parallel component system were introduced; the updated mock and runtime page
reuse the existing `spc-invitation`, identity-form, button, alert, status, and
typography classes without adding literal colors, spacing, or type values.

Sibling-state sweep: direct native setup, claimed native workspace, native Owner
recovery, native invitation, missing invitation link, generic OIDC enrollment,
generic OIDC existing-account login, and OIDC without a private Owner link all
rendered through the same `WorkspaceHandoff` component and passed Storybook.
