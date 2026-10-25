# Vendored: Netimobiledevice

- **Upstream:** https://github.com/artehe/Netimobiledevice (MIT)
- **Pinned tag:** `v2.5.2`
- **Pinned SHA:** `c442a418c522327a8995057b1705afac182a3e69`
- **Vendored:** 2026-07-05
- **License:** MIT — the upstream `LICENSE` (Copyright © 2023 artehe) is vendored
  in this directory alongside the source.

The design (`docs/sideport-implementation-plan.md`) calls for vendoring the device
library at a pinned SHA rather than floating the NuGet feed (flagged unmaintained;
NuGet tops out at 2.5.2). This is that vendored copy of the upstream
`Netimobiledevice/` library project (the Demo/Test projects are not vendored). It
is consumed as a `ProjectReference` from `Sideport.Devices`.

## Local patches (vs. the pinned upstream)

1. **`Usbmuxd/UsbmuxdDevice.cs` — Linux usbmux sockaddr family-byte offset (IPv4)**
   ([issue #4](https://github.com/dragoshont/sideport/issues/4)).
   Upstream reads the sockaddr address family at byte **[1]** on any non-Windows OS
   (the BSD/macOS `struct sockaddr_in { uint8 sin_len; uint8 sin_family; ... }`
   layout). Linux `sockaddr_in` has a 16-bit `sin_family` and **no** `sin_len`, so
   the family byte is at **[0]** (`AF_INET` ⇒ `02 00 …`). `usbmuxd`/`netmuxd` run on
   Linux, so their `Network` devices carry the Linux layout and were wrongly rejected
   with `NotImplementedException("Network address is not supported.")`. Patched the
   OS check to also read byte [0] on Linux; the IPv4 (4–7) and IPv6 (8–23) address
   payload offsets are already identical across the three OSes.

2. **`Usbmuxd/UsbmuxdDevice.cs` — Linux `AF_INET6` family value (IPv6).**
   The IPv6 branch matched only `0x1e` (BSD/macOS `AF_INET6` = 30) and `23`
   (Windows/.NET `InterNetworkV6`). **Linux `AF_INET6` = 10 (`0x0a`)** was unmatched,
   so a Wi-Fi device advertised with an IPv6 (e.g. link-local `fe80::`) address would
   `throw` on Linux — the same OS mismatch as #1, one layer down. Added `0x0a`.

3. **`Usbmuxd/PlistMuxConnection.cs` — per-device parse guard.**
   `UpdateDeviceList` constructed each `UsbmuxdDevice` with no try/catch, so a single
   unparseable record (unexpected sockaddr family, short `NetworkAddress`) aborted the
   **entire** `ListDevices` response — taking down every device operation, not just the
   offending device. Wrapped the per-device construct in try/catch (log + skip).
   The parallel `UsbmuxdConnectionMonitor.Subscribe` path parses a sockaddr the same
   way and is **left unpatched on purpose**: Sideport enumerates only via
   `Usbmux.GetDeviceList` → `UpdateDeviceList` and never subscribes, so that path is
   unreachable here.

4. **`Netimobiledevice.csproj`** — `GeneratePackageOnBuild` and
   `EnforceCodeStyleInBuild` set to `false` (we consume it as a `ProjectReference`,
   not a packed NuGet).

5. **`Usbmuxd/UsbmuxdSocket.cs` and `Usbmuxd/UsbmuxConnection.cs` — exact stream
   framing.** Unix and TCP stream sockets may return partial reads or writes.
   Upstream used one `Socket.Receive`/`Socket.Send` call and the payload retry
   loop replaced earlier chunks instead of appending them. Sideport now loops
   until the requested frame is complete, fails explicitly on EOF, and reads a
   payload exactly once through that bounded primitive.

## Re-vendoring (do it deliberately, never auto-float)

```sh
git clone --depth 1 --branch <tag> https://github.com/artehe/Netimobiledevice.git /tmp/nimd
rm -rf src/Sideport.Devices/vendor/Netimobiledevice
cp -R /tmp/nimd/Netimobiledevice src/Sideport.Devices/vendor/Netimobiledevice
cp /tmp/nimd/LICENSE src/Sideport.Devices/vendor/Netimobiledevice/LICENSE   # MIT notice
# re-apply the patches above, update the tag/SHA here, rebuild + run the device tests
```
