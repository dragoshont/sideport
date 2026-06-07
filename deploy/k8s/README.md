# Sideport on Kubernetes

A minimal, hardened example: **Deployment + Service + Ingress + an anisette
sidecar**, running as a non-root user (`securityContext` drops all capabilities,
read-only root FS) with the node's `usbmuxd` socket mounted for the
device-install leg. It's the generic version of the same shape used in
production — adapt the host, TLS, storage, and secret delivery to your cluster.

## Apply

```bash
# 1. Create the secret from the template (edit the values first):
cp secret.example.yaml secret.yaml
$EDITOR secret.yaml
kubectl create namespace sideport
kubectl apply -n sideport -f secret.yaml

# 2. Deploy everything else:
kubectl apply -k .
```

(Or wire `secret.yaml` into `kustomization.yaml`, or use sealed-secrets / SOPS /
an external secret manager — never commit real credentials.)

Smoke test once the pod is `Running`:

```bash
kubectl -n sideport port-forward deploy/sideport 8080:8080 &
curl -fsS localhost:8080/healthz     # liveness
curl -fsS localhost:8080/readyz      # anisette reachable + signer present
```

## Before you turn it on

- **One signer per Apple ID.** Keep `Sideport__Scheduler__Enabled: "false"`
  until you've confirmed no other signer (AltStore/AltServer or a second
  Sideport) is active for the same Apple ID — two active signers revoke each
  other's certificate. Flip it to `"true"` to let Sideport keep apps signed.
- **anisette identity.** The example mounts an `emptyDir`, so the first login
  provisions a fresh Apple machine identity and prompts for **2FA once**. To
  inherit existing trust (no 2FA) and survive restarts, back the `anisette-data`
  volume with a PVC and seed it from an already-trusted ADI (see the commented
  block in `deployment.yaml`).
- **Device reachability.** The pod reaches the iPhone through the node's
  `usbmuxd` socket (USB, or netmuxd for Wi-Fi). On a multi-node cluster, pin the
  pod to the node where the phone is paired.
- **Rename the password env var** in `deployment.yaml` to match your Apple ID:
  `SIDEPORT_APPLE_PW_<APPLE_ID>`, uppercased, every non-alphanumeric char → `_`
  (e.g. `you@example.com` → `SIDEPORT_APPLE_PW_YOU_EXAMPLE_COM`).

See the repo [README](../../README.md) for the full configuration reference.
