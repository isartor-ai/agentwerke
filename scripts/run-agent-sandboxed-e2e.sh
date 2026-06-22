#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
compose_file="$repo_root/docker/docker-compose.e2e.yml"

cleanup() {
  if [[ "${AUTOFAC_AGENT_SANDBOXED_KEEP_STACK:-0}" == "1" ]]; then
    return
  fi

  docker compose \
    -f "$compose_file" \
    --profile agent-sandboxed \
    down \
    --remove-orphans

  rm -rf "$repo_root/docker/.agent-sandboxed-artifacts"
}

wait_for_api() {
  local attempt
  local max_attempts="${AUTOFAC_AGENT_SANDBOXED_WAIT_ATTEMPTS:-60}"
  local sleep_seconds="${AUTOFAC_AGENT_SANDBOXED_WAIT_SECONDS:-2}"

  for ((attempt = 1; attempt <= max_attempts; attempt++)); do
    if curl -fsS "http://localhost:8085/api/health/live" >/dev/null; then
      return 0
    fi

    sleep "$sleep_seconds"
  done

  return 1
}

trap cleanup EXIT

cd "$repo_root"

# Must exist (and be owned by the invoking user, not root) before api-agent-sandboxed
# starts — see the Sandboxes__Docker__ArtifactsHostPath comment in the compose file
# for why this same path is bind-mounted both here and as the sandbox executor's
# artifacts root.
mkdir -p "$repo_root/docker/.agent-sandboxed-artifacts"

# Build and tag the image OpenSandboxedAgentRunner runs inside the sandbox
# container before bringing up anything that might try to use it.
docker compose \
  -f "$compose_file" \
  --profile agent-sandboxed \
  build \
  agent-runner-image

docker compose \
  -f "$compose_file" \
  --profile agent-sandboxed \
  up \
  --build \
  --force-recreate \
  -d \
  postgres-agent-sandboxed \
  migrate-agent-sandboxed \
  wiremock \
  api-agent-sandboxed

if ! wait_for_api; then
  docker compose \
    -f "$compose_file" \
    --profile agent-sandboxed \
    logs \
    --no-color \
    api-agent-sandboxed \
    wiremock
  echo "Timed out waiting for api-agent-sandboxed to become healthy on http://localhost:8085/api/health/live" >&2
  exit 1
fi

docker compose \
  -f "$compose_file" \
  --profile agent-sandboxed \
  build \
  e2e-tests-agent-sandboxed

docker compose \
  -f "$compose_file" \
  --profile agent-sandboxed \
  run \
  --rm \
  e2e-tests-agent-sandboxed \
  "$@"
