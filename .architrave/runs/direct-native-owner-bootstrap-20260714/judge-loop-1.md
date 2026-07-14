# Judge Loop 1

Copilot verdict: **REVISE**.

Findings:

1. The existing root redirect test was not named in the evidence artifact.
2. Rate-limit and recovery-link preservation evidence was too implicit.
3. The judge interpreted the deployment requirement to remain private/LAN-only
   as an application-level IP restriction.

Remediation:

- Added a native-mode root redirect test.
- Added a direct-bootstrap rate-limit test.
- Added explicit System audit assertions and retained recovery-claim denial.
- Added `threat-model.md` clarifying that Sideport cannot truthfully infer LAN
  topology behind arbitrary ingress/proxies; network privacy is a deployment
  precondition, while the endpoint itself discloses no authority.
- Re-ran the expanded focused slice: 158/158 PASS.
