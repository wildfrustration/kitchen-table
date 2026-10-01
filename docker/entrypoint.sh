#!/bin/sh
# kt serve        migrate, seed demo data if KT_SEED_DEMO=true and the app is empty, then run the API
# kt migrate      apply database migrations
# kt demo-reset   wipe app data and load the demo agencies, brokers and clients
# kt cli ...      the data CLI (ingest, releases, quote, validate …)
set -e
case "$1" in
  serve)
    dotnet /app/PlanD.Api.dll migrate
    if [ "$KT_SEED_DEMO" = "true" ]; then dotnet /app/PlanD.Api.dll demo-seed; fi
    exec dotnet /app/PlanD.Api.dll
    ;;
  migrate | demo-reset | demo-seed)
    exec dotnet /app/PlanD.Api.dll "$@"
    ;;
  cli)
    shift
    exec dotnet /app/cli/PlanD.Cli.dll "$@"
    ;;
  *)
    exec "$@"
    ;;
esac
