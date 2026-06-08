#!/usr/bin/env bash
# Update OpenRussiaRouter (.NET stack) on the VPS: git pull -> build -> restart -> health-gate -> rollback on failure.
set -euo pipefail
cd "$(dirname "$0")"

DIR=app
SERVICES="auth-service billing-service control-plane collector api-gateway webapp"

health() {
  curl -fsS --max-time 8 http://127.0.0.1:8080/health >/dev/null 2>&1 || return 1
  curl -fsS --max-time 8 http://127.0.0.1:5006/health >/dev/null 2>&1 || return 1
  local code
  code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 8 http://127.0.0.1:3000/login)
  [ "$code" = "200" ] || return 1   # /login exercises gRPC to auth-service
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

echo "==> [orr] build"
docker compose build $SERVICES
echo "==> [orr] restart"
docker compose up -d $SERVICES

echo "==> [orr] health (up to 40s)"
ok=0; for i in $(seq 1 8); do sleep 5; if health; then ok=1; break; fi; done

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
docker compose ps --format 'table {{.Service}}\t{{.Status}}' | grep -E 'auth|billing|control|collector|gateway|webapp'
