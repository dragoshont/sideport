# Deterministic Gates

## checks

PASS.

- `gates/checks.sh`: PASS.
- `npm --prefix src/Sideport.Admin run build`: PASS.
- `npm --prefix src/Sideport.Admin run lint`: PASS.
- Warning: copied Architrave kit assets are stale (repo stamp 0.7.0, installed plugin v0.8.0). This is non-blocking gate output.

## backend-checks

PASS.

- `gates/backend-checks.sh`: PASS.
- Backend solution path check: PASS.
- `dotnet build Sideport.slnx`: PASS.
- `dotnet test Sideport.slnx`: PASS, 233 total, 0 failed, 0 skipped.
- IaC plan `kubectl kustomize deploy/k8s`: PASS.
- IaC policy `kubeconform -summary`: PASS, 6 resources valid.
- Deploy secret scan: PASS, no obvious secrets under `deploy`.

## reconcile

Not run. Sideport config has no token/designMap paths for reconcile.

## other

- Targeted API tests: `dotnet test tests/Sideport.Api.Tests/Sideport.Api.Tests.csproj` PASS, 37 total.
- Targeted frontend build/lint PASS after final label cleanup.
- Targeted k8s render/policy PASS after PVC changes.
