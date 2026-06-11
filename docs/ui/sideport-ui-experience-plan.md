# Sideport UI Experience Plan

Branch: `feature/sideport-ui-experience-plan`

Worktree: `/Users/dragoshont/Repo/sideport-ui-experience`

Date: 2026-06-07

## Thesis

Sideport should not become a generic SaaS dashboard. It should feel like a small, careful Apple-device operations console: part Apple Business Manager, part Jamf Now/SimpleMDM, part Tailscale device inventory, part OpenTelemetry operations console.

The product promise is simple: open Sideport from a desktop or phone and immediately know:

- Which devices are reachable.
- Which apps are installed on each device.
- Which signing profiles expire soon.
- Which refreshes are blocked, queued, or failed.
- What action will make a device/app healthy again.

## Current Backend Surface

The current .NET API already supports the first useful console slice:

| Endpoint | Use in UI | Notes |
| --- | --- | --- |
| `GET /healthz` | liveness badge | Public probe. |
| `GET /readyz` | system readiness panel | Checks anisette and signer binary. |
| `GET /api/anisette/info` | setup/trust diagnostics | Shows anisette client identity. |
| `GET /api/devices` | device inventory | Current reachable devices only; no persisted last-seen yet. |
| `GET /api/apps` | app registry + refresh state | Includes expiry, last success, last error. |
| `POST /api/apps` | register app | Needs frontend IPA-path UX. |
| `DELETE /api/apps/{udid}/{bundleId}` | remove app registration | Does not uninstall yet. |
| `POST /api/apps/{udid}/{bundleId}/refresh` | manual refresh | Must stay guarded by bearer auth and single-flight signing. |

## Design Research Inputs

Use these as active references, not decoration:

- Refero: optional real product screens and flows for onboarding, settings, dashboards, permissions, empty states, and error states. Useful if you choose to pay/sign in later; not required for the baseline.
- Mobbin: human browsing source for real iOS and web flows. Use for pattern moodboards, not editable Figma source.
- Subframe: optional design-to-code workflow for polished React pages. The free tier may be enough for experiments; shadcn/Storybook remains the no-account baseline.
- Storybook AI and Chromatic: make agents reuse existing UI components, write stories for edge states, and catch visual/a11y regressions.
- Jamf Now, SimpleMDM, FleetDM: device inventory and operations language.
- Tailscale: device status, last-active, approval, owner, network reachability patterns.
- OpenTelemetry, Grafana, Tempo, Prometheus, and Loki: trace-first diagnostics, service health, latency, queue depth, failure categories, and log correlation.
- Apple HIG: hierarchy, consistency, restrained typography, predictable controls, and platform-familiar language.

## Product Principles

1. Operational clarity beats novelty.
2. Every dashboard number must map to an action or a risk.
3. App signing limits are part of the UX, not an error message after the fact.
4. Single-flight signing must be visible when it matters: queued, running, blocked.
5. Mobile is not a shrunk desktop table. It is a task surface for checking health and triggering one action.
6. Empty states must teach the next step without marketing copy.
7. Blocked states must say exactly what is blocking the user: device offline, 2FA required, app slots full, signer unavailable, anisette unprovisioned, cert cap, profile expired.
8. Avoid generic analytics cards. Prefer device/app lists, queues, timelines, and diagnostics.

## Recommended Frontend Stack

- Vite + React + TypeScript.
- shadcn/ui + Radix primitives.
- Tailwind CSS with a restrained system theme.
- TanStack Query for API state.
- TanStack Table for desktop inventory tables.
- Storybook for UI states before wiring everything to live data.
- Playwright for desktop and phone screenshots.
- Chromatic later for visual/a11y review if the UI grows beyond local use.

Keep Sideport.Api as the backend. Build the admin as a static SPA served either by the API image or a sibling container/ingress after the first slice is proven.

## Phased Work Plan

### P0 - Workbench Setup

Status: started in this branch.

Deliverables:

- Workspace MCP config in `.vscode/mcp.json` with only no-new-paid-account servers active.
- Workspace custom agents in `.github/agents`.
- Reusable prompt in `.github/prompts`.
- Project UI instruction file in `.github/instructions`.
- Claude/Subframe plugin hint in `.claude/settings.json`.
- This plan plus the design spec.

### P1 - Product Model Gap Pass

Goal: define the API/read models the UI needs before building screens.

Backend gaps to capture:

