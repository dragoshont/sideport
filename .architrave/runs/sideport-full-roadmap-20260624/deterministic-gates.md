# Deterministic Gates

## checks

PASS.

- `git diff --check`: PASS.
- `harness/validate-run.sh .architrave/runs/sideport-full-roadmap-20260624`: PASS.
- `gates/checks.sh`: PASS.
- Final `gates/checks.sh`: PASS.
- Final `npm --prefix src/Sideport.Admin run test:screens`: PASS, 20/20. Vite
	proxy emitted expected `ECONNREFUSED 127.0.0.1:8080` warnings because no live
	API was running; unavailable/fallback states still passed.
- Final `git diff --check`: PASS.
- Final editor diagnostics for changed admin/API files: PASS, no errors.

## backend-checks

PASS.

- `gates/backend-checks.sh`: PASS.
- `dotnet build Sideport.slnx`: PASS.
- `dotnet test Sideport.slnx`: PASS, 258 total, 0 failed.
- Final `dotnet build Sideport.slnx`: PASS, 0 warnings, 0 errors.
- IaC plan `kubectl kustomize deploy/k8s`: PASS.
- IaC policy `kubeconform -summary`: PASS, 6 valid, 0 invalid, 0 errors.
- Deploy secret scan: PASS.

## reconcile

Not run. Sideport has no configured token/design-map reconcile path for this
contract/backend phase.

## other

- No live Kubernetes apply, Flux reconcile, runtime restart, network mutation, or
	secret access performed.
