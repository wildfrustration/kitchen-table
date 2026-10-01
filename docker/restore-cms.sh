#!/bin/bash
# One-shot: load the CMS reference data (plans, drugs, pharmacies, geography) into an empty database
# from data/dumps/cms.dump. Skips if it's already loaded or there is no dump.
set -euo pipefail

if [ "$(psql -tAc "select to_regclass('cms.release') is not null")" = "t" ] \
   && [ "$(psql -tAc "select count(*) from cms.release where status = 'active'")" != "0" ]; then
  echo "CMS data already loaded."
  exit 0
fi
if [ ! -f /dumps/cms.dump ]; then
  echo "No /dumps/cms.dump: the app will start without plan data."
  echo "Export one from a loaded database with scripts/export-cms.sh, or load raw files with 'kt cli ingest …'."
  exit 0
fi

echo "Restoring CMS data from cms.dump…"
psql -v ON_ERROR_STOP=1 <<'SQL'
create extension if not exists pg_trgm;
create schema if not exists cms;
create table if not exists public.schema_migrations (
    name       text primary key,
    applied_at timestamptz not null default now()
);
SQL
pg_restore --no-owner --exit-on-error -d "$PGDATABASE" -n cms /dumps/cms.dump
pg_restore --no-owner --exit-on-error --data-only -d "$PGDATABASE" -n public -t schema_migrations /dumps/cms.dump
psql -tAc "select source || coalesce(' ' || plan_year, '') || ': ' || label from cms.release where status = 'active' order by id"
echo "CMS data restored."
