# Phase 1 Deterministic Evidence

Date: 2026-07-11

## Design research

- Inspected [Google Home — Adding a device](https://mobbin.com/flows/501ad914-5c92-4b3f-bf03-82b23ee2b673): one prominent Add device intent, then a short choice surface.
- Inspected [Linear — Import & export](https://mobbin.com/screens/13eef89b-e17e-4ee7-bde4-8d3afa7455d4): import sources are plain rows with progressive disclosure.
- Inspected [ElevenLabs — import empty state](https://mobbin.com/screens/571674c7-1bf7-4781-930c-dfd1a8d87b84): the page and empty state repeat one concrete import action.
- Sideport translates these patterns through its existing web typography,
  spacing, Radix dialogs/popovers, and neutral/accent palette; it does not copy
  brand assets or imitate native macOS chrome.

## Results

- `npm --prefix src/Sideport.Admin run build`: PASS.
- `npm --prefix src/Sideport.Admin run lint`: PASS.
- Focused onboarding stories: 74/74 PASS.
- Focused Admin shell stories: 23/23 PASS.
- `./gates/checks.sh`: PASS; four files, 118/118 Storybook tests.
- `npm --prefix src/Sideport.Admin run build-storybook`: PASS.
- `./gates/reconcile.sh`: PASS (transparent skip; no token build is configured).
- `git diff --check`: PASS.

## Browser QA

- Direct Storybook iframe verified at desktop width for Overview, global Add,
  Add app sources, selected private GitHub permissions, Add iPhone, automatic
  acceptance, and the fresh-install start.
- At a 390 × 844 CSS-pixel viewport, document and body widths remained 390,
  with no horizontal overflow. Visible setup actions measured 44 CSS pixels.
- The Add iPhone browser check found one Connect iPhone action and zero Pair
  actions; the mock automatically reached **iPhone added to Sideport**.

## Notes

The first stable full run retains one pre-existing React `act` warning in the
long keyboard-only onboarding journey. It does not fail or mask a test. The
Architrave kit reports that the copied gate assets are older than the installed
plugin; this is an advisory maintenance note and was not auto-updated during
the product change.
