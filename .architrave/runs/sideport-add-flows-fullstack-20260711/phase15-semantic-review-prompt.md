You are the independent adversarial judge for Sideport Phase 15.
Review actual repo state against AGENTS.md, architrave.config.json,
gates/rubric.md, phase15-intake/options/gates, phase-ledger.md, the backend/UI
contracts, and the packaging diff.

Acceptance criteria:
1. Docker Compose is a real clean install: durable Sideport + anisette state,
   baked signer retained, usbmux socket and read-only pairing records, managed
   credential entry, fixed public origin, non-root/read-only/no-new-privileges.
2. Existing install timeout/grace are configurable without weakening the
   unknown/reconciliation contract.
3. Dockerfile signer source is immutable; CI emits SBOM/provenance.
4. Apple Container uses only official CLI concepts, explicit network, two
   volumes, amd64/Rosetta request, host usbmux socket, no secret printing, and
   fails closed below 1.1/missing runtime.
5. Apple Container physical support is not claimed on this Intel Mac without
   the official runtime.
6. Actual RC image build, clean-volume startup, UID 1000, read-only root,
   restart persistence, health, and production dependency audit are evidenced.
7. Migrated legacy registrations fail closed rather than mutating the phone.
8. No hidden production apply; old deployment restored after isolated checks.

Return full rubric and VERDICT PASS/REVISE/FAIL. Any secret leak, broken fresh
install, false Apple Container claim, mutable signer source, or lost state is a
blocker. Do not edit files.
