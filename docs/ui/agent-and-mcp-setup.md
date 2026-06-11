# Sideport UI Agent And MCP Setup

This branch configures a repeatable design workflow for Sideport and future UI work. The active baseline is intentionally free/local and GitHub Copilot friendly: no Figma account, no OpenAI/Anthropic keys, no Sentry/Firebase project, and no paid design subscription are required to start.

## Workspace Files Added

- `.vscode/mcp.json` - shared VS Code MCP server config.
- `.github/agents/sideport-ui-research.agent.md` - read-only design research agent.
- `.github/agents/sideport-ui-designer.agent.md` - product/UI spec agent.
- `.github/agents/sideport-ui-implementer.agent.md` - frontend implementation agent.
- `.github/agents/sideport-ui-reviewer.agent.md` - adversarial UI review agent.
- `.github/prompts/sideport-ui-bootstrap.prompt.md` - one-shot prompt to restart the workflow.
- `.github/instructions/sideport-ui.instructions.md` - project-specific UI guidelines.
- `.claude/settings.json` - optional prompt for Claude Code users who later choose Subframe.

## Active MCP Servers In `.vscode/mcp.json`

Configured now because they do not require new paid product accounts:

- `github`: GitHub Copilot MCP endpoint from the VS Code docs. You already have Copilot, so this is the only account assumption.
- `playwright`: browser automation and screenshots for UX verification.
- `storybook`: local Storybook MCP endpoint at `http://localhost:6006/mcp`. It only works after Storybook exists and is running with `@storybook/addon-mcp`.

No `inputs` are configured now, so VS Code should not prompt for Figma, Refero, Sentry, Firebase, OpenAI, or Anthropic secrets.

## Optional Account-Gated Tools

These are useful later, but they are not required for Sideport's first UI pass:

| Tool | Account / paid status | Use only when |
| --- | --- | --- |
| Figma remote MCP | Figma says the remote MCP is available on all seats/plans during the current beta, but its pricing page also lists MCP access under Professional features. Treat it as optional and potentially paid later. | You have real Figma files, want code-to-canvas/canvas-to-code, or want to maintain a design system in Figma. |
| Figma Context / Framelink | Requires a Figma personal access token and access to the file. | You need link-based extraction from specific Figma frames. |
| Refero MCP | Requires Refero sign-in/token; likely paid for serious MCP use. | You want curated real-product screen research inside the agent instead of browsing manually. |
| Subframe MCP / skills | Subframe has a free tier, with paid Pro for unlimited projects/pages/AI usage. | You want a visual design-to-code workflow beyond shadcn/Storybook. |
| Sentry MCP | Requires Sentry. Some AI-powered Sentry tools require OpenAI or Anthropic keys. | Sideport or a future app is actually sending errors to Sentry. |
| Firebase Crashlytics MCP | Requires Firebase CLI auth and an app using Crashlytics. | There is a real iOS app with the Crashlytics SDK. Not relevant today. |

The practical answer: no, you do not need paid accounts for all of this. For now, use Copilot + local Storybook + Playwright + OpenTelemetry instrumentation.

## OpenTelemetry Focus Now

Because there is no iOS app yet, diagnostics should start with Sideport's own operations rather than mobile crash SaaS:

- Instrument Sideport.Api, DeveloperApi, Devices, and Orchestrator with OpenTelemetry traces, metrics, and structured logs.
- Correlate refresh operations with trace IDs: auth, anisette headers, developer-services calls, signing identity preparation, signer process, install, readiness checks.
- Export OTLP to a local/homelab collector, then view it in Grafana/Tempo/Prometheus/Loki or whatever observability stack the homelab settles on.
- Reflect that model in the UI: operation timeline, span durations, failure category, retry action, and raw trace/log links.
- Keep Sentry/Firebase as future integrations only after a real app exists and intentionally embeds their SDKs.

## First-Time Setup Commands

From the worktree:

```sh
cd /Users/dragoshont/Repo/sideport-ui-experience
```

In VS Code:

1. Open `.vscode/mcp.json`.
2. Use the inline code lenses to start/trust servers.
3. Confirm `github` and `playwright` tools appear in Chat -> Configure Tools.
4. Ignore `storybook` until the React frontend and Storybook have been created.

Optional external setup:

```sh
npx skills add https://github.com/referodesign/refero_skill
npx skills add SubframeApp/subframe
```

Skip these optional commands unless you deliberately decide to try Refero or Subframe.

For Claude Code users, the committed `.claude/settings.json` advertises the Subframe plugin. In Claude Code:

```text
/plugin
```

Then enable the Subframe marketplace/plugin when prompted.

After the React frontend exists:

```sh
npx storybook@latest init
npx storybook add @storybook/addon-mcp
```

Then start Storybook before starting the `storybook` MCP server:

```sh
npm run storybook
```

If your Storybook runs on a different port, update `.vscode/mcp.json` from `http://localhost:6006/mcp` to the actual port.

## Recommended Agent Flow

1. Run `Sideport UI Research` to gather references and produce patterns.
2. Run `Sideport UI Designer` to update the spec and define the next slice.
3. Run `Sideport UI Implementer` to scaffold/build the selected slice.
4. Run `Sideport UI Reviewer` before merging.

## External Agent Setup

The user-level reusable agent lives outside this repository at:

`~/Library/Application Support/Code/User/prompts/agents/ux-product-design-adversary.agent.md`

Use it in other repositories when you want the same adversarial product/design analysis that produced this plan.
