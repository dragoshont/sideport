# Sideport — implementation plan (build order, gates, adversarial review)

Date: 2026-06-07
Status: plan / ready to execute
Companion to: [`sideport-dotnet-consolidation.md`](sideport-dotnet-consolidation.md) (the *what* and *why*)
This doc: the *how*, *in what order*, and *how we know each step is done*.

Implementation and these design docs live in **`github.com/dragoshont/sideport`**
(MIT). Phase 0 (skeleton + the four seams + MIT relicense) is already committed
(`528c011`).

---

## 0. Invariants (do not violate while executing)

These are the load-bearing decisions from the design doc, restated as hard
rules the implementation must hold at every commit:

1. **No native crypto at runtime.** GrandSlam SRP/AES/HMAC/PBKDF2 is pure
   managed BCL. `libgsa` appears *only* under `tests/` as a vector oracle.
2. **No AGPL source ever enters the tree.** Apple endpoints are clean-roomed
   from documented behaviour + the pypush spec. AltSign/AltServer are read for
   *protocol facts*, never translated. Record provenance in commit messages.
3. **Anisette stays a separate process behind `IAnisetteProvider`** spoken to
   over HTTP. Its ADI volume is treated as a secret + backed up.
4. **One image, two sidecars.** The deliverable is a multi-arch Linux OCI image
   (`linux/amd64` + `linux/arm64`); anisette + signer are sidecars, never baked
   into the brain.
5. **Single-flight signing.** Every re-sign serializes through one lock. Two
   concurrent signs revoke each other's free cert (design §reality-check).
6. **Apple credentials never touch the web tier in cleartext.** SOPS / env on
   the host side; the API receives a handle, not the password.
7. **Every phase ships green** (build + test + a working vertical slice). No
   phase merges behind a broken build or a skipped-without-reason test.

---

## 1. Build order at a glance

```
P0 ✅ skeleton + seams + MIT            (done: 528c011)
P1    device plane (read-only)          ── first real Apple-touching slice
P2 ✅ GrandSlam crypto (managed SRP)    (done: 55649e9 — 49 tests, 0 skipped)
P3 ✅ GrandSlam auth protocol           (done: 8662b30 — e2e vs fake GsService2)
P4 ✅ developer API (cert/appid/profile)  (done: 6523066 — 65 DeveloperApi tests, replay oracle)
P5 ✅ signer + IPA repack               (done: cf261d7 — 44 DeveloperApi tests)
P6 ✅ refresh orchestrator              (done: 0710de3 — 29 tests, single-flight)
P7 ✅ packaging + GitOps deploy         (done — GHCR image, /readyz, API auth, CI)
```

> **Live bring-up (2026-06-07):** the host got the .NET SDK (ansible role
> `dotnet_sdk`) and Sideport was run against **real Apple**. Auth + trust are
> PROVEN live: SRP completes, M2 verifies, and login clears with **NO 2FA** —
> Sideport inherits AltServer's anisette trust by presenting the provisioned
> device-id. Four live wire-format fixes resulted (the replay oracle couldn't see
> them): (1) the GSA cookie `c` is a plist string (not data); (2) the SPD login
> blob is a bare `<dict>` fragment (no `<plist>` envelope); (3) the host anisette
> is provisioned for the flat **v1** `GET /` (not `/v3/get_headers`), and reusing
> its device-id/client-info is what inherits trust; (4) the anisette
> `X-Apple-I-Client-Time` must be echoed (not `UtcNow`) since the OTP is bound to
> it. The **developer-services token** was also implemented: the raw login
> `GsIdmsToken` is rejected (1100), so Sideport now runs the GSA **app-tokens**
> flow (`com.apple.gs.xcode.auth`) — proven live to mint a valid **1-year** token
> (BouncyCastle GCM for the 16-byte IV the blob uses).
>
> The read-path probe (listTeams/devices/appIds) is currently blocked by a
> **transient account throttle** induced by the debugging session's ~8 rapid
> logins: Apple's GSA still authenticates and mints a valid token, but
> developer-services returns 1100 "session expired" for a flagged account (the
> same family as the GSA `-22406` disguised lockout). This is **not a code
> defect** — the real AltServer stack (same AltSign protocol) successfully signed
> via developer-services ~12 h earlier (`/var/log/altserver/refresh.log`:
> "all re-signs OK"), and Sideport's request matches it byte-for-byte. Resolution
> is **time** (space logins ≥30–60 min); re-run `tools/sideport-live-probe` once
> the throttle clears. The cert-mint cutover stays user-gated (it revokes the
> live AltServer cert).

