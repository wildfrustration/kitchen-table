#!/bin/bash
# Exports the CMS reference data (everything except app data) from the local database to data/dumps/cms.dump.
# docker compose's init step restores it into a fresh database; deploy-data copies it to a server.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p data/dumps
docker compose exec -T db pg_dump -U pland -d pland -Fc --exclude-schema=app > data/dumps/cms.dump.tmp
mv data/dumps/cms.dump.tmp data/dumps/cms.dump
ls -lh data/dumps/cms.dump
