# Deterministic Gates

## checks

PASS.

- Baseline before close-out: `dotnet test tests/Sideport.Api.Tests/Sideport.Api.Tests.csproj` PASS 37/37.
- Baseline before close-out: `npm --prefix src/Sideport.Admin run build` PASS.
- Baseline before close-out: `npm --prefix src/Sideport.Admin run lint` PASS.
- After first backend change: `dotnet test tests/Sideport.Api.Tests/Sideport.Api.Tests.csproj` PASS 38/38.
- After judge revision: `dotnet test tests/Sideport.Api.Tests/Sideport.Api.Tests.csproj` PASS 39/39.
- After UI changes: `npm --prefix src/Sideport.Admin run build` PASS.
- After UI changes: `npm --prefix src/Sideport.Admin run lint` PASS.
- Full gate after final revision: `gates/checks.sh` PASS.

## backend-checks

PASS.

- `gates/backend-checks.sh` PASS.
- `dotnet build Sideport.slnx` PASS.
- `dotnet test Sideport.slnx` PASS, 235 total, 0 failed, 0 skipped.
- IaC plan `kubectl kustomize deploy/k8s` PASS.
- IaC policy `kubeconform -summary` PASS, 6 valid resources, 0 invalid, 0 errors.
- Deploy secret scan PASS, no obvious secrets under `deploy`.

## reconcile

Not run. Sideport config does not define token/designMap reconciliation paths for
this backend/UI close-out.

## other

- No live Kubernetes apply, Flux reconcile, runtime restart, or secret access was
	performed.
