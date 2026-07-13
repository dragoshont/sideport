# Phase 5 — GitHub Release Sources Gate

Date: 2026-07-11
Verdict: PASS after repair

## Delivered

- Public repository validation and redacted release/IPA asset discovery.
- Private selected-repository GitHub App setup with hashed 256-bit, five-minute,
  single-use state and fixed-origin callback handling.
- Exact Metadata-read and Contents-read permission checks, short-lived App JWT,
  repository-scoped installation tokens, and in-memory token eviction.
- Fixed GitHub API paths, manual redirect validation, pinned public DNS
  connection, cross-host authorization stripping, byte/time/digest limits, and
  temporary-file cleanup.
- GitHub assets feed the existing bounded IPA inspector and immutable catalog
  publisher; exact and concurrent replay performs no second download.
- Public endpoints and errors omit credentials, signed URLs, response bodies,
  host paths, and private-key details. GitHub reads are protected in open mode
  because private repository names are not public catalog metadata.

## Deterministic evidence

Command: `dotnet test Sideport.slnx --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false`

- Sideport.Api.Tests: 139 passed
- Sideport.DeveloperApi.Tests: 89 passed
- Sideport.Devices.Tests: 63 passed
- Sideport.GrandSlam.Tests: 50 passed
- Sideport.Orchestrator.Tests: 45 passed
- Total: 386 passed, 0 failed, 0 skipped
- `git diff --check`: PASS

The full solution command required an unsandboxed local run because vstest must
bind its loopback coordination socket; no external network was used by tests.

## Semantic evidence

Initial adversarial verdict: FAIL — IPv6 NAT64/6to4/Teredo-style destinations
could pass the public-address test.

Repair: centralized conservative address classification, transition/special
range rejection, production `ConnectCallback` mixed-DNS coverage, actor/key
single-flight, concurrent replay coverage, and fixed callback failure tests.

Final independent adversarial verdict: PASS; no remaining Phase 5 release
blocker.
