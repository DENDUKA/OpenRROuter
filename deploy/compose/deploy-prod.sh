#!/usr/bin/env bash
# Full PROD deploy for OpenRussiaRouter (host 201.51.6.246, ORR-only + Caddy TLS).
#
# Syncs the git-tracked prod compose + Caddyfile into /opt/orr/, then (re)builds the .NET
# services and brings the WHOLE stack up — including Caddy. Use this on first deploy or when
# the compose file / Caddyfile change. For routine code-only updates use update-prod.sh.
#
# Runs ON the box, from /opt/orr/. Prereqs: app/ is a git clone (bootstrap-git.sh), and a
# filled /opt/orr/.env exists (see deploy/compose/.env.example — needs RWT_PUBLIC_DOMAIN,
# ACME_EMAIL, and, for live payments, ROLLYPAY_* + PAYMENTS_ACTIVE_PROVIDER).
set -euo pipefail
cd "$(dirname "$0")"

DIR=app
SRC="$DIR/deploy/compose"
APP_SERVICES="auth-service billing-service control-plane content-service collector api-gateway webapp"

[ -d "$DIR/.git" ] || { echo "!! $DIR is not a git clone. Run bootstrap-git.sh first." >&2; exit 1; }
[ -f .env ]        || { echo "!! .env missing in $(pwd). Copy $SRC/.env.example and fill it." >&2; exit 1; }

echo "==> [prod] fetch + pull app"
git -C "$DIR" fetch --quiet origin
git -C "$DIR" pull --ff-only

echo "==> [prod] sync tracked compose.prod.yml + Caddyfile + monitoring config -> /opt/orr"
cp "$SRC/compose.prod.yml"   compose.yml
cp "$SRC/Caddyfile"          Caddyfile
cp "$SRC/prometheus.prod.yml" prometheus.yml
# Mirror grafana/ wholesale so deleted dashboards/datasources disappear from the box too.
rm -rf grafana
cp -r "$SRC/grafana" grafana

echo "==> [prod] build .NET services"
docker compose build $APP_SERVICES

echo "==> [prod] up (all services incl. caddy)"
docker compose up -d --remove-orphans

# Bind-mounted configs (prometheus.yml, grafana/provisioning) do not trigger a container
# recreate on their own — nudge both so a synced config actually takes effect.
curl -fsS -X POST --max-time 10 http://127.0.0.1:9090/-/reload >/dev/null 2>&1 \
  && echo "    prometheus config reloaded" || true
docker compose restart grafana >/dev/null 2>&1 \
  && echo "    grafana restarted (re-reads provisioning)" || true

echo "==> [prod] health (up to 60s)"
ok=0
for i in $(seq 1 12); do
  sleep 5
  curl -fsS --max-time 8 http://127.0.0.1:8080/health >/dev/null 2>&1 || continue
  curl -fsS --max-time 8 http://127.0.0.1:5006/health >/dev/null 2>&1 || continue
  [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 8 http://127.0.0.1:3000/login)" = "200" ] || continue
  ok=1; break
done
[ "$ok" = "1" ] && echo "    app health OK" || echo "!! [prod] app health NOT OK — check 'docker compose logs'."

# Caddy / TLS is informational: ACME issuance can lag a few seconds and depends on DNS, so it
# never fails the deploy. Verify the cert manually after.
echo "==> [prod] caddy"
docker compose ps caddy --format 'table {{.Service}}\t{{.Status}}' || true
echo "    (verify TLS: curl -I https://${RWT_PUBLIC_DOMAIN:-<domain>}/login ; logs: docker compose logs --tail=30 caddy)"

docker image prune -f >/dev/null
docker compose ps --format 'table {{.Service}}\t{{.Status}}'
[ "$ok" = "1" ] || exit 1
echo "==> [prod] done"
