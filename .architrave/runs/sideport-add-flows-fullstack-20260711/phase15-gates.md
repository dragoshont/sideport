# Phase 15 Gate Evidence

## Packaging

- `docker compose ... config`: PASS with persistent Sideport and anisette
  volumes, usbmux socket, read-only pairing records, managed credential source,
  bounded install settings, read-only root, non-root image, no-new-privileges,
  and all capabilities dropped.
- Apple launcher `sh -n`: PASS.
- Apple launcher `dry-run`: PASS without secret output.
- Apple launcher `check` on this Intel Mac: fails closed because the official
  runtime is absent. Physical Apple Container support is not claimed.
- Dockerfile signer source pinned to an immutable digest.
- CI requests maximum provenance plus SBOM attestations.

## Actual RC image

- Built successfully on the x86-64 homelab Docker host from the current working
  tree as local-only `sideport:phase15-rc`.
- Image runs as UID 1000 with read-only root filesystem and tmpfs `/tmp`.
- Clean named Sideport state volume starts successfully and retains its files
  across container restart.
- `/healthz` returns `{\"ok\":true}` before and after restart.
- Production npm dependency audit: zero vulnerabilities.

## Runtime migration safety

- The RC started successfully from a copied production-state snapshot while
  production was temporarily scaled down under the sole-signer rule.
- Legacy registrations were blocked as `registration-verification-required`
  instead of silently refreshing without V2 device evidence.
- Production `0.1.12` was restored automatically and returned `1/1` ready.

## Repository gates

- Backend checks: PASS; build zero warnings/errors; API 479/479, Orchestrator
  55/55, Developer API 102/102, Devices 65/65, GrandSlam 50/50; Kubernetes 6/6;
  secret scan PASS.
- UI checks: PASS, 86/86.
- Reconcile: transparent not-configured PASS.
- `git diff --check`: PASS.

## Residual limitation

Release publish inside Docker surfaces warnings from the vendored
Netimobiledevice project that the normal solution gate suppresses. They are
upstream nullable/trimming diagnostics outside the Sideport-owned execution
path changed here; the image still publishes successfully. They are recorded,
not represented as a zero-warning Release publish.
