# Sideport

> **Keep your sideloaded iPhone apps alive — automatically, on your own server.**
> No Mac left running, no AltStore on your laptop, no reinstalling every week.

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Image](https://img.shields.io/badge/image-ghcr.io%2Fdragoshont%2Fsideport-2496ED.svg)](https://github.com/dragoshont/sideport/pkgs/container/sideport)
[![Backend: .NET 10](https://img.shields.io/badge/backend-.NET%2010-512BD4.svg)](https://dotnet.microsoft.com/)

---

## The problem Sideport solves

When you install an app on an iPhone **without the App Store** (this is called
*sideloading*), Apple makes it expire. With a **free** Apple ID the app stops
opening after **7 days** and you have to "refresh" it — normally by plugging the
phone into a Mac, or by keeping the **AltStore** app and a desktop helper
running on a computer that's always on.

That's annoying. You forget, the app dies, you re-do it.

**Sideport does that refresh for you, on a server you already run.** You tell it
once: *"keep this app signed on this iPhone."* From then on it logs in to Apple,
re-signs the app, and re-installs it **before the 7 days run out** — quietly, on
a schedule, with nothing plugged into a laptop.

Think of it as **"AltStore/AltServer, but headless"** — a small service for your
homelab or NAS instead of an app on your desktop.

---

## Is this for you?

**Use Sideport if you:**

- Sideload apps onto an iPhone/iPad you own (emulators, utilities, your own
  builds, apps not on the App Store) and you're tired of them expiring.
- Run a **homelab, NAS, or any always-on box** with Docker or Kubernetes.
- Want the refresh to "just happen" without a Mac or a desktop app open.

**You probably don't need it if:** you only sideload occasionally and don't mind
re-doing it by hand, or you have a paid Apple Developer account *and* a Mac
that's always on.

> [!IMPORTANT]
> **Use it only for apps you're allowed to install, on devices you own, with
> your own Apple ID.** Sideport automates the exact same free-signing flow that
> Xcode and AltStore use. It does **not** crack, bypass, or distribute anything,
> and it ships **no** copyrighted apps — you bring your own `.ipa` files.

---

## A 60-second crash course (if Apple stuff is new to you)

| Term | What it means for you |
|---|---|
| **Sideloading** | Installing an app outside the App Store (from an `.ipa` file). |
| **Signing** | Apple requires every app to be cryptographically "signed" by an Apple ID before a phone will run it. |
| **The 7-day expiry** | A **free** Apple ID signs apps for only 7 days. After that the app won't open until it's re-signed. (A **paid** $99/yr developer account signs for a full year.) |
| **`.ipa` file** | The installable package for an iOS app — like an `.apk` on Android. You provide this. |
| **UDID** | Your iPhone's unique ID. Sideport needs it to know which device to sign for. (Find it in Finder, or apps like *Apple Configurator*.) |
| **2FA / trusted device** | Apple wants a verification code on new logins. Sideport reuses a device identity Apple already trusts (via the *anisette* helper) so it can log in without prompting you every time. |

**Free Apple ID limits** (Apple's, not Sideport's): up to **3 apps** signed at
once, **10 app IDs per 7 days**, and the 7-day re-sign. Sideport works within
these; a paid account lifts them.

---

## What you'll need

1. An **Apple ID** (a free one is fine) and its password.
2. An **iPhone or iPad** you own, and its **UDID**.
3. The **`.ipa`** file(s) of the app(s) you want to keep installed.
4. An **always-on host** with **Docker** (or a Kubernetes cluster).
5. The phone reachable from that host — over **USB**, or over **Wi-Fi** once
   it's been paired (Sideport talks to it through `usbmuxd`, the same plumbing
   Finder/iTunes use).

---

## Quick start (Docker Compose)

This brings up Sideport plus its one required helper (*anisette*) and keeps one
app signed.

**1. Create a `compose.yaml`:**

```yaml
services:
  sideport:
    image: ghcr.io/dragoshont/sideport:latest   # signer (zsign) is baked in
    depends_on: [anisette]
    environment:
      Sideport__Anisette__Url: "http://anisette:6969/"
      SIDEPORT_DEVICE_ID: "<YOUR-IPHONE-UDID>"        # required
      SIDEPORT_API_TOKEN: "<A-RANDOM-SECRET>"         # protects the API; openssl rand -hex 32
      # Apple password. The variable name encodes your Apple ID:
      #   you@example.com  ->  SIDEPORT_APPLE_PW_YOU_EXAMPLE_COM
      SIDEPORT_APPLE_PW_YOU_EXAMPLE_COM: "<YOUR-APPLE-ID-PASSWORD>"
    ports: ["8080:8080"]
    volumes:
      - ./ipa:/ipa:ro          # put your .ipa files here
    restart: unless-stopped

  anisette:
    image: dadoum/anisette-v3-server:latest
    volumes:
      - anisette-data:/home/Alcoholic/.config/anisette-v3/lib/   # back this up!
    restart: unless-stopped

volumes:
  anisette-data:
```

**2. Start it:**

```bash
docker compose up -d
curl http://localhost:8080/readyz      # {"ready":true,...} when good to go
```

**3. Tell Sideport to keep an app signed** (replace the values):

```bash
curl -X POST http://localhost:8080/api/apps \
  -H "Authorization: Bearer <A-RANDOM-SECRET>" \
  -H "Content-Type: application/json" \
  -d '{
        "bundleId":    "com.example.myapp",
        "appleId":     "you@example.com",
        "teamId":      "<YOUR-TEAM-ID>",
        "deviceUdid":  "<YOUR-IPHONE-UDID>",
        "inputIpaPath":"/ipa/MyApp.ipa"
      }'
```

That's it. Sideport now signs and installs the app, then **re-signs it on its
own** before each 7-day expiry. Check on it any time:

```bash
curl -H "Authorization: Bearer <A-RANDOM-SECRET>" http://localhost:8080/api/apps
# → shows each app with a "timeUntilExpiry" countdown and last result
```

> [!TIP]
> Don't know your **Team ID**? Call `GET /api/apps` after a first login attempt,
> or check your account at [developer.apple.com](https://developer.apple.com).
> For a free account the Team ID is usually your personal team.

---

## How you use it day to day (the API)

Sideport is a small HTTP service. A UI can sit on top of it, but you can drive it
with `curl` or any HTTP client. Everything under `/api/*` requires the
`Authorization: Bearer <token>` header when you've set `SIDEPORT_API_TOKEN`.

| What you want | Call |
|---|---|
| See all tracked apps + expiry countdowns | `GET /api/apps` |
| Start keeping an app signed | `POST /api/apps` (body below) |
| Re-sign an app **right now** | `POST /api/apps/{udid}/{bundleId}/refresh` |
| Stop tracking an app | `DELETE /api/apps/{udid}/{bundleId}` |
| List devices Sideport can see | `GET /api/devices` |
| Check the Apple login helper | `GET /api/anisette/info` |
| Is it alive? / Is it ready? | `GET /healthz` / `GET /readyz` |

**Register-an-app body:**

```json
{
  "bundleId":     "com.example.myapp",
  "appleId":      "you@example.com",
  "teamId":       "XXXXXXXXXX",
  "deviceUdid":   "00000000-...",
  "inputIpaPath": "/ipa/MyApp.ipa"
}
```

Sideport never stores your Apple password in this registration — it looks the
password up separately from the environment (or your secret store), so the app
list stays safe to read and back up.

---

## Running it in a homelab (Kubernetes / Flux)

Sideport ships as a single container and is built **GitOps-first** — it's meant
to be deployed by [Flux](https://fluxcd.io/), not `kubectl apply` by hand. A
ready-made, hardened deployment (Deployment + Service + Ingress, runs as a
non-root user, readiness/liveness probes, secrets via SOPS, anisette sidecar) is
maintained here:

- **[`homelab/apps/platform/sideport/`](https://github.com/dragoshont/homelab/tree/main/apps/platform/sideport)**

It mounts the host's `usbmuxd` socket so the pod can install to a phone over
USB/Wi-Fi, runs the *anisette* helper as a sidecar, and exposes the API at
`sideport.<your-domain>` through Traefik.

**Going live with Flux** — the manifests sit "staged but inert" (not yet listed
in your cluster's root `kustomization.yaml`) so a half-configured signer can't
start fighting an existing one. To turn it on:

1. **Create the secret.** Copy `secret.sops.example.yaml`, fill in your Apple ID,
   device UDID, and a generated API token, set the Apple password on the host
   (never in the repo), then SOPS-encrypt it to `secret.sops.yaml` and uncomment
   it in the app's `kustomization.yaml`.
2. **Seed anisette.** Point the pod at your already-trusted anisette, or persist
   and seed its ADI volume — a brand-new ADI triggers a 2FA loop (see below).
3. **Wire it in.** Add `- ../../apps/platform/sideport/` to
   `clusters/home/kustomization.yaml` and add the `sideport.<domain>` DNS record.
4. **Reconcile.** `flux reconcile kustomization …` (or just push — Flux pulls it).
   The pod comes up with the **scheduler off**, so it serves the API and does
   nothing destructive until you say so.
5. **Cut over.** Once you've confirmed no other signer is active for that Apple
   ID (see [single-signer](#one-apple-id--one-signer)), flip
   `Sideport__Scheduler__Enabled` to `true`.

> [!NOTE]
> The scheduler ships **off** on purpose so you can deploy via Flux, smoke-test a
> manual refresh, and only then enable the automatic loop — a safe, reversible
> go-live rather than an all-at-once switch.


---

## Configuration reference

Set these as environment variables (or `Sideport__Section__Key` config keys).

| Variable | Required | Default | What it does |
|---|---|---|---|
| `SIDEPORT_DEVICE_ID` | ✅ | — | Your iPhone's UDID. Sideport refuses to start without it. |
| `Sideport__Anisette__Url` | ✅ | `http://anisette:6969/` | Where the anisette helper lives. |
| `SIDEPORT_APPLE_PW_<APPLEID>` | ✅ | — | Apple password. Encode the Apple ID in the name: `you@example.com` → `SIDEPORT_APPLE_PW_YOU_EXAMPLE_COM`. |
| `SIDEPORT_API_TOKEN` | recommended | *(unset)* | Bearer token guarding `/api/*`. If unset, the API is **open** and logs a loud warning. |
| `Sideport__Signer__BinaryPath` | — | `/opt/sideport/zsign` | The signer binary (baked into the image). |
| `Sideport__Scheduler__Enabled` | — | `true` | Turn the automatic 7-day refresh loop on/off. |

---

## Important things to know

### One Apple ID = one signer

A free Apple ID can have **only one active signing certificate**. If you run
Sideport **and** AltStore/AltServer (or a second Sideport) against the **same
Apple ID at the same time**, they will **revoke each other's certificate** and
your apps will keep dying. Pick one signer per Apple ID. (Different Apple IDs are
fine.)

### Back up the anisette data

The *anisette* helper stores a small "this device is trusted by Apple" identity
in its data volume (`anisette-data`). **Back it up.** If you lose it, Apple
treats the next login as a brand-new device — you'll get a 2FA prompt again and
you burn one of your account's trusted-device slots. In production, also pin the
anisette image to a specific version.

### Free vs paid account

A **free** Apple ID means the 7-day re-sign and the 3-app / 10-IDs-per-week
limits above. Sideport handles the re-signing for you, but it can't lift Apple's
limits — a **paid** ($99/yr) developer account does (year-long certs, more apps).

### Logging in too often

Apple rate-limits repeated logins. Sideport spaces its work out; you generally
won't hit this. If you script lots of manual refreshes back-to-back you may see
Apple temporarily reject logins — wait and try later.

---

## How it works (the short version)

```
   your HTTP call ─▶  Sideport service  ──▶ logs in to Apple, gets/renews the
                          │                  signing certificate & profile
                          ├─▶ re-signs your .ipa   (zsign — built into the image)
                          ├─▶ installs it to the iPhone  (USB or Wi-Fi)
                          └─▶ schedules the next refresh before day 7
                          ▲
        anisette helper ──┘  provides the Apple "trusted device" headers
        (a small sidecar)    every Apple login needs
```

Three pieces, two of them tiny:

- **Sideport** — the service you talk to. It does the Apple login, certificate
  and profile management, signing, installing, and scheduling.
- **anisette** — a small required helper that supplies the device-identity
  headers Apple's login expects. Runs as a sidecar; you don't call it directly.
- **the signer** — `zsign`, the tool that actually re-signs the `.ipa`. It's
  **baked into the Sideport image**, so there's nothing extra to install.

Sideport only needs the phone reachable through `usbmuxd` (USB, or Wi-Fi after a
one-time USB pairing).

---

## Status

Sideport is **feature-complete and validated against Apple's live services**:
login, certificate/profile management, signing, device install, and the
scheduled refresh loop are all implemented and covered by ~180 automated tests.
A public container image is published at
`ghcr.io/dragoshont/sideport:latest`. Turning the automatic scheduler on for a
given Apple ID is a deliberate step (see [single-signer](#one-apple-id--one-signer)).

---

## Build from source (for developers)

```bash
dotnet build                              # builds Sideport.slnx
dotnet test                               # full test suite
dotnet run --project src/Sideport.Api     # run the API locally

# or the whole stack (service + anisette):
docker compose -f deploy/compose.yaml up --build
```

**Project layout:**

```
src/
  Sideport.Api           ASP.NET Core API + dependency wiring
  Sideport.Core          the core interfaces + domain records
  Sideport.GrandSlam     Apple login (managed crypto — no native libraries at runtime)
  Sideport.DeveloperApi  Apple developer-services client, anisette client, signer wrapper
  Sideport.Devices       device discovery + install (Netimobiledevice)
  Sideport.Orchestrator  the single-flight refresh loop + app registry
deploy/
  Dockerfile             the container image (signer baked in)
  compose.yaml           service + anisette for local runs
tools/
  sideport-live-probe    read-only check against the real Apple services
```

### Design background

Sideport is a clean-room, **MIT-licensed** reimplementation of the
sideload-and-refresh flow. The Apple login crypto is pure managed .NET (no native
crypto libraries at runtime), and the Apple endpoints were reimplemented from
documented behaviour rather than copied from AGPL projects — which is what keeps
the whole thing permissively licensed. The full design rationale and build plan
live in the homelab docs:
[`sideport-implementation-plan.md`](https://github.com/dragoshont/homelab/blob/main/docs/sideport-implementation-plan.md).

---

## Acknowledgments

Sideport stands on the shoulders of the people who reverse-engineered and
documented Apple's sideloading flow, and on several excellent open-source
libraries. Sideport is an independent, clean-room reimplementation — where a
project below is copyleft (AGPL), it was studied for **protocol facts only** and
**no code was copied**; that boundary is what lets Sideport stay MIT-licensed.
Huge thanks to:

**The sideloading ecosystem that defined this category**
- [**AltStore / AltServer / AltSign**](https://altstore.io) by Riley Testut — the
  original free-developer-account sideloading tools this project automates a
  headless equivalent of.
- [**AltServer-Linux**](https://github.com/NyaMisty/AltServer-Linux) by NyaMisty
  — proved the flow runs without a Mac.
- [**pypush**](https://github.com/JJTech0130/pypush) by JJTech0130 — the clearest
  open documentation of Apple's GrandSlam (GSA) authentication, used as the
  protocol spec for the clean-room login implementation.

**Bundled helpers (run alongside Sideport)**
- [**anisette-v3-server**](https://github.com/Dadoum/anisette-v3-server) by Dadoum
  — supplies the Apple device-identity (ADI) headers every Apple login needs.
- [**zsign**](https://github.com/zhlynn/zsign) by zhlynn — the fast iOS code
  signer, baked into the image as the signing backend.

**Libraries Sideport builds on**
- [**Netimobiledevice**](https://github.com/artehe/Netimobiledevice) (MIT) — pure
  .NET device communication (usbmux / lockdown / install), replacing the entire
  native `libimobiledevice` family.
- [**plist-cil**](https://github.com/claunia/plist-cil) (MIT) — Apple property-list
  parsing.
- [**Bouncy Castle**](https://www.bouncycastle.org/) (MIT) — the AES-GCM mode
  Apple's token blobs require.
- [**.NET / ASP.NET Core**](https://dotnet.microsoft.com/) by Microsoft (MIT).
- [**libgsa**](https://github.com/dragoshont/libgsa) (MIT) — used as a
  byte-for-byte test oracle to verify the managed SRP/GrandSlam crypto.

---

## License

[MIT](LICENSE).
