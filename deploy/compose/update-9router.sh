#!/usr/bin/env bash
# Update f9router (Inc64/F9Router fork) on the VPS: git pull -> build -> restart -> health-gate -> rollback on failure.
set -euo pipefail
cd "$(dirname "$0")"

DIR=f9router
SERVICES="f9router"

health() {
  local code
  code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 http://127.0.0.1:20128/api/health)
  [ "$code" = "200" ] || return 1
  return 0
}

if [ ! -d "$DIR/.git" ]; then
  echo "!! $DIR is not a git clone. Run bootstrap-git.sh first." >&2
  exit 1
fi

echo "==> [9r] fetch + pull"
before=$(git -C "$DIR" rev-parse HEAD)
git -C "$DIR" fetch --quiet origin
git -C "$DIR" pull --ff-only
after=$(git -C "$DIR" rev-parse HEAD)
echo "    ${before:0:9} -> ${after:0:9}"

echo "==> [9r] build"
docker compose build $SERVICES
echo "==> [9r] restart"
docker compose up -d $SERVICES

echo "==> [9r] health (up to 60s — Next.js boot)"
ok=0; for i in $(seq 1 12); do sleep 5; if health; then ok=1; break; fi; done

if [ "$ok" != "1" ]; then
  echo "!! [9r] health FAILED after update."
  if [ "$before" != "$after" ]; then
    echo "!! [9r] rolling back to ${before:0:9} and rebuilding"
    git -C "$DIR" reset --hard "$before"
    docker compose build $SERVICES
    docker compose up -d $SERVICES
  fi
  docker compose ps --format 'table {{.Service}}\t{{.Status}}' | grep f9router
  exit 1
fi

docker image prune -f >/dev/null
echo "==> [9r] OK at ${after:0:9}"
docker compose ps --format 'table {{.Service}}\t{{.Status}}' | grep f9router
