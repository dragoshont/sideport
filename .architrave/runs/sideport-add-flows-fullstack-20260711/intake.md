# Intake

## Objective

Extend the approved six-step first-run setup into a consistent signed-in
experience, then implement the truthful runtime seams so an authenticated owner
can add another iPhone or IPA at any time.

## Human authorization

On 2026-07-11 the user asked to audit the remaining screens, implement the
mockups, create a `codex/` branch after the screens are complete, and then
implement the Sideport changes autonomously. This supersedes the earlier
planning-only boundary; it does not authorize live deployment, secret access,
or infrastructure apply.

## Acceptance criteria

1. One persistent, keyboard-accessible **Add** entry point offers **Add iPhone**
   and **Add app** from every signed-in screen.
2. Add iPhone is one explicit five-minute session: USB, unlock, Trust on the
   phone, non-pairing verification, then automatic acceptance. Passive reads
   never pair or accept a device.
3. Add app supports a browser IPA upload, a confined configured server root,
   public GitHub release assets, and private selected-repository access.
4. Private GitHub access is least privilege: Metadata read and Contents read,
   no write access, no broad `repo` token, and no browser/catalog/log custody of
   credentials.
5. Existing Apple/onboarding safety, paired-Wi-Fi refresh with USB fallback,
   capability truth, recovery, and accessibility remain intact.
6. Apple `container` support is implemented as a secret-free, plan-only
   official-CLI path and remains experimental until physical-device acceptance.
7. All repo gates and independent semantic review pass; physical USB/Wi-Fi
   evidence is reported honestly if unavailable locally.

## Grounding

- `AGENTS.md` and `architrave.config.json`
- `docs/sideport-backend-contract.md`
- `docs/ui/sideport-ui-design-spec.md`
- `docs/ui/sideport-ui-data-contract.md`
- `docs/ui/sideport-onboarding-implementation-plan.md`
- Current Admin shell, device backend/inventory, operation store, catalog, API,
  deployment, and test code
- Mobbin Google Home Adding a device flow, Linear Import & export, and
  ElevenLabs import empty state, inspected on 2026-07-11

## Preserved state

The only registered worktree is `/Users/dragoshont/Repo/sideport`. It began
dirty on `main`; every pre-existing modification and unrelated untracked file
is preserved. Nothing is reset, staged, committed, deployed, or applied during
intake.
