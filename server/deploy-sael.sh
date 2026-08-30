#!/usr/bin/env bash
set -Eeuo pipefail

SAEL_DIR=${SAEL_DIR:-/opt/sael}
: "${SAEL_BACKEND_PORT:?Set SAEL_BACKEND_PORT after running audit-readonly.sh}"
COMPOSE_FILE="$SAEL_DIR/backend/docker-compose.yml"
test -f "$COMPOSE_FILE" || { echo "Missing $COMPOSE_FILE" >&2; exit 1; }
cd "$SAEL_DIR/backend"
export SAEL_ENV_FILE=${SAEL_ENV_FILE:-/etc/sael/backend.env}
docker compose config --quiet
previous_id=$(docker compose images -q sael-evidence 2>/dev/null | head -n 1 || true)
if [[ -n "$previous_id" ]]; then
  rollback_tag="sael-evidence-backend:rollback-$(date -u +%Y%m%dT%H%M%SZ)"
  docker image tag "$previous_id" "$rollback_tag"
  printf '%s\n' "$rollback_tag" > /var/lib/sael/previous-image
fi
docker compose build --pull
docker compose up -d --remove-orphans
docker compose ps
curl --fail --silent --show-error --max-time 5 "http://127.0.0.1:${SAEL_BACKEND_PORT}/health"

echo 'Nginx was not modified. Configure the dedicated SAEL location separately, backup its file, run nginx -t, then reload.'
