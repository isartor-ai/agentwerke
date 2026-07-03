#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="$repo_root/docker/docker-compose.e2e.yml"

cleanup() {
  if [[ "${AGENTWERKE_OPEN_SANDBOX_KEEP_STACK:-0}" == "1" ]]; then
    return
  fi

  docker compose \
    -f "$compose_file" \
    --profile opensandbox \
    down \
    --remove-orphans
}

# OpenSandbox's egress sidecar enforcement only works with the docker runtime's
# network_mode set to "bridge" (see docker/opensandbox/sandbox.toml), so sandbox
# and egress-sidecar containers the server creates via the mounted host Docker
# socket land on Docker's literal "bridge" network. Both the opensandbox server
# (reaching its own egress sidecars) and api-opensandbox (reaching sandboxes'
# execd endpoints directly, since UseServerProxy=false avoids a proxy framing
# bug — see docker-compose.e2e.yml) need to be attached there too. Compose
# can't declare this itself — the daemon rejects network-scoped aliases on
# non-user-defined networks — so it's done imperatively, per container, here.
attach_to_bridge_network() {
  local service="$1"
  local container_id
  container_id="$(docker compose -f "$compose_file" --profile opensandbox ps -q "$service")"
  docker network connect bridge "$container_id" 2>/dev/null || true
}

wait_for_api() {
  local attempt
  local max_attempts="${AGENTWERKE_OPEN_SANDBOX_WAIT_ATTEMPTS:-60}"
  local sleep_seconds="${AGENTWERKE_OPEN_SANDBOX_WAIT_SECONDS:-2}"

  for ((attempt = 1; attempt <= max_attempts; attempt++)); do
    if curl -fsS "http://localhost:8083/api/health/live" >/dev/null; then
      return 0
    fi

    sleep "$sleep_seconds"
  done

  return 1
}

trap cleanup EXIT

cd "$repo_root"

docker compose \
  -f "$compose_file" \
  --profile opensandbox \
  up \
  --build \
  --force-recreate \
  -d \
  postgres-opensandbox \
  migrate-opensandbox \
  wiremock \
  opensandbox

attach_to_bridge_network opensandbox

docker compose \
  -f "$compose_file" \
  --profile opensandbox \
  up \
  --build \
  --force-recreate \
  -d \
  api-opensandbox

attach_to_bridge_network api-opensandbox

if ! wait_for_api; then
  docker compose \
    -f "$compose_file" \
    --profile opensandbox \
    logs \
    --no-color \
    api-opensandbox \
    opensandbox
  echo "Timed out waiting for api-opensandbox to become healthy on http://localhost:8083/api/health/live" >&2
  exit 1
fi

docker compose \
  -f "$compose_file" \
  --profile opensandbox \
  build \
  e2e-tests-opensandbox

docker compose \
  -f "$compose_file" \
  --profile opensandbox \
  run \
  --rm \
  e2e-tests-opensandbox \
  "$@"
