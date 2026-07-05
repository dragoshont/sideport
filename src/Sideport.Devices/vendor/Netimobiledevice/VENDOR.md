# Vendored: Netimobiledevice

- **Upstream:** https://github.com/artehe/Netimobiledevice (MIT)
- **Pinned tag:** `v2.5.2`
- **Pinned SHA:** `c442a418c522327a8995057b1705afac182a3e69`
- **Vendored:** 2026-07-05

The design (`docs/sideport-implementation-plan.md`) calls for vendoring the device
library at a pinned SHA rather than floating the NuGet feed (flagged unmaintained;
NuGet tops out at 2.5.2). This is that vendored copy of the upstream
`Netimobiledevice/` library project (the Demo/Test projects are not vendored). It
is consumed as a `ProjectReference` from `Sideport.Devices`.

## Local patches (vs. the pinned upstream)

1. **`Usbmuxd/UsbmuxdDevice.cs` — Linux usbmux sockaddr family-byte offset**
   ([issue #4](https://github.com/dragoshont/sideport/issues/4)).
   Upstream reads the sockaddr address family at byte **[1]** on any non-Windows OS
   (the BSD/macOS `struct sockaddr_in { uint8 sin_len; uint8 sin_family; ... }`
   layout). Linux `sockaddr_in` has a 16-bit `sin_family` and **no** `sin_len`, so
   the family byte is at **[0]** (`AF_INET` ⇒ `02 00 …`). `usbmuxd`/`netmuxd` run on
   Linux, so their `Network` devices carry the Linux layout and were wrongly rejected
   with `NotImplementedException("Network address is not supported.")`. Patched the
   OS check to also read byte [0] on Linux; the IPv4 (4–7) and IPv6 (8–23) payload
   offsets are already identical on Linux.

2. **`Netimobiledevice.csproj`** — `GeneratePackageOnBuild` and
   `EnforceCodeStyleInBuild` set to `false` (we consume it as a `ProjectReference`,
   not a packed NuGet).

## Re-vendoring (do it deliberately, never auto-float)

```sh
git clone --depth 1 --branch <tag> https://github.com/artehe/Netimobiledevice.git /tmp/nimd
rm -rf src/Sideport.Devices/vendor/Netimobiledevice
cp -R /tmp/nimd/Netimobiledevice src/Sideport.Devices/vendor/Netimobiledevice
# re-apply the patches above, update the tag/SHA here, rebuild + run the device tests
```