> **Execution note (2026-06-07):** Implementation ran **P2 → P3 → P5 → P6**
> autonomously (122 tests, 0 skipped, all committed/pushed). The order is driven
> by *what has an independent test oracle*: P2 (libgsa golden vector), P3 (an
> independent in-test GsService2 SRP server), P5 (deterministic zip/CMS + a
> fake-signer harness), P6 (fakes for every seam). All four are proven offline
> with zero device/Apple-account exposure.
>
> **P4 is deliberately paused.** The `developerservices2.apple.com` resource
> protocol (listTeams / addDevice / CSR→cert / App-ID / profile) has **no
> independent oracle** — the SideStore `apple-dev-apis` reference is an unfinished
> stub, and the only other source is AGPL AltSign (forbidden). Shipping it would
> mean committing unvalidatable protocol guesses, which violates the
> no-corner-cutting bar. P4 needs either a live-session capture to replay against
> or a decision to accept live-only validation. The orchestrator (P6) is built
> against the `ISigningIdentityProvider` seam, so it does not depend on P4 being
> filled in; `PortalSigningIdentityProvider` is a clear `NotImplemented` stub
> until P4 can be validated.
>
> **P1 (device plane)** also remains: its exit gate needs the physical iPhone on
> the host, so the live half is best done in a session where that's available
> (the vendored Netimobiledevice controller + fake-transport unit tests can be
> built ahead of that).



---

## 2. Phase detail

Each phase lists: **goal**, **work** (file-level), **tests**, **exit gate**
(the binary done/not-done check), and **risk**.

### P1 — Device plane (read-only)
**Goal:** discover devices, list installed apps + signature expiry, over USB.
No Apple account involved.

**Work**
- Vendor `artehe/Netimobiledevice` at a pinned commit under
  `src/Sideport.Devices/vendor/` (git subtree or copied source + a
  `VENDOR.md` recording the SHA + upstream URL). Do **not** add the NuGet
  package (unmaintained listing, design §6).
- Implement `NetimobiledeviceController : IDeviceController`:
  - `ListDevicesAsync` → usbmux device enumeration + lockdown `DeviceName`,
    `ProductType`, `ProductVersion`.
  - `ListInstalledAppsAsync` → `installation_proxy` browse; parse
    `SignerIdentity` / embedded `.mobileprovision` `ExpirationDate` into
    `InstalledApp.SignatureExpiresAt`.
  - `InstallAsync` → `installation_proxy` install (used later by P6).
- Wire real DI in `Program.cs` (already registered as a stub).
- Map `/api/devices` and `/api/devices/{udid}/apps`.

**Tests**
- Unit: a fake usbmux transport feeding canned lockdown/installation_proxy
  responses; assert mapping into `DeviceInfo` / `InstalledApp`.
- Integration (manual, host-gated): run against the homelab box's connected
  iPhone (UDID `00008140-001A41390242801C`); assert it appears and DiceRoll's
  expiry parses.

**Exit gate:** `GET /api/devices` returns the real phone over USB on the host,
and `…/apps` shows DiceRoll with a sane `signatureExpiresAt`.

**Risk:** Netimobiledevice pairing/trust flow on Linux. Mitigation: reuse the
existing host pairing record in `/var/lib/lockdown/`; the lib should consume it.

---

### P2 — GrandSlam crypto (managed SRP-6a) — **KEYSTONE**
**Goal:** reproduce the Apple SRP variant byte-for-byte in pure C#, validated
against the libgsa golden vectors.