- Persistent device inventory with `lastSeenAt`, `lastConnection`, `lastKnownName`, `lastKnownOsVersion`, and `lastKnownProductType`.
- Installed app snapshot per device, including signature expiry and slot count.
- Refresh event history, not only last state.
- Signing queue state: waiting, running, succeeded, failed, canceled.
- Apple team/user model for multi-team accounts.
- Sideport user/role model, even if v1 delegates auth to reverse proxy.
- OpenTelemetry model for Sideport operations: trace ID, operation ID, spans, logs, metrics, and failure categories.
- Diagnostics model: invalid signature, install failure, launch-check failure, device unreachable, anisette error, Apple auth/developer-services error, signer error.

Output:

- `docs/ui/sideport-ui-data-contract.md` or an OpenAPI sketch.
- Issues/tasks for missing backend endpoints.

### P2 - Frontend Scaffold

Goal: create a minimal React app inside the Sideport repo with mocks first.

Recommended path:

- Add `src/Sideport.Admin` or `web/admin` depending on final repo convention.
- Use Vite React TypeScript.
- Add Tailwind, shadcn/ui, TanStack Query, TanStack Router or React Router, TanStack Table.
- Add Storybook immediately.
- Create mock fixtures for devices/apps/teams/diagnostics.

Do not wire live refresh actions until the screens and states are reviewed.

### P3 - Core Flow Prototypes

Build these as route-level mock screens and Storybook stories:

1. First-run onboarding.
2. Dashboard / device inventory.
3. Device detail with app slots.
4. Add app to device.
5. Renewal queue.
6. Diagnostics / app will not launch.
7. Users and teams.

Each route needs desktop and phone screenshots.

### P4 - Live API Integration

Wire the frontend to current endpoints:

- `/readyz`
- `/api/anisette/info`
- `/api/devices`
- `/api/apps`
- `/api/apps/{udid}/{bundleId}/refresh`

Use TanStack Query, optimistic UI only for non-dangerous registry edits, and explicit confirmation for refresh/install actions.

### P5 - Missing Backend Slices

Implement backend support only after the UI proves what it needs:

- Device observation store.
- Refresh event store.
- Installed app snapshot endpoint.
- Teams endpoint.
- Diagnostics endpoint backed by OpenTelemetry trace/log/event data.
- Optional crash integration endpoint only after a real app exists and intentionally emits crash data.

### P6 - QA and Review Loop

Every route must have:

- Empty state.
- Loading state.
- Healthy state.
- Warning state.
- Blocked state.
- Failed state.
- Mobile layout.
- Keyboard accessible controls.

Automate:

- Storybook test runner.
- Playwright screenshot checks.
- Axe/a11y where practical.
- Visual review via Chromatic if the project leaves personal/homelab scope.

## Flow Capture Checklist

Use no-account references first, and Refero/Subframe only if you later decide their accounts are worth it, to collect patterns for these exact flows before implementing final UI:

- Onboarding: account setup, team selection, first device, first app.
- Multi-user: invite user, pending invite, role selection, owner transfer / emergency owner.
- Multi-team: choose Apple team, show active team, switch team, team unavailable.
- Device inventory: last seen, connection type, owner, app slots, health filter.
- Device detail: apps, signing expiry, network diagnostics, activity timeline.
- App management: inspect IPA, register app, refresh, remove registration.
- Certificate renewal: queue, due soon, blocked by 2FA, cert cap, signer unavailable.
- Diagnostics: grouped Sideport operation failures, invalid signature, install failure, launch-check failure, anisette/developer-services/signing errors, trace/log drilldown.

## Initial Prompt For Research Agents

```text
Find real product references for a Sideport admin console. Sideport manages Apple Developer teams, paired iPhones, and up to 3 sideloaded apps per device. It tracks device reachability, last seen, signing expiry, refresh queue, install status, and OpenTelemetry-backed operation diagnostics. Research Apple Business Manager, Jamf Now, SimpleMDM, FleetDM, Tailscale, Grafana, Tempo, Prometheus, Loki, and high-quality mobile admin apps. Return reusable patterns, not screenshots only.
```

## Initial Prompt For Design Agents

```text
Design Sideport as a calm Apple-device operations console, not a generic SaaS dashboard. Desktop must support dense inventory scanning; mobile must support health checks and one decisive action. Required flows: first-run onboarding, multi-team/multi-user admin, device inventory, device detail with three app slots, add IPA, refresh queue, diagnostics. Use shadcn/Radix/Tailwind patterns and prepare states for Storybook.
```

## Definition Of Done For A First Useful UI

- New admin can complete first-run setup in a prototype.
- Device inventory shows reachable devices and registered apps.
- Device detail explains app slots and expiry without opening logs.
- Manual refresh flow shows running, queued, success, failure, and 2FA-required states.
- Phone layout is usable without horizontal scrolling.
- Storybook includes at least 30 states across routes/components.
- Playwright screenshots cover desktop and mobile for dashboard, device detail, add app, and diagnostics.
