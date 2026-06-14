#!/usr/bin/env bash
# Waits for Postgres to be ready, then runs EF Core migrations.
# Used as the entrypoint for the 'migrate' service in docker-compose.

set -euo pipefail

HOST="${DB_HOST:-postgres}"
PORT="${DB_PORT:-5432}"
USER="${DB_USER:-postgres}"

echo "Waiting for Postgres at $HOST:$PORT..."
until pg_isready -h "$HOST" -p "$PORT" -U "$USER" -q; do
  sleep 1
done
echo "Postgres is ready."

echo "Running EF Core migrations..."
dotnet ef database update \
  --project src/Autofac.Infrastructure/Autofac.Infrastructure.csproj \
  --startup-project src/Autofac.Api/Autofac.Api.csproj \
  --connection "$CONNECTION_STRING"

echo "Migrations complete."
