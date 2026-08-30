#!/usr/bin/env bash
set -Eeuo pipefail
SAEL_DIR=${SAEL_DIR:-/opt/sael}
: "${SAEL_BACKEND_PORT:?Set SAEL_BACKEND_PORT}"
cd "$SAEL_DIR/backend"
export SAEL_ENV_FILE=${SAEL_ENV_FILE:-/etc/sael/backend.env}
docker compose ps
curl --fail --silent --show-error --max-time 5 "http://127.0.0.1:${SAEL_BACKEND_PORT}/health"
