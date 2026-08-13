#!/usr/bin/env bash
# One-time: convert the tar-pushed source dirs into git clones of the private repos,
# using per-repo deploy keys, then build+start at origin/main. Run AFTER the two
# public deploy keys are added on GitHub. Safe to re-run (skips dirs already cloned).
set -euo pipefail
cd "$(dirname "$0")"

RWT_URL="git@github-orr:DENDUKA/OpenRussiaRouter.git"
F9_URL="git@github-f9:Inc64/F9Router.git"

clone_into() {  # <url> <target>
  local url="$1" target="$2"
  if [ -d "$target/.git" ]; then echo "   $target already a git clone — skip"; return 0; fi
  echo "   cloning $url -> $target"
  rm -rf "${target}.git"
  GIT_SSH_COMMAND="ssh -o StrictHostKeyChecking=accept-new" git clone "$url" "${target}.git"
  rm -rf "$target"
  mv "${target}.git" "$target"
}

echo "==> verifying deploy-key access"
GIT_SSH_COMMAND="ssh -o StrictHostKeyChecking=accept-new" ssh -T git@github-orr 2>&1 | grep -qi 'successfully authenticated\|does not provide shell' || true
GIT_SSH_COMMAND="ssh -o StrictHostKeyChecking=accept-new" ssh -T git@github-f9  2>&1 | grep -qi 'successfully authenticated\|does not provide shell' || true

echo "==> cloning repos"
clone_into "$RWT_URL" app
clone_into "$F9_URL"  f9router

echo "==> build + start everything at origin/main"
docker compose build
docker compose up -d

echo "==> done. Current commits:"
echo "   orr      : $(git -C app rev-parse --short HEAD)"
echo "   f9router : $(git -C f9router rev-parse --short HEAD)"
docker compose ps --format 'table {{.Service}}\t{{.Status}}'
