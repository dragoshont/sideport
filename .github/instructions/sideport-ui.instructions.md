---
description: "Use when designing or implementing the Sideport web admin UI, device inventory, app signing, refresh queue, onboarding, diagnostics, React, shadcn, Tailwind, Storybook, or Playwright screens."
applyTo: ["src/Sideport.Admin/**", "web/**", "frontend/**", "admin/**", "**/*.stories.*", "**/*.tsx", "**/*.css"]
---

# Sideport UI Guidelines

- Treat Sideport as an Apple-device operations console, not a generic SaaS dashboard.
- Prefer dense but readable lists, tables, queues, timelines, and diagnostics over decorative cards.
- Always model empty, loading, warning, blocked, failed, success, and mobile states.
- Make scarce Apple/free-tier limits visible before the user hits them: 3 app slots per device, signing identity reuse, single-flight signing, 2FA, certificate/profile expiry.
- Do not auto-trigger refresh/sign/install from route load. User-triggered actions need visible progress and failure reasons.
- Use React + shadcn/Radix/Tailwind conventions unless this repo later establishes a different frontend stack.
- Use TanStack Table for desktop inventory behavior and card/list layouts for mobile.
- Build Storybook stories for hard-to-reach states before wiring live data.
- Verify important screens with Playwright screenshots at desktop and phone widths.
- Keep copy plain and operational. Say exactly what is blocked and what action fixes it.
