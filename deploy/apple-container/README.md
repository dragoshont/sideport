# Apple Container — experimental

This is the smallest official-CLI path for Apple silicon on macOS 26 with
Apple `container` 1.1 or newer. Sideport remains a Linux/amd64 image and runs
through Apple's Rosetta support. Anisette remains a separate container on one
explicit network.

The launcher intentionally does not emulate Docker Compose or claim native
arm64 support. It creates two persistent named volumes and forwards the macOS
usbmuxd socket instead of requesting raw USB passthrough.

```sh
export SIDEPORT_API_TOKEN="$(openssl rand -hex 32)"
export SIDEPORT_DEVICE_ID="$(uuidgen)"
export SIDEPORT_PUBLIC_ORIGIN="http://127.0.0.1:8080/"
./deploy/apple-container/sideport-container.sh check
./deploy/apple-container/sideport-container.sh start
```

Back up both named volumes before upgrades. The launcher never prints secret
values. `dry-run` is safe on a host without the Apple runtime and documents the
effective topology.

Physical Apple Container device installation is still experimental until it is
run on an eligible Apple-silicon/macOS 26 host. A missing runtime or version
older than 1.1 fails closed.
