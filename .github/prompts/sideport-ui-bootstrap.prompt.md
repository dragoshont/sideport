---
name: "Sideport UI Bootstrap"
description: "Bootstrap or resume Sideport UI design work: research references, update plan/spec, identify backend gaps, and choose the next implementable slice."
agent: "Sideport UI Designer"
tools: [read, search, web, edit, todo]
argument-hint: "Focus area, e.g. onboarding, devices, renewals, diagnostics"
---

Resume Sideport UI work for the focus area I provide.

Read:

- [UI experience plan](../../docs/ui/sideport-ui-experience-plan.md)
- [UI design spec](../../docs/ui/sideport-ui-design-spec.md)
- [Agent and MCP setup](../../docs/ui/agent-and-mcp-setup.md)

Then:

1. Re-check current backend/API seams relevant to the focus area.
2. Search for real product references if needed.
3. Update the spec with any missing flows/states/backend gaps.
4. Propose the next smallest implementation slice.
5. Provide Storybook stories and Playwright screenshot targets for that slice.

Keep the output grounded in Sideport's real constraints: Apple teams, paired devices, up to 3 apps per device, signing expiry, single-flight refresh, anisette trust, and diagnostics.
