#!/usr/bin/env bash
set -Eeuo pipefail
SAEL_DIR=${SAEL_DIR:-/opt/sael}
: "${SAEL_BACKEND_PORT:?Set SAEL_BACKEND_PORT}"
PREVIOUS_IMAGE=${1:-$(cat /var/lib/sael/previous-image 2>/dev/null || true)}
test -n "$PREVIOUS_IMAGE" || { echo 'No previous image recorded.' >&2; exit 1; }
[[ "$PREVIOUS_IMAGE" =~ ^[a-zA-Z0-9._/:@-]+$ ]] || { echo 'Invalid image tag.' >&2; exit 1; }
cd "$SAEL_DIR/backend"
export SAEL_ENV_FILE=${SAEL_ENV_FILE:-/etc/sael/backend.env}
SAEL_IMAGE="$PREVIOUS_IMAGE" docker compose config --quiet
SAEL_IMAGE="$PREVIOUS_IMAGE" docker compose up -d --no-build sael-evidence
curl --fail --silent --show-error --max-time 5 "http://127.0.0.1:${SAEL_BACKEND_PORT}/health"
