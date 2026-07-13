# Phase 7 Approved Deletion And Merge Map

This map is the proposed Phase 11 runtime deletion boundary. Phase 7 does not
delete production components, routes, tests, or API behavior.

| Current surface | Canonical destination | Phase 11 action |
| --- | --- | --- |
| `overview` | Home | Rename and keep only household health, attention, devices, and recent activity. |
| `catalog` + `install-app` | Apps | Merge library, search, provenance, app detail, and contextual install. Remove permanent Install App route. |
| `devices` + `device-detail` | Devices | Keep list/detail and invoke the one shared add-iPhone assistant. |
| `users` + workspace half of Teams | Family | Replace fictional roles/capabilities with Owner and Family membership. |
| `operations` + `diagnostics` | Activity | Merge plain-language timeline and failures; put traces/raw logs behind technical disclosure. |
| `apple-access` + Apple half of Teams + Settings | Settings | Merge into owner-only Signing plus account, refresh, setup recovery, and technical details. |
| `renewals` | Home + Apps | Delete route after due-soon/blocked states move to the relevant app/device context. |
| `onboarding` | Outside shell | Render only before the durable completion receipt. Keep recovery under Settings; delete permanent navigation route. |

## Onboarding implementation disposition

- `RuntimeFirstRunOnboarding.tsx`: retain as the runtime workflow/evidence
  source, then rebind its behavior to the approved assistant presentation.
- `FirstRunOnboardingPrototype.tsx`, its scenarios, and its stories: retain as
  the fixture/state oracle until equivalent canonical stories cover recovery,
  keyboard, focus, reduced motion, reflow, and capability-truth states; then
  delete.
- Dead `OnboardingPage` and its legacy helpers in `App.tsx`: delete only after
  the canonical runtime binding passes.
- `AddIPhoneDialog`: retain durable polling/resume/recovery behavior; replace
  duplicate presentation with the shared assistant only after runtime tests
  cover resume and recovery.

## Coupling hazards to repair during binding

- Replace literal `onboarding` route checks used by app-return behavior with a
  semantic setup context.
- Do not conditionally mount hooks or the durable add-iPhone resume boundary.
- Gate shell entry on the immutable completion receipt and matching verified
  operation, never only `readyNow`.
- Port old route and screenshot assertions only in Phase 11, after visual
  approval; Phase 7 leaves runtime tests untouched.

## Approval checklist

- [x] Exactly Home, Apps, Devices, Family, Activity, Settings.
- [x] Setup outside the signed-in shell.
- [x] Owner and Family content boundaries are visible.
- [x] Add/Search are global and role-aware.
- [x] One canonical first-run/add-iPhone assistant.
- [x] Desktop, 390px mobile, keyboard, 320px reflow, and accessibility checks pass.
- [ ] Human visual confirmation recorded before runtime binding.
