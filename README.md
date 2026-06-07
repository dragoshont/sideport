# Sideport

> **Keep your sideloaded iPhone apps alive — automatically, on a server you already own.**
> No Mac left switched on. No helper app on your laptop. No re-installing every 7 days.

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-0.1.0-blue.svg)](https://github.com/dragoshont/sideport/pkgs/container/sideport)
[![Image](https://img.shields.io/badge/image-ghcr.io%2Fdragoshont%2Fsideport-2496ED.svg)](https://github.com/dragoshont/sideport/pkgs/container/sideport)
[![Arch](https://img.shields.io/badge/arch-linux%2Famd64-555.svg)](#will-it-run-on-my-hardware-intel-vs-arm)
[![Backend: .NET 10](https://img.shields.io/badge/backend-.NET%2010-512BD4.svg)](https://dotnet.microsoft.com/)

---

## The story: why this exists

You found an app that is **not** in the App Store — an emulator, a small utility,
your own build — and you installed it straight onto your iPhone. This is called
**sideloading**.

It works. Then, about **7 days later, the app stops opening.** Apple does this on
purpose: an app installed with a **free** Apple ID is only allowed to run for 7
days. To use it again, the app must be **"re-signed"**.

The usual way to re-sign is to plug the phone into a **Mac**, or to keep a
desktop app (**AltStore** + **AltServer**) running on a computer that never turns
off. Either way a personal computer has to stay awake and do the work — and if
you forget, the app dies and you start over.

**Sideport removes the chore.** You run it once on a machine that is *already*
always on — your **homelab**, your **NAS**, or any small **Docker** box — and you
tell it: *"keep this app signed on this iPhone."* From then on, Sideport logs in
to Apple, re-signs the app, and re-installs it **before the 7 days run out** — by
itself, on a timer, with nothing plugged into a laptop.

Think of it as **AltStore/AltServer without the desktop** — a quiet background
service for the server you already run at home.

> [!IMPORTANT]
> **Use Sideport only for apps you are allowed to install, on devices you own,
> with your own Apple ID.** It automates the exact same free-signing flow that
> Apple's own Xcode and the popular AltStore use. It does **not** crack, bypass,
> or share anything, and it ships **no** apps — you bring your own `.ipa` files.

> [!NOTE]
> **You do not need to know any programming to run Sideport.** You do not need
> .NET, and you never touch code. You run one container and send a few simple web
> requests (all shown below). That is all.

---

## Is Sideport for you?

**Yes, if you:**

- Sideload apps onto an **iPhone or iPad you own** and you are tired of them
  dying after a week.
- Already run an **always-on box** — a homelab, a NAS, a mini-PC — with
  **Docker** or **Kubernetes**.
- Want the weekly refresh to **"just happen"** with no Mac and no desktop app.

**Probably not, if you:**

- Only sideload once in a while and don't mind redoing it by hand.
- Already have a **paid** Apple Developer account *and* a Mac that is always on
  (your apps already last a year).

---

## How this README is organised

This page follows the [Diátaxis](https://diataxis.fr/) idea: it keeps four
different needs apart, so you can find exactly what you came for.

- **Understand it** — the story above, plus *[How Sideport works](#how-sideport-works)*
  and *[the architecture](#the-end-state-architecture)*.
- **Do it the first time** — *[Tutorial: your first auto-signed app](#tutorial-your-first-auto-signed-app)*.
- **Do one specific task** — the *[How-to guides](#how-to-guides)*.
- **Look something up** — the *[Reference](#reference)* (settings, web API, image
  tags, limits).

Read it top-to-bottom the first time; jump straight to a section after that.

---

## Words you will meet (a 60-second glossary)

| Word | What it means for you |
|---|---|
| **Sideloading** | Installing an app from an `.ipa` file, outside the App Store. |
| **Signing** | Apple makes every app carry a cryptographic "signature" from an Apple ID before a phone will run it. |
| **The 7-day expiry** | A **free** Apple ID signs apps for only **7 days**. After that the app won't open until it is re-signed. A **paid** ($99/year) account signs for a **full year**. |
| **`.ipa` file** | The install package for an iOS app — like an `.apk` on Android. **You provide this.** |
| **UDID** | Your iPhone's unique ID. Sideport needs it to know which device to sign for. |
| **Team ID** | The ID of your Apple "team". On a free account, this is your personal team. |
| **anisette / ADI** | A small helper that gives Apple's login a **trusted-device identity**, so Sideport can sign in **without** asking you for a 2FA code every time. |
| **zsign** | The tool that actually re-signs the `.ipa`. It is **built into Sideport** — nothing to install. |
| **usbmuxd** | The small piece of system "plumbing" (the same one Finder/iTunes use) that lets a computer talk to an iPhone over USB or Wi-Fi. |

> **Apple's free-account limits** (Apple's rules, not Sideport's): up to **3
> apps** signed at once, **10 app IDs per 7 days**, and the 7-day re-sign.
> Sideport works inside these limits; only a paid account lifts them.

---

## How Sideport works

Here is the whole journey, from *"my app is about to die"* to *"it's freshly
installed again"*. The pipeline is:

```
need to sign  →  log in to Apple  →  get a certificate  →  get a profile
              →  re-sign the .ipa  →  install on the iPhone  →  schedule the next refresh
```

When a refresh is due (before day 7), or when you ask for one, Sideport does
**every** step on its own:

1. **Get a trusted identity.** Sideport asks **anisette** for the device headers
   Apple expects, so the login looks like a device Apple already trusts — no 2FA
   prompt.
2. **Log in to Apple.** Sideport signs in to your Apple ID securely.
3. **Get a developer token.** It turns the login into the token Apple's developer
   service needs.
4. **Check the device.** It makes sure your iPhone's UDID is registered.
5. **Get a signing certificate.** It reuses your current one, or mints a fresh
   one when needed (a free account has only one certificate slot).
6. **Get a provisioning profile.** The small file that ties *your app* to *your
   certificate* and *your device*.
7. **Re-sign the app.** **zsign** re-signs your `.ipa` with that certificate and
   profile.
8. **Install it on the iPhone.** It sends the signed app to the phone through
   **usbmuxd** (USB, or Wi-Fi after a one-time USB pairing).
9. **Set the next alarm.** It records the new ~7-day deadline and schedules the
   next refresh — so you never have to think about it again.

---

## The end-state architecture

Sideport runs as **one small stack**: the Sideport service plus **one required
helper** (anisette). Everything else is either built into the image or already
on your host.

```
  both containers run together on your host (one Docker stack, or one k8s pod):

  ┌────────────────────────────┐      ┌──────────────────────┐
  │  Sideport (the .NET brain) │      │  anisette (sidecar)  │
  │  • Apple login + dev API   │◀───▶ │                      │
  │  • zsign (signer, built-in)│:6969 │  gives the Apple     │
  │  • installs to the iPhone  │      │  "trusted device"    │
  │  • the 7-day scheduler     │      │  headers the login   │
  │  • serves the API (:8080)  │      │  needs.              │
  └─────────────┬──────────────┘      └──────────────────────┘
                │  uses the host's usbmuxd socket  (/var/run/usbmuxd)
                ▼
          📱  your iPhone  —  USB cable, or Wi-Fi after a one-time USB pairing
```

**The three pieces** (two of them tiny):

- **Sideport** — the service you talk to. It does the Apple login, the
  certificate and profile work, the signing, the install, and the scheduling.
- **anisette** — a small **required** sidecar. It supplies the device-identity
  headers every Apple login needs. You never call it directly.
- **the signer (zsign)** — **already inside** the Sideport image. Nothing extra
  to install.

**What each part is, and why it's here:**

| Part | What it does | Where it runs | License | CPU type |
|---|---|---|---|---|
| **Sideport** | The brain: login, certs, signing, install, schedule | Main container | MIT | **linux/amd64** |
| **anisette** (`anisette-v3-server`) | Apple trusted-device (ADI) headers for login | Sidecar container | GPL-3.0 | amd64 **and** arm64 |
| **zsign** | Re-signs the `.ipa` | **Built into** the Sideport image | MIT | amd64 (static) |
| **Netimobiledevice** | Talks to the iPhone (find + install) | Library inside Sideport | MIT | any (managed) |
| **plist-cil** | Reads/writes Apple's plist files | Library inside Sideport | MIT | any (managed) |
| **Bouncy Castle** | One crypto mode Apple's tokens need | Library inside Sideport | MIT | any (managed) |
| **usbmuxd** | USB/Wi-Fi link to the iPhone | **Native daemon on your host** | GPL/Apache | host's |

> The Apple login crypto is **pure .NET** — Sideport links **no native crypto
> libraries** at run time. (A separate library, *libgsa*, is used only to *test*
> that crypto; it is **not** shipped in the image.)

---

## Will it run on my hardware? (Intel vs ARM)

**Short answer: any normal x86-64 (Intel/AMD) Linux box with Docker — yes,
directly.** That covers most homelab gear: Intel NUCs, mini-PCs, most x86 NAS
units (Synology/QNAP), and typical home servers.

| Your host | Works? | How |
|---|---|---|
| **Intel / AMD (x86-64)** Linux + Docker | ✅ Directly | This is the native target. Just run it. |
| **Kubernetes** on an x86-64 node | ✅ Directly | See the homelab how-to. |
| **ARM** (Raspberry Pi, ARM NAS, Ampere, Apple-Silicon Linux VM) | ⚠️ With emulation | The image is **amd64 only** today (the bundled `zsign` is a static amd64 binary). Run it under amd64 emulation (QEMU/binfmt — one command, in the how-to). It works, just slower. A native arm64 image is a known gap (below). |
| **Windows / macOS desktop** | ↪️ Use a Linux VM / Docker | Sideport ships as a **Linux** container; run it in Docker Desktop or a Linux VM. (anisette can't run natively on macOS/Windows either — it needs Linux.) |

**Container deliverables (what you actually pull):**

| Image | Tag | Architecture | Use it for |
|---|---|---|---|
| `ghcr.io/dragoshont/sideport` | `0.1.0` | linux/amd64 | An exact release — **the recommended pin.** |
| `ghcr.io/dragoshont/sideport` | `0.1` | linux/amd64 | The 0.1 line; picks up patch fixes (`0.1.1`, …). |
| `ghcr.io/dragoshont/sideport` | `latest` | linux/amd64 | Newest stable release. Fine to start with. |
| `ghcr.io/dragoshont/sideport` | `edge` / `sha-<short>` | linux/amd64 | Newest `main` build / one exact commit (debugging). |
| `dadoum/anisette-v3-server` | `latest` | multi-arch | The required helper. **Pin a digest in production.** |

> **Versioning.** Sideport uses semantic-version tags, starting at **`0.1.0`**.
> Pin `0.1.0` for a build that never changes, or `0.1` to receive patch fixes.
> `latest` follows the newest stable release; `edge` follows `main`. It is
> **pre-1.0**, so a minor bump (`0.1` → `0.2`) can carry small breaking changes —
> pin an exact version and skim the notes before upgrading.

---

## What you need before you start

1. An **Apple ID** (free is fine) and its password.
2. An **iPhone or iPad you own**, and its **UDID** (how to find it: below).
3. The **`.ipa` file(s)** of the app(s) you want to keep installed.
4. An **always-on host** with **Docker** (or a Kubernetes cluster).
5. On that host: the **`usbmuxd`** package installed, and the iPhone connected by
   **USB at least once** with **"Trust This Computer"** tapped. After that, USB or
   Wi-Fi both work.

> On Debian/Ubuntu: `sudo apt install usbmuxd`. This is the **only** thing you
> install directly on the host — everything else lives in the containers.

---

## Tutorial: your first auto-signed app

*A start-to-finish walkthrough. Follow every step in order; by the end, one real
app keeps itself signed.* This uses **Docker Compose** on an x86-64 host.

### 1. Make a folder and put your app in it

```bash
mkdir sideport && cd sideport
mkdir ipa
cp /path/to/MyApp.ipa ./ipa/        # the app you want to keep alive
```

### 2. Create `compose.yaml`

```yaml
services:
  sideport:
    image: ghcr.io/dragoshont/sideport:latest   # signer (zsign) is baked in
    depends_on: [anisette]
    environment:
      Sideport__Anisette__Url: "http://anisette:6969/"
      SIDEPORT_DEVICE_ID: "<YOUR-IPHONE-UDID>"          # required
      SIDEPORT_API_TOKEN: "<A-RANDOM-SECRET>"           # protects the web API
      USBMUXD_SOCKET_ADDRESS: "unix:/var/run/usbmuxd"   # how it reaches the phone
      # Your Apple password. The variable NAME encodes your Apple ID:
      #   you@example.com  ->  SIDEPORT_APPLE_PW_YOU_EXAMPLE_COM
      SIDEPORT_APPLE_PW_YOU_EXAMPLE_COM: "<YOUR-APPLE-ID-PASSWORD>"
    ports: ["8080:8080"]
    volumes:
      - ./ipa:/ipa:ro                        # your .ipa files
      - /var/run/usbmuxd:/var/run/usbmuxd    # the link to your iPhone
    restart: unless-stopped

  anisette:
    image: dadoum/anisette-v3-server:latest
    volumes:
      - anisette-data:/home/Alcoholic/.config/anisette-v3   # BACK THIS UP
    restart: unless-stopped

volumes:
  anisette-data:
```

> Make the secret with `openssl rand -hex 32`. Find your **UDID** in the
> [how-to below](#find-your-udid-and-team-id). Plug the iPhone into the host by
> USB and tap **Trust** the first time.

> [!IMPORTANT]
> **First-login 2FA.** Apple challenges the *first* login from a new device
> identity, and Sideport can't take that code through the API yet. Start from an
> anisette identity Apple already trusts — seed/copy the ADI volume from an
> existing AltServer/SideStore — or the first refresh fails with a *"login
> requires interaction"* error. After the identity is trusted once, every later
> login is silent. See [Known limits & gaps](#known-limits--gaps).

### 3. Start it

```bash
docker compose up -d
curl http://localhost:8080/readyz      # → {"ready":true,...} when good to go
```

`ready:true` means the login helper and the signer are both healthy.

### 4. Tell Sideport to keep your app signed

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

Sideport signs and installs the app **now**, then **re-signs it on its own**
before each 7-day expiry.

### 5. Watch it

```bash
curl -H "Authorization: Bearer <A-RANDOM-SECRET>" http://localhost:8080/api/apps
# → each app with a "timeUntilExpiry" countdown and the last result
```

**Done.** The app on your phone now stays alive with no further action from you.
If something looks off, see *[Known limits & gaps](#known-limits--gaps)*.

---

## How-to guides

*Short answers to specific tasks. Each one assumes Sideport is already running.*

### Manage your apps (the web API)

Everything under `/api/*` needs the `Authorization: Bearer <token>` header once
`SIDEPORT_API_TOKEN` is set.

| You want to… | Call |
|---|---|
| See all tracked apps + countdowns | `GET /api/apps` |
| Start keeping an app signed | `POST /api/apps` (body in the tutorial) |
| Re-sign an app **right now** | `POST /api/apps/{udid}/{bundleId}/refresh` |
| Stop tracking an app | `DELETE /api/apps/{udid}/{bundleId}` |
| List devices Sideport can see | `GET /api/devices` |
| Check the login helper | `GET /api/anisette/info` |
| Is it alive / ready? | `GET /healthz` · `GET /readyz` |

Sideport never stores your Apple password in the app registration — it looks the
password up separately from the environment — so the app list is safe to read and
back up.

### Find your UDID and Team ID

- **UDID** — plug the iPhone into a Mac and open **Finder** (click the device,
  then click the details line until the UDID appears); or use **Apple
  Configurator**; or read it from Sideport with `GET /api/devices` once the phone
  is connected to the host.
- **Team ID** — sign in at
  [developer.apple.com](https://developer.apple.com/account) and look under
  *Membership*; on a free account it is your **personal team**. Sideport also
  reports it in the logs after the first successful login.

### Run it on an ARM host (Raspberry Pi, ARM NAS)

The image is amd64 only, so turn on amd64 emulation once, then run normally:

```bash
docker run --privileged --rm tonistiigi/binfmt --install amd64
docker compose up -d        # the same compose.yaml as the tutorial
```

Expect slower signing, but it works. (A native arm64 image is a
*[known gap](#known-limits--gaps)*.)

### Back up the Apple identity (do this!)

anisette keeps a small *"this device is trusted by Apple"* identity in its data
volume (`anisette-data`, mounted at `/home/Alcoholic/.config/anisette-v3`).

- **Back up that volume.** If you lose it, Apple treats the next login as a brand
  new device — you get a 2FA prompt again, and you **use up one of your account's
  trusted-device slots**.
- In production, **pin the anisette image to a digest** so it can't change under
  you.

### Don't run two signers on one Apple ID

A free Apple ID has **only one** signing certificate. If Sideport **and**
AltStore/AltServer (or a second Sideport) sign with the **same Apple ID at the
same time**, they **cancel each other's certificate** and your apps keep dying.
**Pick one signer per Apple ID.** (Different Apple IDs do not conflict.)

### Run it on Kubernetes

Sideport is one container and is built **GitOps-first**. A ready-made, hardened
example — Deployment + Service + Ingress, a non-root user, health probes, the
anisette sidecar, and the host `usbmuxd` socket mounted — lives in
[`deploy/k8s/`](deploy/k8s/):

```bash
cp deploy/k8s/secret.example.yaml deploy/k8s/secret.yaml   # then edit the values
kubectl create namespace sideport
kubectl apply -n sideport -f deploy/k8s/secret.yaml
kubectl apply -k deploy/k8s/
```

It ships **"staged but safe"**: the pod runs and serves the API with the
**scheduler turned off** (`Sideport__Scheduler__Enabled: "false"`), so it does
nothing destructive until you deliberately turn it on. Flip it to `"true"` only
after you've (1) confirmed no other signer is active for the same Apple ID (see
above) and (2) seeded the anisette identity to avoid a fresh 2FA —
[`deploy/k8s/README.md`](deploy/k8s/README.md) has the go-live notes.

---

## Reference

### Configuration (environment variables)

Set these as environment variables, or as `Sideport__Section__Key` config keys.

| Variable | Required | Default | What it does |
|---|---|---|---|
| `SIDEPORT_DEVICE_ID` | ✅ | — | Your iPhone's UDID. Sideport refuses to start without it. |
| `Sideport__Anisette__Url` | ✅ | `http://anisette:6969/` | Where the anisette helper lives. |
| `SIDEPORT_APPLE_PW_<APPLEID>` | ✅ | — | Apple password. Encode the Apple ID in the name: `you@example.com` → `SIDEPORT_APPLE_PW_YOU_EXAMPLE_COM`. |
| `USBMUXD_SOCKET_ADDRESS` | recommended | system default | How to reach the iPhone. Set to `unix:/var/run/usbmuxd` when you mount the host socket. |
| `SIDEPORT_API_TOKEN` | recommended | *(unset)* | Bearer token guarding `/api/*`. If unset, the API is **open** and logs a loud warning. |
| `Sideport__Signer__BinaryPath` | — | `/opt/sideport/zsign` | The signer binary (baked into the image). |
| `Sideport__Scheduler__Enabled` | — | `true` | Turn the automatic 7-day refresh loop on/off. |

### HTTP API

| Method & path | Auth | Purpose |
|---|---|---|
| `GET /healthz` | open | Liveness — the process is up. |
| `GET /readyz` | open | Readiness — anisette reachable + signer present. |
| `GET /api/apps` | bearer | List tracked apps + expiry countdowns. |
| `POST /api/apps` | bearer | Start keeping an app signed. |
| `POST /api/apps/{udid}/{bundleId}/refresh` | bearer | Re-sign now. |
| `DELETE /api/apps/{udid}/{bundleId}` | bearer | Stop tracking an app. |
| `GET /api/devices` | bearer | Devices Sideport can see. |
| `GET /api/anisette/info` | bearer | anisette helper status. |

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

### Ports

| Port | Service | Notes |
|---|---|---|
| `8080` | Sideport HTTP API | The only port you expose. Put it behind your reverse proxy. |
| `6969` | anisette | Internal, between the two containers. Don't expose it. |

### Apple's free-account limits

| Limit | Free Apple ID | Paid ($99/yr) |
|---|---|---|
| A signature lasts | **7 days** | **1 year** |
| Apps signed at once | **3** | up to 100 |
| New app IDs | **10 per 7 days** | far more |
| Active certificates | **1** | several |

Sideport handles the re-signing for you, but it **cannot** lift Apple's limits —
only a paid account does.

---

## Known limits & gaps

*Read this before you depend on Sideport.* An honest list of what is **not** there
yet, or needs care:

- **First login needs an Apple identity Apple already trusts.** Sideport logs in
  silently only when the anisette helper carries a trusted device identity. A
  brand-new anisette triggers Apple's 2FA on the first login, and there is **no
  API endpoint to enter that code yet** (the 2FA machinery exists internally but
  isn't exposed). For now, seed/reuse an already-trusted anisette identity (e.g.
  from an existing AltServer/SideStore setup) instead of starting from a blank
  one. Once trusted, every later login is silent — so **back up that volume**.
- **amd64-only image.** No native arm64 build yet — ARM hosts need emulation (see
  the [how-to](#run-it-on-an-arm-host-raspberry-pi-arm-nas)). Multi-arch is the
  top future improvement.
- **Pre-1.0.** The HTTP API and config keys may change between minor versions
  until 1.0. Pin an exact version (`0.1.0`) and check the notes before bumping.
- **The app list is kept in memory.** If the Sideport container restarts, you
  must **register your apps again** (`POST /api/apps`). A persistent registry is
  planned. (Your Apple identity survives in the anisette volume; only the *list
  of tracked apps* is in memory.)
- **One signer per Apple ID.** Can't run beside AltStore/AltServer on the same
  Apple ID — they revoke each other (see the
  [how-to](#dont-run-two-signers-on-one-apple-id)).
- **The Apple identity must be trusted.** A brand-new anisette identity triggers a
  2FA prompt and uses a trusted-device slot. Reuse/seed an already-trusted one,
  and **back it up**.
- **Install needs `usbmuxd` (USB or paired Wi-Fi).** There is **no pure
  over-the-air install**: the phone must be reachable from the host through
  `usbmuxd` (USB, or Wi-Fi after a one-time USB pairing).
- **API only, no web UI yet.** You drive Sideport with simple web requests
  (`curl` or any HTTP client). A graphical UI is planned.
- **Apple rate-limits frequent logins.** Sideport spaces its work out; if you
  fire many manual refreshes back-to-back, Apple may pause logins for a while —
  just wait.

---

## Status

Sideport is **feature-complete and validated against Apple's live services**:
login, certificate and profile management, signing, device install, and the
scheduled refresh loop are all implemented and covered by **~180 automated
tests**. The first tagged image is published at
`ghcr.io/dragoshont/sideport:0.1.0` (and `:latest`). Turning the automatic
scheduler on for a given Apple ID is a deliberate,
reversible step (see
*[Don't run two signers](#dont-run-two-signers-on-one-apple-id)*).

---

## Build from source (for developers)

You do **not** need this to *use* Sideport — it's here for contributors.

```bash
dotnet build                              # builds Sideport.slnx
dotnet test                               # full test suite (~180 tests)
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
  Dockerfile             the container image (signer baked in, linux/amd64)
  compose.yaml           service + anisette for local runs
tools/
  sideport-live-probe    read-only check against the real Apple services
```

### Design background

Sideport is a clean-room, **MIT-licensed** reimplementation of the
sideload-and-refresh flow. The Apple login crypto is pure managed .NET (no native
crypto at run time), and Apple's endpoints were reimplemented from **documented
behaviour** rather than copied from copyleft (AGPL) projects — which is what keeps
the whole thing permissively licensed. The full design and build plan live in
[`docs/`](docs/):
[`sideport-dotnet-consolidation.md`](docs/sideport-dotnet-consolidation.md) (the
*what* & *why*) and
[`sideport-implementation-plan.md`](docs/sideport-implementation-plan.md) (the
*how* & *in what order*).

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
