# Phase 7 Product-Shell Research

Date: 2026-07-12

## Grounding order

1. `architrave.config.json` Storybook source and UI spec.
2. Existing Sideport setup, add-iPhone, add-app, status, pipeline, and fixture
   behavior.
3. Web platform knowledge pack and WCAG 2.2 requirements.
4. Mobbin references as inspiration only; no external visual is copied.

## Mobbin evidence inspected

- [Alan — Inviting a user](https://mobbin.com/flows/1a1edfa6-6c51-4b11-9b6b-3e47016e463a):
  the inspected iOS flow keeps the family list compact, gives one visible
  **Add a family member** action, explains the invitation before the email
  field, and uses a single disabled-until-valid Invite action.
- [Philips Hue — Adding a security device](https://mobbin.com/flows/8d39b393-01f5-43dd-a59f-f9d6eb1b3a9b):
  the inspected iOS flow presents one question or outcome per screen, uses a
  strong product/device visual, and ends with an unmistakable success state.
- [Wise — Devices](https://mobbin.com/screens/a2c2af3b-ff20-4541-bad6-dc05c92ec8d5):
  the inspected web screen uses a quiet sidebar, generous whitespace, a short
  device list, and disclosure rather than dense dashboard cards.
- [komoot — Connections](https://mobbin.com/screens/73912dfd-65da-415d-8f46-11c227cb9694):
  the inspected web screen separates navigation from one focused content
  surface and keeps secondary integration detail below the primary action.

## Applied deltas

- Exactly six signed-in destinations; setup is not a destination.
- One primary action per assistant state.
- Invitation copy explains outcome before asking for an email.
- Family login says **passkey** and names familiar device prompts; it does not
  claim official Sign in with Apple.
- The cable assistant performs detection, pairing, Trust verification, and
  Sideport acceptance automatically after one start action.
- App choice goes directly to one Install action; automatic refresh is the
  default, not another setup choice.
- Verified completion is visually dominant and says **Installed — you can
  unplug**. Wi-Fi refresh copy retains the cable fallback.

## Capability truth

These are deterministic Storybook fixtures. Family authorization, Authentik
invitation/passkey enrollment, completion audio, and repaired Wi-Fi bulk
transfer remain gated by Phases 8–14 and are labelled proposed where needed.
