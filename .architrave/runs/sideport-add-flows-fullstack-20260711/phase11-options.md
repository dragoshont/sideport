# Phase 11 Options

| Option | Product fit | Risk | Decision |
| --- | --- | --- | --- |
| Keep the legacy router and relabel destinations | Poor: overlapping responsibilities and permanent onboarding remain | High UX drift and continued dead surfaces | Rejected |
| Add a second canonical runtime beside the old console | Poor: creates parallel components and inconsistent authorization | High maintenance and capability-honesty risk | Rejected |
| Collapse the existing runtime in place, reuse live components and flows, then delete unreachable UI | Best fit with approved Storybook and YAGNI | Moderate migration risk covered by route/capability/responsive tests | Selected |

The selected option preserves the live API bindings and add/install recovery
logic while replacing only information architecture, composition, role labels,
and capability-aware visibility. It removes rather than wraps obsolete UI.
