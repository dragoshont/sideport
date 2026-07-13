# Sideport on Kubernetes

A minimal, hardened example: **Deployment + Service + Ingress + persistent
Sideport/anisette volumes + an anisette sidecar**, running as a non-root user
(`securityContext` drops all capabilities, read-only root FS) with the node's
`usbmuxd` socket mounted for the device-install leg. Adapt the host, TLS,
StorageClass, and secret delivery to your cluster.

## Render and review

```bash
kubectl kustomize deploy/k8s > /tmp/sideport-k8s-rendered.yaml
kubeconform -summary /tmp/sideport-k8s-rendered.yaml
```

This repository is plan-only. A human-owned environment overlay and reviewed
GitOps merge are the apply boundary. Deliver secrets through that environment's
existing SOPS, sealed-secret, or external-secret workflow; do not create a
plaintext `secret.yaml` from this repository.

Smoke test once the pod is `Running`:

```bash
kubectl -n sideport port-forward deploy/sideport 8080:8080 &
curl -fsS localhost:8080/healthz     # liveness
curl -fsS localhost:8080/readyz      # anisette reachable + signer present
```

Use the TLS ingress, not the HTTP port-forward, for first-run Apple credential
entry. Sign in (or provide the bearer token), then Sideport guides Apple sign-in,
iPhone Trust over USB, app selection, install, and automatic refresh enablement
inside the UI. Apple credentials do not belong in the Kubernetes Secret.

## Authentik and passkeys

The example now shows the required Sideport OIDC relying-party values, exact
forwarded-header trust, and the optional new-account enrollment adapter. Replace
every example hostname, flow UUID, client ID, and proxy CIDR before rendering a
real overlay. Keep the OIDC client secret and Authentik API token in your secret
manager.

Apply [`../authentik/sideport-blueprint.yaml`](../authentik/sideport-blueprint.yaml)
to Authentik only through your normal reviewed Authentik/GitOps process. It
creates an invitation-only external-user flow that requires a passkey before
login completion. Sideport never stores or reads that passkey. Existing users
can sign in through ordinary Authentik OIDC without the optional API adapter.

Traefik must terminate HTTPS and send `X-Forwarded-For`,
`X-Forwarded-Proto=https`, and `X-Forwarded-Host`. Sideport consumes those
headers only when the direct peer matches `KnownProxies` or `KnownNetworks`.
Use the narrowest real value; the example cluster CIDR is not a safe universal
default.

## Before you turn it on

- **One signer per Apple ID.** Confirm no AltStore/AltServer or second Sideport
  is active for the same Apple ID — two active signers revoke each other's
  certificate. The manifest's scheduler value is only a bootstrap request;
  durable scheduling remains disabled until onboarding verifies the signer,
  installed app, and paired iPhone.
- **Persistent state.** The example declares `sideport-state` (2 GiB) and
  `sideport-anisette` (512 MiB) PVCs. `/var/lib/sideport` remains writable even
  with the read-only root filesystem and holds managed credential protection
  keys, registrations, stored IPAs, operation history, and scheduler evidence.
  Back up both PVCs together. Seed anisette only when deliberately migrating an
  already-trusted identity.
- **Device reachability.** The pod reaches the iPhone through the node's
  `usbmuxd` socket (USB, or netmuxd for Wi-Fi). On a multi-node cluster, pin the
  pod to the node where the phone is paired.
- **Pairing records.** `/var/lib/lockdown` is host trust material, not ordinary
  app data. For USB-only installs, the `usbmuxd` socket is enough. If the host
  provides Wi-Fi pairing from this directory, uncomment the optional hostPath
  and mount it read-only on the node that owns the pairing.

See the repo [README](../../README.md) for the full configuration reference.
