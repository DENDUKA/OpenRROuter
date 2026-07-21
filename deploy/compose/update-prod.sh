#!/usr/bin/env bash
# Routine PROD update for OpenRussiaRouter (host 201.51.6.246, ORR-only + Caddy TLS):
# git pull -> re-sync tracked compose.prod.yml/Caddyfile -> rebuild .NET services -> restart
# -> health-gate -> rollback to the previous commit on failure.
#
# Re-syncing the compose + Caddyfile from git every run is what keeps the box from
# hand-diverging. Caddy is recreated too (picks up Caddyfile changes); its image is not built.
# For infra changes (new compose service, etc.) prefer deploy-prod.sh.
set -euo pipefail
cd "$(dirname "$0")"

DIR=app
SRC="$DIR/deploy/compose"
SERVICES="auth-service billing-service control-plane collector api-gateway webapp"

sync_tracked() {
  cp "$SRC/compose.prod.yml"    compose.yml
  cp "$SRC/Caddyfile"           Caddyfile
  cp "$SRC/prometheus.prod.yml" prometheus.yml
  # Mirror grafana/ wholesale so deleted dashboards/datasources disappear from the box too.
  rm -rf grafana
  cp -r "$SRC/grafana" grafana
}

# prometheus.yml and grafana/provisioning are bind mounts: `compose up -d` does NOT recreate a
# container when only the mounted file changed, so a synced config would silently not apply.
# Prometheus reloads in place (--web.enable-lifecycle); Grafana re-reads provisioned datasources
# only on boot, so it gets restarted. Never fatal — monitoring must not fail an app deploy.
reload_monitoring() {
  curl -fsS -X POST --max-time 10 http://127.0.0.1:9090/-/reload >/dev/null 2>&1 \
    && echo "    prometheus config reloaded" \
    || echo "    !! prometheus reload skipped (not running?)"
  docker compose restart grafana >/dev/null 2>&1 \
    && echo "    grafana restarted (re-reads provisioning)" \
    || echo "    !! grafana restart skipped"
}

health() {
  curl -fsS --max-time 8 http://127.0.0.1:8080/health >/dev/null 2>&1 || return 1
  curl -fsS --max-time 8 http://127.0.0.1:5006/health >/dev/null 2>&1 || return 1
  [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 8 http://127.0.0.1:3000/login)" = "200" ] || return 1
  return 0
}

[ -d "$DIR/.git" ] || { echo "!! $DIR is not a git clone. Run bootstrap-git.sh first." >&2; exit 1; }

echo "==> [prod] fetch + pull"
before=$(git -C "$DIR" rev-parse HEAD)
git -C "$DIR" fetch --quiet origin
git -C "$DIR" pull --ff-only
after=$(git -C "$DIR" rev-parse HEAD)
echo "    ${before:0:9} -> ${after:0:9}"

echo "==> [prod] sync compose.prod.yml + Caddyfile from git"
sync_tracked

echo "==> [prod] build"
docker compose build $SERVICES
echo "==> [prod] restart app + caddy + monitoring"
docker compose up -d $SERVICES caddy prometheus grafana
reload_monitoring

echo "==> [prod] health (up to 40s)"
ok=0; for i in $(seq 1 8); do sleep 5; if health; then ok=1; break; fi; done

if [ "$ok" != "1" ]; then
  echo "!! [prod] health FAILED after update."
  if [ "$before" != "$after" ]; then
    echo "!! [prod] rolling back to ${before:0:9} and rebuilding"
    git -C "$DIR" reset --hard "$before"
    sync_tracked
    docker compose build $SERVICES
    docker compose up -d $SERVICES caddy prometheus grafana
  fi
  docker compose ps --format 'table {{.Service}}\t{{.Status}}'
  exit 1
fi

docker image prune -f >/dev/null
echo "==> [prod] OK at ${after:0:9}"
docker compose ps --format 'table {{.Service}}\t{{.Status}}'
