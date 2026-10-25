# Tournament of Options

## Device transport

| Option | Reliability | Safety | Cost | Decision |
| --- | ---: | ---: | ---: | --- |
| Add blind retries | 2 | 1 | 4 | Reject: amplifies duplicate pairing and unknown installs. |
| Leave host and Sideport pairing active | 1 | 1 | 5 | Reject: two potential pairing owners. |
| Host-only pairing | 4 | 3 | 4 | Supported deployment mode, but passive cable attach may prompt Trust. |
| Sideport-only pairing | 5 | 5 | 3 | Target: preserves explicit Add iPhone authorization. |
| Surgical current-stack hardening | 4 | 5 | 4 | Select first: ownership, discovery, typed failures, timeouts. |
| Immediate Netimobiledevice major upgrade | 4 | 2 | 1 | Separate compatibility spike; too broad for the incident fix. |

## Product structure

| Option | Clarity | Drill-down | Cost | Decision |
| --- | ---: | ---: | ---: | --- |
| Restyle current dashboard/cards | 2 | 2 | 4 | Reject: preserves overlapping responsibilities. |
| Separate Apps and Store destinations | 3 | 3 | 3 | Reject: duplicates the same library. |
| Four primary destinations plus secondary inbox/settings | 5 | 5 | 4 | Select. |
| Replace runtime with the existing canonical mock | 3 | 2 | 2 | Reject: the mock contains dead controls and contradictory policy. |
| Port approved patterns into live-bound components, then delete the parallel mock | 5 | 5 | 3 | Target migration. |

## YAGNI result

Reuse the durable operation model, current React components, Storybook, existing
device interfaces, and installed dependencies. The first slice adds no new
package, privileged helper, or alternate device stack.