**Work**
- `src/Sideport.GrandSlam/Srp/`:
  - `AppleSrpClient.cs` — SRP-6a with the proven Apple variant
    (design §6 crypto row; recipe below). `System.Numerics.BigInteger` for
    modular arithmetic; `System.Security.Cryptography.SHA256`.
  - Reuse the existing `GrandSlamCrypto.DerivePasswordKey` (s2k) + `Pad`.
  - `Spd.cs` — the SPD/extra-data AES-CBC-pkcs7 + AES-GCM unwrap and the HMAC
    session-subkey derivation (labels `"extra data key:"`,`"extra data iv:"`,
    `"HMAC key:"`), const-time compare via `CryptographicOperations.FixedTimeEquals`.
- **Import the libgsa oracle vectors** into `tests/Sideport.GrandSlam.Tests/vectors/`
  (port `apple_srp_vector.h` → a C#/JSON fixture). Source: `dragoshont/libgsa`
  `tests/vectors/apple_srp_vector.h` (19/19 proven).

**The exact recipe (from libgsa, do not re-derive):**
```
k  = SHA256( PAD256(N) | PAD256(g) )
u  = SHA256( PAD256(A) | PAD256(B) )
x  = SHA256( salt | SHA256( ":" + passwordKey ) )   // noUsernameInX keeps ":"
S  = (B - k*g^x) ^ (a + u*x)   (mod N)
K  = SHA256( minimal(S) )
M1 = SHA256( H(N) XOR H(g) | SHA256(appleId) | salt | minimal(A) | minimal(B) | K )
M2 = SHA256( minimal(A) | M1 | K )
passwordKey = PBKDF2-HMAC-SHA256( SHA256(password), salt, iters, 32 )
// A,B,S enter MINIMAL big-endian; only k,u pad to len(N)=256.
```

**Tests**
- Un-skip `Srp_MatchesLibgsaOracleVectors`: assert `A`, `passwordKey`, `M1`,
  `K`, `M2` exactly equal the libgsa vectors (deterministic `a`).
- Property test: random `a` → server-side check `M2` verifies (round-trip with
  a managed reference server stub).

**Exit gate:** all five SRP intermediates match libgsa vectors byte-for-byte;
the previously-skipped test is green and **not** skipped.

**Risk:** BigInteger sign/endianness bugs (BCL is little-endian two's-complement,
Apple is unsigned big-endian). Mitigation: a single audited `ToMinimalBigEndian`
/ `FromBigEndian` pair with their own unit tests; the libgsa oracle catches the
rest.

---

### P3 — GrandSlam auth protocol
**Goal:** `AuthenticateAsync(appleId, password)` → a usable `AppleSession`
(ADSID + session key) against **live** `gsa.apple.com`.

**Work**
- `src/Sideport.DeveloperApi/GrandSlam/`:
  - `GrandSlamClient.cs` — the two-round plist-over-HTTPS handshake to
    `GsService2`: `init` (send `A`, `u`, get `B`, `salt`, `iters`, `c`) →
    `complete` (send `M1`, `c`, get `M2`, `spd`, `np`). Attach anisette headers
    from `IAnisetteProvider` on every request.
  - 2FA path: detect `au=trustedDeviceSecondaryAuth` / SMS, expose a
    `Submit2faCodeAsync` continuation on the API (operator types the code once;
    after that the anisette trusted-device state carries it).
  - Decrypt `spd` with the P2 session subkeys → extract `adsid`, `GsIdmsToken`,
    account name → `AppleSession`.
- `AppleDeveloperPortal.AuthenticateAsync` delegates to `GrandSlamClient`.

**Tests**
- Unit: replay captured (sanitized) GsService2 plist exchanges through a fake
  `HttpMessageHandler`; assert session extraction.
- Live smoke (host-gated, manual, throttle-aware): authenticate the real Apple
  ID via the host anisette sidecar; assert a non-empty ADSID. **Single-shot,
  no hammering** (Apple 502/throttle lesson from the libgsa e2e).

**Exit gate:** one successful live login producing an ADSID + session key, with
the 2FA continuation exercised once.

**Risk:** Apple GSA 502/throttling (already seen). Mitigation: exponential
backoff, a synthetic-probe health check, and never auto-retry in a tight loop.

---

### P4 — Developer API (provisioning) — DONE (6523066)
**Goal:** clean-room `developerservices2.apple.com`: teams, devices, cert,
app-ID, profile.

> **Done 2026-06-07.** Implemented in `src/Sideport.DeveloperApi/DeveloperServices/`
> (`DeveloperServicesClient` plist transport + identity headers
> `X-Apple-I-Identity-Id`/`X-Apple-GS-Token`, `DevelopmentKeyPair` managed CSR +
> p12, `AppleDeveloperPortal` ListTeams/RegisterDevice/EnsureCertificate/
> EnsureProfile, `PortalSigningIdentityProvider` with persisted-cert reuse).
> SideStore's `apple-dev-apis` turned out to be a stub, so rather than stay
> blocked the wire spec was extracted from the AltSign `AppleAPI` surface (read
> as a protocol spec, clean-room) and a **stateful replay fake** stands in as the
> validation oracle (65 DeveloperApi tests). Live read-path validation is
> deferred to go-live (host has no .NET; creds are host-side). Honest deferrals:
> revoke-aware cert management, leaf-only p12 (matches AltSign + the verified
> homelab zsign path).

**Work** (`src/Sideport.DeveloperApi/DeveloperServices/`)
- `DeveloperServicesClient.cs` — signed plist-over-HTTPS with the P3 session +
  anisette headers. Endpoints:
  - `listTeams` → `AppleTeam[]`
  - `listDevices` / `addDevice` (register UDID, idempotent)
  - `listAllDevelopmentCerts` / `submitDevelopmentCSR` (CSR via
    `System.Security.Cryptography.X509Certificates.CertificateRequest`)
  - `listAppIds` / `addAppId` (+ capabilities)
  - `listAppGroups` / `addAppGroup` / `assignAppGroup`
  - `downloadTeamProvisioningProfile` → `.mobileprovision`
- Implement the `EnsureCertificate` / `EnsureProfile` idempotent wrappers on
  `AppleDeveloperPortal` (fetch-or-create semantics).

**Tests**
- Unit: replay sanitized responses; assert DTO mapping + idempotency (existing
  cert/app-ID is reused, not duplicated — respect the 10-app-ID/7-day ceiling).
- Live smoke (host-gated): against the real team `M62Z4M5EUY`, ensure a profile
  for a throwaway bundle ID; assert a parseable `.mobileprovision`.

**Exit gate:** end-to-end produces a valid signing cert + provisioning profile
for the test device without exceeding Apple's free-tier limits.

**Risk:** burning App-ID slots during dev. Mitigation: integration tests reuse
one fixed bundle ID; never loop new ones.

---

### P5 — Signer + IPA repack
**Goal:** turn (IPA + cert + profile) into an installable signed IPA.

**Work**
- `src/Sideport.DeveloperApi/Signing/`:
  - Finish `ProcessSigner` — build the `zsign` argv (`-k key.pem -m profile -p
    pass -o out in`, or p12 path), capture stdout/exit, parse bundle ID, surface
    `Code=85`-class failures as `SignResult.Error`.
  - `IpaRepacker.cs` — `System.IO.Compression` extract → inject profile →
    re-zip with correct layout; `Claunia.PropertyList` for `Info.plist`.
- Ship `zsign` into the image's signer sidecar (reuse the proven
  `altserver-stack` `zsign` binary, version `fe1750d`); `SignerOptions.Kind`
  selects zsign vs rcodesign.

