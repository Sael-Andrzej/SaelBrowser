#!/usr/bin/env bash
set -Eeuo pipefail
SAEL_DIR=${SAEL_DIR:-/opt/sael}
: "${SAEL_BACKEND_PORT:?Set SAEL_BACKEND_PORT}"
cd "$SAEL_DIR/backend"
export SAEL_ENV_FILE=${SAEL_ENV_FILE:-/etc/sael/backend.env}
docker compose logs --tail "${SAEL_LOG_LINES:-200}" sael-evidence
