# Sideport

> A self-hosted, private iOS app catalog. You register apps, manage your own
> devices, and hand each one a one-tap install link from a single web console —
> while the signing is kept valid on current iOS automatically.
> The signing is real; the UI is the pretty face.

[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![Backend: .NET 10 (LTS)](https://img.shields.io/badge/backend-.NET%2010%20LTS-512BD4.svg)](https://dotnet.microsoft.com/)
[![UI: Blazor WASM PWA](https://img.shields.io/badge/UI-Blazor%20WASM%20PWA-5C2D91.svg)](https://learn.microsoft.com/aspnet/core/blazor/)

---

## What this is

Sideport is a **control plane** for sideloading iOS apps onto **your own**
devices, using **your own** free Apple ID developer provisioning — without a Mac
in the loop and without each user driving the process by hand.

You (the developer/operator) get a web console to:

- **Register apps** — drop in an `.ipa`, give it an icon and a name.
- **Manage devices** — a roster of the iPhones/iPads you provision for.
- **Publish & install** — hand each device a one-tap `itms-services` install
  link. (iOS has **no silent push** — the user taps the link once and trusts
  the profile; Sideport publishes, the device pulls.)
- **Watch expiry** — free-provisioning certs and profiles live ~7 days; Sideport
  shows live countdowns and re-signs before they lapse.

The hard part — producing a signature that **current iOS actually accepts at
launch** — is already solved (see [Engine](#the-engine-what-actually-signs)).
Sideport wraps that engine in an API + installable web UI.

> [!IMPORTANT]
> Sideport is for sideloading apps **you are entitled to install** onto
> **devices you own or administer**, using **your own** Apple ID. It does not
> bypass Apple's security model — it automates the same free-provisioning flow
> Xcode uses, then re-signs and serves the result. It is not a piracy tool and
> ships no copyrighted apps.

---

## Reality check — what the free tier actually allows

Before the architecture, the hard limits that shape the whole product. Sideport
rides a **free Apple ID ("Personal Team")**, which Apple caps hard:

| Limit | Free Apple ID | What it means for Sideport |
|---|---|---|
| Apps installed at once | **3** | A device roster larger than ~3 apps needs apps parked/rotated — or a paid account |
| New App IDs / rolling 7 days | **10** | Re-signing *existing* apps is free; each *new distinct* bundle ID burns one slot (an explicit bundle ID can burn **two**) |
| Signing cert lifetime | **7 days** | The only reason the re-sign loop exists |
| Active signing certs | **1 per account** | **Only one signer may run at a time** — two simultaneously revoke each other (see [Single-signer rule](#credential-custody--the-single-signer-rule)) |

> [!WARNING]
> The "store" framing is real but **bounded**. One free Apple ID is a
> **single-team, 3-app-at-a-time** engine — not an app-store backend. Scaling
> past that means a **paid Developer account** ($99/yr — lifts the 7-day and
> 3-app limits) or **multiple Apple IDs** (each its own cert + anisette + 2FA,
> with account-standing risk). Sideport's job is to manage the fleet you *can*
> serve and **surface the ceiling** rather than pretend it away.

---

## How we're different (the part everyone confuses)

There are **four** different things called "Alt-something," plus a couple of
cousins. Conflating them is the #1 source of confusion, so here is the precise
map.

| Project | What it actually is | Where it runs | Maintained? | Relationship to Sideport |
|---|---|---|---|---|
| **AltStore** | An **iOS app** users install; a personal store on the phone | iOS | ✅ Active (Riley Testut) | **We replace its UX.** We don't ship it. |
| **AltServer** (desktop) | A **Mac/Windows app** that signs & pushes over USB | macOS / Windows | ✅ Active | Not used — desktop only, needs a Mac/PC running |
| **AltServer-Linux** (NyaMisty fork) | A **headless Linux daemon** that does Apple-portal automation + signing | Linux | ⚠️ **Dead repo** (`v0.0.5`, Apr 2022) | **This is our engine.** We build on top of it. |
| **SideStore** | An **iOS app** that signs **on the phone** via a local VPN trick | iOS | ✅ Very active | **Architecturally incompatible** (see below) — but its *libraries* are our escape hatch |
| **Sideloadly** | A desktop sideloading GUI | macOS / Windows | ✅ Active | Not used — desktop only |
| **zsign** | A cross-platform `codesign` replacement | Linux / macOS | ✅ Active | **Our actual code-signing step** |

### The one-sentence positioning

> **Sideport replaces AltStore's *user experience* (a store you browse and
> install from) with a central, multi-device web console that publishes
> one-tap installs — while reusing AltServer-Linux's *plumbing* (Apple-portal
> automation) and zsign's *signing* underneath.**

We are **not** "a different AltServer." We are a product layer *on top of* the
signing engine, exposing a developer view (register apps, see expiry) and a
fleet view (devices) that neither AltStore-the-app nor AltServer ever gives you.

### Why not "just switch to SideStore"?

SideStore is excellent and actively maintained — but it is an **iOS app that runs
on the phone** and pulls apps for **one device, driven by its owner**. That is
the exact **opposite** of Sideport's model, which is **server-side, central,
multi-device, publish-from-one-console**. Adopting SideStore-the-app would mean
deleting the product and telling every user "install this app and do it
yourself." Same relationship as AltStore: it's a competitor to the *UX*, not an
upgrade path for the *engine*.

What **is** worth adopting from the SideStore org — eventually, only if forced —
are its **Linux-capable libraries** (`apple-private-apis`, `omnisette`; MPL-2.0),
as the migration target for the one orphaned piece of our stack. See
[Longevity & the escape hatch](#longevity--the-escape-hatch).

---

## The engine: what actually signs

```mermaid
graph TD
    subgraph cp["Sideport — control plane (this repo)"]
        UI["Blazor WASM PWA<br/>catalog · devices · publish · countdowns"]
        API["ASP.NET Core 10 API<br/>hosts WASM · install manifests · live logs"]
        UI --> API
    end
    subgraph eng["Signing engine (reused, headless)"]
        PROV["IAppleProvisioner<br/>Apple-ID auth · mint cert · register device<br/>· make profile · 7-day refresh"]
        SIGN["ISigner<br/>codesign via patched zsign"]
    end
    subgraph apple["Apple / device"]
        PORTAL["Apple Developer portal<br/>(needs anisette/ADI auth data)"]
        PHONE["your iPhone / iPad<br/>(itms-services install)"]
    end
    API -->|"Sign(ipa, device)"| PROV
    PROV --> SIGN
    PROV -->|auth| PORTAL
    API -->|"signed .ipa + manifest.plist"| PHONE
```

**Today, both `IAppleProvisioner` and `ISigner` are satisfied by
AltServer-Linux** (with its signing step swapped from a stale `ldid` to a patched
**zsign**, so signatures pass AMFI on iOS 17+/26). The control plane never names
AltServer directly — it only talks to the two interfaces.

### Why the signing swap mattered

AltServer-Linux historically signed via a ~4-year-old vendored `ldid` that emits
SHA-1 + legacy-DER signatures. Modern iOS (17+, verified on **iOS 26.5**) rejects
those at launch (`Code=85`) — the app installs but dies on first run. Replacing
the final codesign with a **patched zsign** (SHA-256-only) produces signatures
that launch cleanly. This is already done and verified end-to-end on a physical
**iPhone 16 Pro Max / iOS 26.5**.

> **When upstream zsign merges the fix**, we drop our local patch and pin stock
> zsign — a one-line change with zero architectural impact.

---

## How install actually works on iOS

A web page **cannot** push an app onto iOS directly. The real mechanism is
Apple's enterprise/ad-hoc install manifest:

1. In Sideport you tap **Install** → Safari is handed an
   `itms-services://?action=download-manifest&url=https://<host>/.../manifest.plist`.
2. iOS fetches a **`manifest.plist`** (over HTTPS with a valid cert) that points
   at the signed **`.ipa`**.
3. The IPA must be signed for **that device's** provisioning profile — which the
   provisioner already mints. The user then taps **Trust** once for the profile
   (free-developer apps are not silently trusted).

So Sideport is, mechanically, a **signed-IPA + manifest host with a good UI**.
The "store" is real; the install is `itms-services`.

> [!NOTE]
> **Verified vs. assumed.** End-to-end signing + launch is verified by **LAN
> `devicectl` direct-install** on a physical iPhone 16 Pro Max / iOS 26.5. The
> over-the-air `itms-services` manifest path is the **intended** install UX but
> is **not yet validated end-to-end** here — it additionally requires the device
> be in the profile *and* a manual "Untrusted Developer → Trust" step. Treat OTA
> install as a roadmap item, not a solved mechanism. (`devicectl` direct-install
> is the proven path today for tethered/LAN devices.)

---

## Architecture

```text
sideport/
├── signer/                    # the headless engine (AltServer-Linux + zsign, Docker)
│                              #   built/published to GHCR; implements the provisioner+signer
├── src/
│   ├── Sideport.Api/          # ASP.NET Core 10 Web API; also serves the WASM static files
│   ├── Sideport.Client/       # Blazor WebAssembly PWA (manifest + service worker)
│   └── Sideport.Shared/       # DTOs/contracts shared by API + Client
└── (later) Sideport.App/      # MAUI Blazor Hybrid — reuses Client components natively
```

### Decisions

| Concern | Choice | Why |
|---|---|---|
| Backend runtime | **.NET 10 (LTS)** | Long-term support into 2028; verified toolchain present |
| UI | **Blazor WebAssembly PWA** | True installable/offline "Add to Home Screen" on iPhone **and** desktop; clean client/API split. (Blazor *Server* can't be a real offline PWA — it needs a live socket.) ⚠️ iOS PWAs have real limits — no true background, storage eviction — so the PWA is a console, not a daemon. |
| Native reuse | **MAUI-*ready*, not MAUI-*now*** | Components stay DI-clean and JS-interop-light so a MAUI Blazor Hybrid app *could* host them later — but no MAUI app is a current goal (YAGNI; revisit only if a native need appears) |
| Hosting shape | **Single container** | The API serves the WASM bundle *and* the API → one URL, one TLS cert, installable as a PWA |
| Engine boundary | **`IAppleProvisioner` + `ISigner`** | The whole product stays engine-agnostic (see below) |
| License | **AGPL-3.0** | Matches the AltServer-Linux / AltSign lineage — see [License](#license) for the network-clause implications |

---

## Credential custody & the single-signer rule

Two operational invariants the control plane **must** enforce — they are not UI
polish, they are correctness:

- **One signer at a time.** A free Apple ID has exactly **one** active signing
  cert. The engine host already has up to three re-sign triggers (a twice-daily
  timer, a udev-on-plug hook, and the AltStore-app self-refresh). Sideport's
  "Re-sign now" button and any Sideport-side scheduler add **more** triggers, so
  Sideport must serialize every sign through a **single host-level lock** and
  never let two runs overlap. Two concurrent signers revoke each other's cert
  and thrash every installed app. A push-based control plane *increases* the
  number of potential signers — owning the mutex is therefore Sideport's job,
  not an afterthought.
- **Credential custody is explicit.** Signing needs the Apple ID password
  (today it lives host-local in `/etc/altserver/env`, 0600). Sideport does **not**
  hold or proxy that secret in the web tier: the control plane *triggers* the
  engine over a narrow local channel; the engine keeps custody of the Apple
  credential. Target state moves the secret into SOPS/sealed-secrets, never into
  the API container or the repo. The console authenticates the *operator*; it
  never sees the *Apple password*.

---

## Longevity & the escape hatch

The signing problem is solved. The **durable risk is not signing — it's Apple
Developer *auth automation***. AltServer-Linux logs into Apple's portal using
**anisette/ADI** (Apple's one-time-password machinery). Apple rotates that
protocol periodically, and the dead NyaMisty repo won't follow. If that happens,
*signing still works* but *minting a valid profile* breaks.

That is exactly why the engine sits behind a **wide interface**, and why the
migration target is **components, not SideStore-the-app**:

| Function | Today | Replacement if Apple breaks AltServer-Linux | Status of replacement |
|---|---|---|---|
| Apple-ID auth / anisette | AltServer-Linux | [`Dadoum/anisette-v3-server`](https://github.com/Dadoum/anisette-v3-server) (Docker, Linux) | ✅ alive |
| Portal automation (cert/device/profile) | AltServer-Linux | [`SideStore/apple-private-apis`](https://github.com/SideStore/apple-private-apis) Rust crates (`apple-dev-apis`, `icloud-auth`, `omnisette`; MPL-2.0) | ⚠️ stale (~2 yr, "early stage") — would need **non-trivial revival**: it reimplements Apple's private GSA/Xcode auth, arguably the hardest part of the whole stack |
| Codesign | patched **zsign** | patched **zsign** (unchanged) | ✅ alive |

**Migration policy:** stay on AltServer-Linux now (it works); **execute the swap
only when the trigger fires** — i.e. AltServer-Linux's Apple auth actually
breaks. "Abandoned but functional, behind an interface" beats "actively
maintained but the wrong shape." The replacement is an **engine-only daemon
swap** the control plane never notices.

---

## Status

🧪 **Design / pre-implementation.** This README captures the product thesis,
the engine lineage, and the architecture. Code scaffolding has not started.

### Roadmap (high level)

- [ ] `signer/` — package the verified AltServer-Linux + zsign engine behind a
      small local API implementing `IAppleProvisioner` + `ISigner`, with the
      **single-signer lock** owned here.
- [ ] `Sideport.Api` — catalog, devices, IPA upload, expiry parsing,
      `itms-services` manifest generation, live sign logs (SSE/SignalR).
- [ ] `Sideport.Client` — Blazor WASM PWA: app grid, device roster, countdowns,
      one-tap install.
- [ ] Validate the OTA `itms-services` install path end-to-end (today only LAN
      `devicectl` is proven).
- [ ] Auth model (LAN-only / SSO / PIN), credential custody (SOPS), storage layout.
- [ ] `Sideport.App` — MAUI Blazor Hybrid wrapper (deferred, only if a native need appears).
- [ ] Provisioner v2 (anisette-v3-server + revived Rust crates) — **deferred,
      trigger-gated.**

---

## License

[AGPL-3.0](LICENSE), matching the AltServer-Linux / AltSign lineage Sideport
builds on.

> [!NOTE]
> AGPL-3.0 is **strong network copyleft**: anyone you *expose Sideport to over a
> network* may request the corresponding source. For a private homelab tool
> that is usually fine (and intentional — it keeps the lineage open), but it is
> the most aggressive copyleft option; relax to a permissive license only with
> eyes open. Upstreams carry their own terms — the SideStore crates are
> **MPL-2.0**, zsign's license should be confirmed before vendoring its code —
> see each upstream.
