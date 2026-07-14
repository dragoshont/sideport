# Recommended Plan

1. Make pairing ownership explicit and durable. Preserve compatibility in code,
   then configure the Linux production host deliberately after physical proof.
2. Split transport-only enumeration from lockdown enrichment and use one fresh
   passive probe per recovery attempt.
3. Introduce typed transport/pairing dispositions and focused regression tests;
   keep the existing five-minute operation boundary.
4. Build the simplified product shell in Storybook only: Home, Apps, Devices,
   People; secondary Activity and Settings; drillable rows/details everywhere.
5. Review the mock, then port the approved structure into runtime components
   with real semantic URLs and live People/App/Device contracts.
6. Harden socket framing/timeouts and use a dedicated socket-directory mount.
7. Run deterministic and adversarial gates, then execute the physical iPhone
   matrix before GitOps deployment.
