#!/bin/bash
# Mirrors the project to the server over SFTP (SSH key login), skipping build output and the ~34 GB of raw CMS
# downloads. Uploads only changed files and deletes remote files that were deleted here; data/dumps/cms.dump goes too.
# Usage: scripts/sync.sh [--dry-run]    Needs lftp (brew install lftp). Then on the server: docker compose up -d --build
set -euo pipefail
cd "$(dirname "$0")/.."

REMOTE_HOST=${REMOTE_HOST:-bryce.dankata.com}
REMOTE_USER=${REMOTE_USER:-yordan}
REMOTE_DIR=${REMOTE_DIR:-/home/yordan/workspace/plan-d}

dry_run=""
[ "${1:-}" = "--dry-run" ] && dry_run="--dry-run"

# Excluded paths are neither uploaded nor deleted on the server, so a server-side .env or
# docker-compose.override.yml survives.
lftp -u "$REMOTE_USER," "sftp://$REMOTE_HOST" -e "
set net:timeout 15; set net:max-retries 2
mirror --reverse --delete --verbose --parallel=4 $dry_run \
  -x '^data/raw/' -x '^data/work/' \
  -x '(^|/)bin/' -x '(^|/)obj/' -x '(^|/)node_modules/' -x '(^|/)TestResults/' \
  -x '^web/dist/' -x '^src/PlanD\.Api/wwwroot/' \
  -x '^\.git/' -x '^\.idea/' -x '^\.vs/' -x '\.user$' -x '(^|/)\.DS_Store$' -x '^\.env' \
  -x '^docker-compose\.override\.yml$' \
  . '$REMOTE_DIR'
quit"
