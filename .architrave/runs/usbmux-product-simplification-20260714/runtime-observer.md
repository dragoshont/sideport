# Runtime and Visual Evidence

## Production transport evidence

The Ubuntu host runs `usbmuxd 1.1.1` with `--systemd` and without
`--no-preflight`, so host preflight is currently enabled. During the captured
Trust attempt, host logs repeatedly reported the absent pairing record from
18:42:15–18:42:16 EEST; the record was created at 18:42:19, the same transition
in which Sideport captured `UsbmuxException`, and passive installed-app reads
succeeded seconds later. This supports the competing-pairing-owner hypothesis.

The current homelab manifest bind-mounts the `/var/run/netmuxd` socket inode into
the pod. A later deployment slice must move to a narrowly dedicated parent
directory mount so socket recreation is visible without remounting the pod.

## Storybook visual evidence

The canonical owner Home and Apps screens were inspected at a 390 × 844 browser
viewport. The first pass revealed the account avatar wrapping onto a second
header row because five controls occupied a four-column grid. The grid was
corrected and reverified.

The accepted mock shows:

- one-line mobile header with logo, search, Add, attention, and account;
- Home with one actionable attention row, drillable iPhones, and quiet history;
- Apps with Your apps/Browse, search, owner-only Manage sources, and full-row
  drill-down;
- four-item mobile navigation: Home, Apps, Devices, People;
- no metric dashboard cards and no primary Activity/Settings competition.

This is Storybook-only. The deployed runtime still uses the older operator
console until the separate runtime migration phase passes its own gates.
