# Deterministic Gates

## Sideport

- `dotnet build src/Sideport.Devices/Sideport.Devices.csproj` — PASS after local accessibility repair.
- `dotnet build src/Sideport.Api/Sideport.Api.csproj` — PASS.
- `dotnet test tests/Sideport.Devices.Tests/Sideport.Devices.Tests.csproj` — PASS: 28 total, 0 failed.
- `dotnet test tests/Sideport.Api.Tests/Sideport.Api.Tests.csproj` — PASS: 64 total, 0 failed.
- `dotnet test Sideport.slnx` — PASS: 263 total, 0 failed.
- `git diff --check` — PASS.

## ApprenticeOps

- `python3 -m json.tool data/scenarios.json >/dev/null` — PASS.
- `python3 render_scenarios.py` — PASS: rendered 27 scenarios.
- Custom scenario check: loaded JSON, asserted 27 unique scenario IDs, and ran `run_checks` against the three new gold answers — PASS.
- `git diff --check` — PASS.

## Runtime / Issues

- `gh issue create -R dragoshont/sideport ...` — PASS: https://github.com/dragoshont/sideport/issues/2.
- `gh issue create -R dragoshont/homelab ...` — PASS: https://github.com/dragoshont/homelab/issues/129.
