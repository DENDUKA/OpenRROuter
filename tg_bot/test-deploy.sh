#!/usr/bin/env bash
# Deterministic updater tests: no Docker daemon, SSH, secrets or real sleeps.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
mkdir "$TMP/bin"
cat > "$TMP/bin/sleep" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
cat > "$TMP/bin/docker" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$TEST_LOG"
if [ "$1" = inspect ]; then
  case "$3" in
    '{{.Image}}') echo sha256:previous ;;
    *compose.project*) echo rawrter-tg-bot ;;
    *Health.Status*)
      if [ "$SCENARIO" = unhealthy ] && [ ! -f "$TEST_RESTORED" ]; then echo unhealthy; else echo healthy; fi ;;
  esac
  exit 0
fi
case " $* " in
  *' build '*) [ "$SCENARIO" != build-fail ]; exit $? ;;
  *' up '*)
    # An extra override is only used for rollback.
    if [ "$#" -gt 11 ]; then
      touch "$TEST_RESTORED"
    elif [ "$SCENARIO" = up-fail ]; then
      exit 1
    fi ;;
esac
MOCK
chmod +x "$TMP/bin/"*
for scenario in success build-fail unhealthy up-fail; do
  export SCENARIO="$scenario" TEST_LOG="$TMP/$scenario.log" TEST_RESTORED="$TMP/$scenario.restored"
  rc=0
  PATH="$TMP/bin:$PATH" bash "$ROOT/deploy/compose/update-tg-bot.sh" > "$TMP/$scenario.out" 2>&1 || rc=$?
  if [ "$scenario" = success ]; then
    [ "$rc" = 0 ]
    [ ! -f "$TEST_RESTORED" ]
  else
    [ "$rc" != 0 ]
    if [ "$scenario" = build-fail ]; then
      ! grep -q ' up ' "$TEST_LOG"
    else
      [ -f "$TEST_RESTORED" ]
    fi
  fi
  ! grep -Eq 'down|rm |volume|prune' "$TEST_LOG"
  echo "PASS: $scenario"
done
