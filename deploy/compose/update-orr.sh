#!/usr/bin/env bash
# Update OpenRussiaRouter (.NET stack) on the VPS: git pull -> build -> restart -> health-gate -> rollback on failure.
# Usage:
#   ./update-orr.sh                 # all .NET services
#   ./update-orr.sh api-gateway     # one service (auth-service|billing-service|control-plane|collector|api-gateway|webapp)
set -euo pipefail
cd "$(dirname "$0")"

DIR=app
ALL_SERVICES="auth-service billing-service control-plane collector api-gateway webapp"
REQUESTED="${1:-}"

# Validate the requested service if any.
if [ -n "$REQUESTED" ]; then
  case "$REQUESTED" in
    auth-service|billing-service|control-plane|collector|api-gateway|webapp) ;;
    *) echo "!! unknown service: $REQUESTED (allowed: $ALL_SERVICES)" >&2; exit 2 ;;
  esac
  SERVICES="$REQUESTED"
  echo "==> [orr] single-service mode: $SERVICES"
else
  SERVICES="$ALL_SERVICES"
  echo "==> [orr] full-stack mode: $SERVICES"
fi

# Container-state check: returns 0 if container is running and not restarting/crashing.
container_running() {  # <service>
  local svc="$1"
  local status
  status=$(docker compose ps "$svc" --format '{{.Status}}' 2>/dev/null | head -1)
  [ -n "$status" ] || return 1
  echo "$status" | grep -qi '^Up' || return 1
  echo "$status" | grep -qiE 'Restarting|Exited' && return 1
  return 0
}

# HTTP health per service (only where an HTTP endpoint is exposed).
http_health() {  # <service>
  local svc="$1"
  local url
  case "$svc" in
    api-gateway) url="http://127.0.0.1:8080/health" ;;
    collector)   url="http://127.0.0.1:5006/health" ;;
    webapp)      url="http://127.0.0.1:3000/login" ;;
    *)           return 0 ;;  # gRPC-only services: skip HTTP check, rely on container_running
  esac
  local code
  code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$url" || echo 000)
  [ "$code" = "200" ] || return 1
  return 0
}

# Full-stack health (legacy multi-endpoint check).
full_health() {
  curl -fsS --max-time 8 http://127.0.0.1:8080/health >/dev/null 2>&1 || return 1
  curl -fsS --max-time 8 http://127.0.0.1:5006/health >/dev/null 2>&1 || return 1
  local code
  code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 8 http://127.0.0.1:3000/login)
  [ "$code" = "200" ] || return 1   # /login exercises gRPC to auth-service
  return 0
}

# Per-service health gate.
check_health() {
  if [ "$SERVICES" = "$ALL_SERVICES" ]; then
    full_health
    return $?
  fi
  container_running "$SERVICES" || return 1
  http_health "$SERVICES"        || return 1
  return 0
}

if [ ! -d "$DIR/.git" ]; then
  echo "!! $DIR is not a git clone. Run bootstrap-git.sh first." >&2
  exit 1
fi

echo "==> [orr] fetch + pull"
before=$(git -C "$DIR" rev-parse HEAD)
git -C "$DIR" fetch --quiet origin
git -C "$DIR" pull --ff-only
after=$(git -C "$DIR" rev-parse HEAD)
echo "    ${before:0:9} -> ${after:0:9}"

echo "==> [orr] build: $SERVICES"
docker compose build $SERVICES
echo "==> [orr] restart: $SERVICES"
docker compose up -d $SERVICES

echo "==> [orr] health (up to 60s)"
ok=0; for i in $(seq 1 12); do sleep 5; if check_health; then ok=1; break; fi; done

if [ "$ok" != "1" ]; then
  echo "!! [orr] health FAILED after update."
  if [ "$before" != "$after" ]; then
    echo "!! [orr] rolling back to ${before:0:9} and rebuilding"
    git -C "$DIR" reset --hard "$before"
    docker compose build $SERVICES
    docker compose up -d $SERVICES
  fi
  docker compose ps --format 'table {{.Service}}\t{{.Status}}'
  exit 1
fi

docker image prune -f >/dev/null
echo "==> [orr] OK at ${after:0:9}"
if [ "$SERVICES" = "$ALL_SERVICES" ]; then
  docker compose ps --format 'table {{.Service}}\t{{.Status}}' | grep -E 'auth|billing|control|collector|gateway|webapp'
else
  docker compose ps "$SERVICES" --format 'table {{.Service}}\t{{.Status}}'
fi
