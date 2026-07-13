# Tournament of Options

## Signed-in entry point

1. Repeat separate buttons on each page.
2. Add sidebar destinations for device and app setup.
3. Use one persistent labelled **+ Add** menu, plus contextual empty-state and
   detail actions that invoke the same flows.

Decision: option 3. It stays available without expanding navigation, gives the
two intents equal prominence, and matches the inspected Mobbin pattern of a
clear add intent followed by a short choice surface.

## Device trust

1. Keep usbmux auto-pair on ordinary reads.
2. Ask the operator to pair and then separately confirm/add.
3. Make reads non-pairing and reserve pairing for one explicit, durable,
   bounded enrollment operation that observes Trust and accepts automatically.

Decision: option 3. Options 1 and 2 violate consent or add redundant steps.

## Private GitHub access

1. Store a classic personal access token submitted in the browser.
2. Accept arbitrary signed/download URLs.
3. Prefer a selected-repository GitHub App with Metadata/Contents read only;
   permit a deployment-secret reference to a fine-grained read-only token only
   as a bounded interim provider.

Decision: option 3. It is the only option with least-privilege repository
selection and server-side credential custody. No write, workflow, admin, broad
`repo`, arbitrary URL, or token persistence is introduced.

## Delivery shape

1. One large cross-tier rewrite.
2. Mock UI only.
3. Contract-first vertical slices: mock/approve, branch, non-pairing enrollment,
   managed imports, GitHub authorization/import, runtime binding, Apple
   `container`, then full gates.

Decision: option 3. Each slice is independently testable and keeps exactly one
Architrave phase active.
