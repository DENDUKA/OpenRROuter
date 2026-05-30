#!/usr/bin/env bash
# Wrapper: update both products or one.
#   ./update.sh          -> update ORR then f9router
#   ./update.sh orr      -> only OpenRussiaRouter
#   ./update.sh 9r       -> only f9router
set -euo pipefail
cd "$(dirname "$0")"

case "${1:-all}" in
  orr)            ./update-orr.sh ;;
  9r|f9router)    ./update-9router.sh ;;
  all)            ./update-orr.sh && ./update-9router.sh ;;
  *) echo "usage: $0 [orr|9r|all]" >&2; exit 2 ;;
esac
