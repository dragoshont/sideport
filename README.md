# Sideport

> A self-hosted, multiplatform backend that **signs and refreshes sideloaded
> iOS apps**, exposing a stable API a UI can be built on. The .NET "brain" is
> the product; anisette and the signer are two small sidecars behind interfaces.

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Backend: .NET 10 (LTS)](https://img.shields.io/badge/backend-.NET%2010%20LTS-512BD4.svg)](https://dotnet.microsoft.com/)
[![Image: multi-arch Linux OCI](https://img.shields.io/badge/image-linux%2Famd64%20%2B%20arm64-2496ED.svg)](deploy/Dockerfile)

> [!NOTE]
> **Status: scaffold.** This repo is project skeleton + interface seams only.
> The design and the import/port/rewrite calls live in the adversarial design
> doc: [`homelab/docs/sideport-dotnet-consolidation.md`](https://github.com/dragoshont/homelab/blob/main/docs/sideport-dotnet-consolidation.md).
> That doc is the source of truth; this README summarizes it.

---

## What this is

Sideport authenticates an Apple ID (GrandSlam), manages developer resources
(certs / app-IDs / profiles / devices), signs an `.ipa`, installs it, and
**re-signs on a schedule** before the 7-day free cert expires — all behind a
stable HTTP API so a UI is a thin client, never coupled to the plumbing.

It replaces today's six-native-dependency chain (AltServer-Linux + AltSign +
corecrypto→libgsa + zsign + the libimobiledevice family + netmuxd) with **one
pure-.NET service** plus **exactly two non-managed sidecars**.

> [!IMPORTANT]
> Sideport is for sideloading apps **you are entitled to install** onto
> **devices you own or administer**, using **your own** Apple ID. It automates
> the same free-provisioning flow Xcode uses. It is not a piracy tool and ships
> no copyrighted apps.

## Multiplatform — the honest shape

"One managed binary everywhere" is **false**: anisette depends on Apple's
closed, per-architecture Android libraries, and device USB/Wi-Fi transport is
OS-specific. The real multiplatform unit is therefore a **multi-arch Linux OCI
image** (`linux/amd64` + `linux/arm64`) — the .NET brain plus an anisette
sidecar plus a signer binary. **Multiplatform = any host with a container
runtime.** Native desktop (`.exe`/`.app`) is explicitly out of v1 scope.

## Architecture

```
            ┌──────────────────────── Multi-arch Linux OCI image ───────────────────────┐
 thin UI ─▶ │  Sideport.Api  ─▶  orchestrator  ─▶  GrandSlam auth ─┐                     │
            │  (REST/SSE)        (single-flight)   (BCL crypto)     ├─▶ gsa.apple.com     │
            │                          │           dev-API client ──┴─▶ developerservices2│
            │                          ├─▶ IPA repack ─▶ ISigner ──────▶ [signer sidecar] │
            │                          └─▶ IDeviceController ──────────▶ iPhone (USB/WiFi)│
            │                              IAnisetteProvider ──HTTP──▶ [anisette sidecar]  │
            └────────────────────────────────────────────────────────────────────────────┘
```

Four stable seams keep the UI and plumbing decoupled: `IAnisetteProvider`,
`ISigner`, `IDeviceController`, and `IAppleDeveloperPortal` (the Apple auth +
dev-API — the only code we truly own and write).

## Key decisions (from the adversarial pass)

- **Rewrite GrandSlam crypto in pure managed BCL** (`System.Numerics.BigInteger`
  + `System.Security.Cryptography`). No native libgsa at runtime — it forces a
  5-RID build matrix. `libgsa` is demoted to a **test oracle** (its SRP vectors
  are proven byte-for-byte).
- **Clean-room the Apple endpoints**, never port AltSign/AltServer source —
  they are **AGPL-3.0**, which would relicense the whole backend + UI.
- **Import what's permissive:** `Netimobiledevice` (MIT, vendored at a pinned
  SHA) for device transport; the signer (`zsign` MIT now → `rcodesign`
  Apache-2.0 later) as a sidecar binary.
- **Keep anisette at HTTP arm's length** — it's irreducible (Apple's closed
  Android `.so`) and the boundary doubles as a license firewall.

These choices keep Sideport **permissively (MIT) licensed**.

## Layout

```
src/
  Sideport.Api           ASP.NET Core 10 — public API + DI wiring
  Sideport.Core          the four seams + domain records (no dependencies)
  Sideport.GrandSlam     clean-room BCL crypto + auth (libgsa = test oracle)
  Sideport.DeveloperApi  clean-room dev-API, anisette HTTP provider, process signer
  Sideport.Devices       vendored Netimobiledevice controller (pending)
tests/
  Sideport.GrandSlam.Tests   crypto unit tests + libgsa golden-vector oracle (phase 2)
deploy/
  Dockerfile             multi-arch Linux image for the .NET brain
  compose.yaml           brain + anisette + signer sidecars
```

## Build & run

```bash
dotnet build                 # restores + builds Sideport.slnx
dotnet test                  # GrandSlam crypto tests
dotnet run --project src/Sideport.Api

# full stack (brain + anisette + signer sidecars):
docker compose -f deploy/compose.yaml up --build
```

> [!WARNING]
> The anisette ADI volume is effectively an Apple **trusted-device
> registration**. Losing it burns a trusted-device slot and forces re-2FA.
> Back up `anisette-data` and pin the anisette image by digest in production.

## Roadmap (design §8)

| Phase | Work | Size |
|---|---|---|
| 0 | Skeleton + seams (DI, anisette HTTP, process signer, single-flight lock) | **done (this scaffold)** |
| 1 | Device plane: vendor Netimobiledevice; discover/pair/list/install; resolve Wi-Fi mDNS gap | S–M |
| 2 | **Auth plane** — managed SRP-6a + s2k + anisette headers, validated vs libgsa oracle | **M (keystone)** |
| 3 | **Dev-API plane** — teams, device add, CSR→cert, app-ID + profile (clean-room) | M |
| 4 | Refresh loop — auth → ensure cert/profile → re-sign → install; countdowns, SSE logs | M |
| 5 | Hardening — SOPS Apple creds, persisted anisette volume, health, multi-arch build | S |

## License

[MIT](LICENSE). The permissive license is deliberate: the clean-room reversals
above keep all AGPL-3.0 lineage (AltSign/AltServer) outside our process
boundary, so the core and any future UI stay free of network copyleft.
