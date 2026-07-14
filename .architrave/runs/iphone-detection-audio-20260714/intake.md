# Intake — Personal iPhone Detection Feedback

## Problem

The live enrollment detected USB, requested Trust, then a transient
`UsbmuxException` moved the operation to a generic red recovery error. The UI
looked hung, required a manual **Check Trust and continue** action, had no sound
feedback, and did not identify whose iPhone Sideport was waiting for.

## Approved outcome

- One start action opens a calm, personalized **Waiting for Dragos’s iPhone…**
  state.
- Best-effort browser audio distinguishes listening, detected/trusted, and
  still-not-detected/attention states.
- After pairing is requested, Sideport keeps checking Trust automatically within
  the original five-minute operation and never repeats pairing.
- Continue remains disabled until the server accepts the iPhone.
- Errors name the observable remedy instead of exposing “lockdown” terminology.

## Grounding

- Live operation `op_enroll_7df93232f53d4d44b45f5149fda069b0`.
- `docs/sideport-backend-contract.md` device enrollment contract.
- `docs/ui/sideport-ui-design-spec.md` one-cable assistant rules.
- `src/Sideport.Admin/src/add-flows/AddFlows.tsx` and Storybook stories.

## Assumptions

- Audio is best effort and never proof of device state.
- The browser may block audio; visual and screen-reader status remains complete.
- The deployment remains single-replica and the existing durable operation owns
  the five-minute enrollment boundary.

## Blocking questions

None. The user explicitly requested the three sound outcomes and personalized
waiting message.
