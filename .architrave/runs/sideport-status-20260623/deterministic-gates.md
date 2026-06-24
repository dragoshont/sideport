# Deterministic Gates

## checks

PASS.

- Quick config/JSON gate passed.
- Full frontend gate passed.
- Build: `npm --prefix src/Sideport.Admin run build` succeeded.
- Test/lint: `npm --prefix src/Sideport.Admin run lint` succeeded.
- Warning: copied Architrave kit assets are stale: repo stamp 0.7.0, installed plugin v0.8.0.

## backend-checks

PASS.

- Backend solution path check passed.
- `dotnet build Sideport.slnx` succeeded.
- `dotnet test Sideport.slnx` succeeded: 223 total, 0 failed, 0 skipped.
- IaC plan `kubectl kustomize deploy/k8s` succeeded.
- IaC policy `kubeconform -summary` succeeded: 4 valid resources.
- Deploy secret scan found no obvious secrets under `deploy`.

## reconcile

Not run. Sideport config does not define designMap/tokens, so reconcile is not meaningful for this audit.

## other

Git status before audit: clean `main...origin/main` at 728e095. After audit: expected new `.architrave/` audit artifacts only.