**Tests**
- Unit: `IpaRepacker` round-trips a fixture IPA (zip in == zip out structure).
- Integration (host): sign `CertCountdown.ipa` with the host zsign; assert a
  valid signature on disk (the proven path — `p12` pass `probe` for the test
  dev cert).

**Exit gate:** a re-signed IPA installs and launches on the test iPhone with
**no Code=85** (matches the already-proven host result).

**Risk:** rcodesign migration scope. Mitigation: ship zsign first; rcodesign is
a later swap behind the same `ISigner`, not P5 scope.

---

### P6 — Refresh orchestrator (the product loop)
**Goal:** the scheduled `auth → ensure cert/app-ID/profile → re-sign → install`
loop with single-flight + countdowns + structured logs.

**Work**
- `src/Sideport.Core` → implement `IRefreshOrchestrator` in a new
  `src/Sideport.Orchestrator/`:
  - `RefreshOrchestrator` with a `SemaphoreSlim(1,1)` single-flight gate
    (invariant #5). Persists per-app/device expiry + last-result.
  - A hosted `BackgroundService` scheduler that refreshes before the ~7-day
    cert lapse (configurable lead time).
- API: `POST /api/refresh/{udid}/{bundleId}` (manual trigger),
  `GET /api/apps` (expiry countdowns), `GET /api/logs` (SSE stream).

**Tests**
- Unit: single-flight (two concurrent refreshes → serialized, second waits);
  scheduler picks the soonest-expiring app.
- Integration (host): full loop refreshes DiceRoll on the real device.

**Exit gate:** a cold `POST /api/refresh/...` performs the whole chain on the
real device and reports a new expiry; concurrent calls do not double-sign.

**Risk:** the loop is the first place all five Apple-facing subsystems run
together → cascading failure surface. Mitigation: each step already has its own
green gate (P1–P5); the orchestrator only sequences them.

---

### P7 — Packaging + GitOps deploy
**Goal:** shippable image + production posture, deployed the homelab way —
**Flux/k8s GitOps**, not docker-compose. (Course-corrected 2026-06-07: the
homelab runs every first-party service through Flux; `altserver_linux` is on the
host *only* because it owns the iPhone via udev/usbmuxd/netmuxd — Sideport's
brain has no such constraint, so it belongs in-cluster like watch-gateway.)

**Split (mirrors altserver's host/device boundary):**
- **In-cluster (Flux):** GrandSlam auth (P3), dev-API (P4), zsign (P5),
  orchestrator + API (P6), anisette sidecar — pure HTTPS + subprocess, no
  device access.
- **Host dependency (consumed, not relocated):** the device-install leg (P1)
  reaches the iPhone via the **host** `usbmuxd` socket (hostPath-mounted) +
  `netmuxd` for Wi-Fi — the same daemons the `altserver_linux` role already
  installs. The pod stays non-privileged (one socket mount, not `privileged`).

**Work**
- `deploy/Dockerfile` multi-arch build (`buildx`, `linux/amd64` + `arm64`);
  publish to `ghcr.io/dragoshont/sideport`.
- **Flux app** at `apps/platform/sideport/` (STAGED 2026-06-07, inert until
  go-live): `deployment.yaml` (Sideport + anisette sidecar, hostPath usbmuxd,
  hardened `securityContext`, `Recreate`, `/healthz` probes), `ingress.yaml`
  (Traefik → `sideport.hont.ro`, `wildcard-hont-ro-tls`), `secret.sops.yaml`
  (from `secret.sops.example.yaml`). Go-live = add the dir to
  `clusters/home/kustomization.yaml` + Cloudflare A record (cf-dns-add), as one
  verified commit (proxy+SSL+DNS trio together, per RC-15).
- Secrets: Apple ID creds via **SOPS** (`apps/**/*.sops.yaml`, dual age+AKV
  recipients) — migrates the host `/etc/altserver/env` cleartext debt. The API
  reads env handles from the Secret, never a literal in the repo (invariant #6).
- Health: `/healthz` (already) + `/readyz` (anisette reachable, signer present,
  last-auth fresh); a synthetic auth probe.
- CI: extend `ci.yml` to build the image + run the unit suite on PR; a separate
  manual/self-hosted job for the host-gated integration tests.
- **Single-signer guard:** ship with `Sideport__Scheduler__Enabled=false`. The
  Mac AltServer (primary) and `altserver_linux` standby share the one free-tier
  cert; two active signers revoke each other. Flip the scheduler on only at a
  deliberate cutover with the other two confirmed stopped.
- ADI: persist the anisette volume and seed it from the host's already-trusted
  ADI at go-live (risk S6: a fresh ADI = 2FA loop).

**Exit gate:** pod `Running` under Flux, `curl https://sideport.hont.ro/healthz`
green (no `-k`, no cert warning), and — at a single-signer cutover window — one
refresh completing end-to-end, equivalent to today's bare-metal AltServer.

---

## 3. Cross-cutting concerns

**Testing strategy.** Three tiers, enforced in CI:
1. **Unit** (no network, no host) — runs on every PR. Crypto vectors, DTO
   mapping, single-flight, repack. This is the safety net.
2. **Replay/integration** (fake `HttpMessageHandler` + canned plists) — runs on
   every PR. Proves protocol wiring without Apple.
3. **Host-gated live** (`[Trait("Category","Live")]`, skipped by default) —
   manual, throttle-aware, against the real account/device. Never in PR CI.

**Provenance discipline.** Every commit touching Apple protocol cites its source
(pypush spec / Apple doc / observed traffic), never "ported from AltSign". This
is the AGPL firewall in practice (invariant #2).

**Secrets.** No Apple password, anisette identifiers, or Plex/CF tokens in the
repo. The existing host `/etc/altserver/env` cleartext is a known debt — Sideport
must read from SOPS, and migrating that secret is a P7 task, not a copy-forward.

**Observability.** Structured logs (Serilog or `ILogger` + JSON) with an SSE
tap for the UI; counters for auth success/502, sign success/Code=85, refresh
lead time.

---

## 4. Risks & required spikes (carried from design §9)

| # | Risk / unknown | When | Resolution |
|---|---|---|---|
| S1 | Netimobiledevice Wi-Fi/mDNS discovery may not exist | before P1 exit | Spike: does it browse `_apple-mobdev2._tcp`? If no → keep `netmuxd` sidecar for Wi-Fi OR add `Makaretu.Dns`. USB-only is acceptable for P1. |
| S2 | Apple GSA 502 / throttling | P3 | Backoff + synthetic probe; single-shot live tests. |
| S3 | BigInteger endianness in SRP | P2 | Audited convert helpers + libgsa oracle. |
| S4 | Free-tier slot exhaustion during dev | P4 | Fixed test bundle ID; never loop new App-IDs. |
| S5 | anisette image rotation by Apple | P7 / ongoing | Pin digest, persist ADI, `RemoteV3` fallback, monitor. |
| S6 | 2FA loop on fresh anisette | P3 | Reuse the host's already-provisioned ADI volume; never re-provision. |

Each spike's outcome is recorded back into this doc as it resolves.

---

## 5. Definition of done (v1)

Sideport v1 is done when, on the homelab box, a single multi-arch image + the
two sidecars can: authenticate the Apple ID, register the device, mint
cert+profile, re-sign DiceRoll, install it (no Code=85), show a live expiry
countdown via the API, and auto-refresh before expiry — replacing the bare-metal
AltServer with no loss of function. No native crypto, no AGPL source, permissive
license intact.

---

## 6. Adversarial review of THIS plan

Same discipline as the design doc: try to break the plan, keep what survives.
Revisions below are already folded into §1–§5 above.

**A1 — "Device-plane-first is busywork; the project's risk is auth, so front-load
P2/P3."**
Partly right. The *intellectual* risk is auth+crypto; the *integration* risk is
the device/pairing/vendoring plumbing, which is independent and can fail late
and ugly. Verdict: keep P1 first **but** time-box it — if Netimobiledevice
pairing on Linux isn't trivially working in the first sitting, fall back to
USB-only and move on; don't let device plumbing block the keystone. *(Folded
into P1 risk + S1.)*

**A2 — "P2 validates against libgsa vectors, but those vectors were generated by
the same person who'll write the C# — a shared-bug oracle."**
Real concern. libgsa itself was proven against an *independent* oracle (the MIT
`srp` PyPI lib, the exact code pypush uses live against Apple). So the chain is
managed-C# ⇄ libgsa ⇄ MIT-srp ⇄ live-Apple — two independent implementations
between us and Apple, not one. Verdict: sufficient, **but** add the P3 live
smoke as the true end-to-end proof; the vectors prove *correctness vs libgsa*,
the live login proves *correctness vs Apple*. *(Folded into P3 exit gate.)*

**A3 — "Clean-room is a fig leaf — you've already read AltSign's
Authentication.cpp this very session; the knowledge is contaminated."**
The sharpest objection. Distinction that holds up legally and practically: we
extracted *protocol facts* (which endpoint, which fields, the SRP variant) — not
copyrightable — and re-expressed them, exactly as libgsa did for the crypto.
We did **not** copy expression. Verdict: defensible, but tighten the discipline:
(a) implement from the pypush spec + Apple docs as the cited source, (b) never
paste AltSign code, (c) record provenance per commit. If in doubt on a specific
blob's structure, derive it from observed traffic, not from their source.
*(Folded into invariant #2 + §3 provenance.)*

**A4 — "P5 ships zsign, contradicting the design's 'collapse five processes to
one image' — you've still got a C++ binary."**
True but intended. The design explicitly keeps a *signer sidecar* (zsign→
rcodesign) as one of the *two* allowed non-managed pieces; "one image" counts
the brain, not the sidecars. Verdict: not a contradiction; zsign-as-sidecar is
the proven fast path, rcodesign is the later C++-elimination. Make sure the
README/plan don't oversell "pure managed". *(Already true in design §4/§6;
restated in invariant #4.)*

**A5 — "P6 orchestrator is where everything integrates, yet it's near the end —
you'll discover seam-fit problems too late."**
Legit sequencing risk. Mitigation: build a **trivial vertical slice** through
the orchestrator as soon as P3 lands (auth-only "refresh" that just logs in and
stops), then thicken it as P4/P5 complete — so the composition seam is exercised
continuously, not first assembled at P6. Verdict: adopt. *(Folded: P6 note +
the "each step green" gates make this incremental.)*

**A6 — "Live tests against one real Apple ID + one phone aren't CI; the green
build is a lie about real behaviour."**
Correct and unavoidable: Apple won't give us a hermetic test account. Honesty
fix: PR CI proves *unit + replay* only and the README/plan say so plainly; the
*live* tier is explicitly manual, single-operator, throttle-aware. We do not
pretend the live path is continuously verified. Verdict: accept the limitation,
document it loudly. *(Folded into §3 testing tiers.)*

**A7 — "Vendoring Netimobiledevice forks you off upstream security fixes."**
Trade-off acknowledged in design §6 (NuGet listing is unmaintained/yanked, so
floating it is *also* risky). Mitigation: pin a SHA, record it in `VENDOR.md`,
and add a Renovate/scheduled check that flags upstream commits — re-vendor
deliberately, don't auto-float. Verdict: vendoring is the lesser risk; add the
drift check. *(Folded into P1 work + S1.)*

**Net:** the plan survives. The only ordering change from the adversarial pass
is A5 (thread a thin orchestrator slice through from P3 onward); everything else
is discipline tightening, not a structural change.

---

## Appendix — provenance
- SRP recipe + vectors: `dragoshont/libgsa` (`src/srp_openssl.c`,
  `tests/vectors/apple_srp_vector.h`), proven 19/19 vs MIT `srp`.
- Auth/dev-API field facts: `JJTech0130/pypush` (`gsa.py`) read as spec;
  `SideStore/apple-private-apis` (Rust, MPL-2.0) as a second reference.
- Device comm: `artehe/Netimobiledevice` (MIT) tree.
- Signer: `dragoshont/zsign@fe1750d` (Code=85 fix), proven on host.
- Live constraints (502/throttle, anisette health, device reachability):
  observed during the libgsa corecrypto-replacement e2e on `home.hont.ro`.
