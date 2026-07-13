# Phase 15 Options

| Option | Result | Decision |
| --- | --- | --- |
| Keep the old Compose tutorial and document exceptions | Fresh installs remain broken | Rejected |
| Introduce a new installer/orchestrator | Unnecessary dependency and abstraction | Rejected |
| Repair Compose, add one official-CLI Apple launcher, and reuse current image/config contracts | Smallest complete packaging fix | Selected |

Apple Container remains an explicit experimental path until executed on an
eligible Apple-silicon/macOS 26 host with Apple `container` 1.1+.
