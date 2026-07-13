# Phase 15 Intake — Fresh Docker and Apple Container Paths

## Reproduced packaging gaps

- The checked-in Compose file overrode the signer baked into the image with a
  missing host directory, persisted anisette but not Sideport state, omitted the
  usbmux/pairing mounts, and used an obsolete anisette configuration key.
- The Dockerfile used a floating signer source image.
- CI explicitly disabled SBOM and provenance despite the immutable-release gate.
- No official Apple Container launcher existed.
- Install timeout settings existed in code but could not be configured by a
  deployment, preventing bounded acceptance drills.

## Scope

- Repair Compose as a real fresh-install source of truth.
- Add a secret-free env example and keep actual env files ignored.
- Add the smallest official Apple Container 1.1+ launcher with check/dry-run,
  persistent volumes, explicit network, Rosetta amd64 request, and usbmux socket.
- Bind the existing install timeout/grace options to configuration.
- Pin the signer-stage image by digest and enable release attestations.
- Build/run the actual RC image on an isolated Docker host and prove non-root,
  read-only-root, clean-volume startup, and restart persistence.

## Capability boundary

This Mac is Intel and has no Apple `container` CLI. The launcher is syntax and
dry-run validated and fails closed when the runtime is absent, but physical
Apple Container device installation remains explicitly experimental and is not
claimed production-supported on this host.
