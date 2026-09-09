#!/usr/bin/env bash
# Called by update-prod.sh AFTER the main application passes its health gate.
# Keeps the existing separate Compose project and reads its existing secret file.
set -euo pipefail
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
COMPOSE="$ROOT/tg_bot/compose.yml"
BOT=rawrter-tg-bot
DC=(docker compose -p "$BOT" -f "$COMPOSE")

"${DC[@]}" config --quiet
previous=$(docker inspect --format '{{.Image}}' "$BOT" 2>/dev/null || true)
if [ -z "$previous" ]; then
  echo '!! Existing bot container not found; initial deployment requires separate approval.' >&2
  exit 1
fi
# Do not replace a container belonging to a different project.
project=$(docker inspect --format '{{index .Config.Labels "com.docker.compose.project"}}' "$BOT")
[ "$project" = "$BOT" ] || { echo '!! Unexpected bot Compose project.' >&2; exit 1; }

healthy() {
  [ "$(docker inspect --format '{{.State.Health.Status}}' "$BOT" 2>/dev/null)" = healthy ]
}
wait_healthy() {
  for i in $(seq 1 18); do
    sleep 5
    if healthy; then return 0; fi
  done
  return 1
}

# Build failure leaves the running bot untouched.
"${DC[@]}" build "$BOT"
if "${DC[@]}" up -d --no-deps --no-build "$BOT" && wait_healthy; then
  echo '==> Bot updated; local HTTP health passed (not proof of Telegram delivery).'
  exit 0
fi

echo '!! Bot update failed; restoring previous image. Main services stay unchanged.' >&2
override=$(mktemp)
trap 'rm -f "$override"' EXIT
printf 'services:\n  rawrter-tg-bot:\n    image: "%s"\n' "$previous" > "$override"
if "${DC[@]}" -f "$override" up -d --no-deps --no-build "$BOT" && wait_healthy; then
  echo '==> Previous bot image restored.' >&2
else
  echo '!! Bot rollback health failed; manual investigation required.' >&2
fi
exit 1
