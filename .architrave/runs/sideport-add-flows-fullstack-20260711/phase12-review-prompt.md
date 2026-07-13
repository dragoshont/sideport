You are an independent adversarial reviewer. Review the current in-progress
Sideport Phase 12 implementation only. It is not being claimed complete.

Inspect AGENTS.md, gates/rubric.md, the phase ledger, phase12-intake.md,
phase12-options.md, docs/sideport-backend-contract.md, and the actual diff for:
- src/Sideport.Core/IAppleDeveloperPortal.cs
- src/Sideport.Core/ISigningIdentityProvider.cs
- src/Sideport.DeveloperApi/AppleDeveloperPortal.cs
- src/Sideport.DeveloperApi/PortalSigningIdentityProvider.cs
- src/Sideport.Orchestrator/IAppRegistry.cs
- src/Sideport.Orchestrator/FileAppRegistry.cs
- src/Sideport.Api/AppleAccess/PersonalAppleAccess.cs
- src/Sideport.Api/AppleAccess/SigningCutoverService.cs
- src/Sideport.Api/Operations/OperationContracts.cs
- src/Sideport.Api/Program.cs
- src/Sideport.Api/WorkspaceAccess/WorkspaceApiPolicy.cs
- relevant tests and runtime UI.

Focus on destructive certificate safety, lock sharing, exact acknowledgement,
idempotency conflict, restart recovery, crash windows, durable intent, team
migration ordering, registration atomicity, capability/CSRF/transport boundary,
and UI capability honesty. Current tests: build clean; Developer API 101/101;
Orchestrator 54/54; API 470/470; Devices 64/64; GrandSlam 50/50; Storybook
85/85; Playwright 14/14.

Return findings ordered by severity and an explicit VERDICT. Do not edit files.
