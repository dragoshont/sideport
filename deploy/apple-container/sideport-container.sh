#!/bin/sh
set -eu

MIN_VERSION=1.1.0
NETWORK=${SIDEPORT_CONTAINER_NETWORK:-sideport}
SIDEPORT_NAME=${SIDEPORT_CONTAINER_NAME:-sideport}
ANISETTE_NAME=${ANISETTE_CONTAINER_NAME:-sideport-anisette}
SIDEPORT_VOLUME=${SIDEPORT_STATE_VOLUME:-sideport-state}
ANISETTE_VOLUME=${ANISETTE_STATE_VOLUME:-sideport-anisette}
SIDEPORT_IMAGE=${SIDEPORT_IMAGE:-ghcr.io/dragoshont/sideport:0.1.12}
ANISETTE_IMAGE=${ANISETTE_IMAGE:-dadoum/anisette-v3-server:latest}
USBMUXD_SOCKET=${USBMUXD_SOCKET:-/var/run/usbmuxd}

usage() {
  echo "usage: $0 check|dry-run|start|status|stop" >&2
  exit 2
}

version_ge() {
  awk -v minimum="$MIN_VERSION" -v actual="$1" '
    function parts(value, out) { return split(value, out, ".") }
    BEGIN {
      parts(minimum, min); parts(actual, got)
      for (i = 1; i <= 3; i++) {
        if ((got[i] + 0) > (min[i] + 0)) exit 0
        if ((got[i] + 0) < (min[i] + 0)) exit 1
      }
      exit 0
    }'
}

require_runtime() {
  command -v container >/dev/null 2>&1 || {
    echo "Apple container CLI is required (version $MIN_VERSION or newer)." >&2
    exit 1
  }
  raw=$(container --version 2>/dev/null || true)
  version=$(printf '%s' "$raw" | sed -nE 's/.*([0-9]+\.[0-9]+\.[0-9]+).*/\1/p' | head -n 1)
  [ -n "$version" ] && version_ge "$version" || {
    echo "Apple container CLI $MIN_VERSION or newer is required; found: ${raw:-unknown}." >&2
    exit 1
  }
}

require_config() {
  : "${SIDEPORT_API_TOKEN:?set SIDEPORT_API_TOKEN without printing it}"
  : "${SIDEPORT_DEVICE_ID:?set SIDEPORT_DEVICE_ID to a stable UUID}"
  : "${SIDEPORT_PUBLIC_ORIGIN:=http://127.0.0.1:8080/}"
  : "${SIDEPORT_IDENTITY_MODE:=passkey}"
  case "$SIDEPORT_IDENTITY_MODE" in
    passkey) ;;
    oidc)
      : "${SIDEPORT_OIDC_AUTHORITY:?set SIDEPORT_OIDC_AUTHORITY for oidc mode}"
      : "${SIDEPORT_OIDC_CLIENT_ID:?set SIDEPORT_OIDC_CLIENT_ID for oidc mode}"
      : "${SIDEPORT_OIDC_CLIENT_SECRET:?set SIDEPORT_OIDC_CLIENT_SECRET for oidc mode}"
      ;;
    *) echo "SIDEPORT_IDENTITY_MODE must be passkey or oidc." >&2; exit 1 ;;
  esac
  [ -S "$USBMUXD_SOCKET" ] || {
    echo "macOS usbmuxd socket is not available at $USBMUXD_SOCKET." >&2
    exit 1
  }
}

print_plan() {
  cat <<EOF
runtime: Apple container >= $MIN_VERSION
network: $NETWORK
sideport: $SIDEPORT_NAME ($SIDEPORT_IMAGE, linux/amd64 through Rosetta)
anisette: $ANISETTE_NAME ($ANISETTE_IMAGE)
state volumes: $SIDEPORT_VOLUME, $ANISETTE_VOLUME
device socket: $USBMUXD_SOCKET -> /var/run/usbmuxd
public origin: ${SIDEPORT_PUBLIC_ORIGIN:-http://127.0.0.1:8080/}
identity mode: ${SIDEPORT_IDENTITY_MODE:-passkey}
secrets: supplied through environment; values are never printed
EOF
}

start() {
  require_runtime
  require_config
  container network create "$NETWORK" >/dev/null 2>&1 || true
  container volume create "$SIDEPORT_VOLUME" >/dev/null 2>&1 || true
  container volume create "$ANISETTE_VOLUME" >/dev/null 2>&1 || true

  container run --detach --name "$ANISETTE_NAME" --network "$NETWORK" \
    --volume "$ANISETTE_VOLUME:/home/Alcoholic/.config/anisette-v3" \
    "$ANISETTE_IMAGE"

  container run --detach --name "$SIDEPORT_NAME" --network "$NETWORK" \
    --arch amd64 --publish 127.0.0.1:8080:8080 \
    --env "Sideport__Anisette__Url=http://$ANISETTE_NAME:6969/" \
    --env "Sideport__Api__AuthToken=$SIDEPORT_API_TOKEN" \
    --env "Sideport__Identity__Mode=$SIDEPORT_IDENTITY_MODE" \
    --env "Sideport__Oidc__Authority=${SIDEPORT_OIDC_AUTHORITY:-}" \
    --env "Sideport__Oidc__ClientId=${SIDEPORT_OIDC_CLIENT_ID:-}" \
    --env "Sideport__Oidc__ClientSecret=${SIDEPORT_OIDC_CLIENT_SECRET:-}" \
    --env "Sideport__Identity__ProviderLabel=${SIDEPORT_IDENTITY_PROVIDER_LABEL:-Your account}" \
    --env "Sideport__Identity__LoginLabel=${SIDEPORT_IDENTITY_LOGIN_LABEL:-Continue to sign in}" \
    --env "Sideport__Apple__DeviceId=$SIDEPORT_DEVICE_ID" \
    --env "Sideport__Apple__CredentialSource=managed" \
    --env "Sideport__Apple__AllowInsecureCredentialEntryOnLoopback=true" \
    --env "Sideport__PublicOrigin=$SIDEPORT_PUBLIC_ORIGIN" \
    --env "Sideport__State__Directory=/var/lib/sideport" \
    --env "Sideport__Orchestrator__WorkDirectory=/var/lib/sideport/signed" \
    --env "Sideport__Orchestrator__InstallTimeout=${SIDEPORT_INSTALL_TIMEOUT:-00:03:00}" \
    --env "Sideport__Orchestrator__InstallCancellationGrace=${SIDEPORT_INSTALL_CANCELLATION_GRACE:-00:00:02}" \
    --env "Sideport__Devices__PairingRecordsDir=/var/lib/lockdown" \
    --env "USBMUXD_SOCKET_ADDRESS=unix:/var/run/usbmuxd" \
    --volume "$SIDEPORT_VOLUME:/var/lib/sideport" \
    --volume "$USBMUXD_SOCKET:/var/run/usbmuxd" \
    "$SIDEPORT_IMAGE"
}

case "${1:-}" in
  check) require_runtime; print_plan ;;
  dry-run) print_plan ;;
  start) start ;;
  status) require_runtime; container list --all ;;
  stop)
    require_runtime
    container stop "$SIDEPORT_NAME" >/dev/null 2>&1 || true
    container stop "$ANISETTE_NAME" >/dev/null 2>&1 || true
    ;;
  *) usage ;;
esac
