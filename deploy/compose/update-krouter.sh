#!/usr/bin/env bash
# Update krouter (DENDUKA/krouter fork) on the VPS: git pull -> build -> restart -> health-gate -> rollback on failure.
set -euo pipefail
cd "$(dirname "$0")"

DIR=krouter
SERVICES="krouter"

health() {
  local code
  code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 http://127.0.0.1:20129/api/health)
  [ "$code" = "200" ] || return 1
  return 0
}

if [ ! -d "$DIR/.git" ]; then
  echo "!! $DIR is not a git clone." >&2
  exit 1
fi

echo "==> [krouter] fetch + pull"
before=$(git -C "$DIR" rev-parse HEAD)
git -C "$DIR" fetch --quiet origin
git -C "$DIR" pull --ff-only
after=$(git -C "$DIR" rev-parse HEAD)
echo "    ${before:0:9} -> ${after:0:9}"

echo "==> [krouter] build"
docker compose build $SERVICES
echo "==> [krouter] restart"
docker compose up -d $SERVICES

echo "==> [krouter] health (up to 60s — Next.js boot)"
ok=0; for i in $(seq 1 12); do sleep 5; if health; then ok=1; break; fi; done

if [ "$ok" != "1" ]; then
  echo "!! [krouter] health FAILED after update."
  if [ "$before" != "$after" ]; then
    echo "!! [krouter] rolling back to ${before:0:9} and rebuilding"
    git -C "$DIR" reset --hard "$before"
    docker compose build $SERVICES
    docker compose up -d $SERVICES
  fi
  docker compose ps --format 'table {{.Service}}\t{{.Status}}' | grep krouter
  exit 1
fi

docker image prune -f >/dev/null
echo "==> [krouter] OK at ${after:0:9}"
docker compose ps --format 'table {{.Service}}\t{{.Status}}' | grep krouter
