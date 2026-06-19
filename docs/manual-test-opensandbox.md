# Manual Test Scenario: OpenSandbox Rollout (Local Fallback, Deployment, Verification)

Version: Draft v0.1
Status: Active
Date: 2026-06-19

## Purpose

This scenario covers the OpenSandbox-first sandbox rollout described in `docs/decisions/ADR-003-use-opensandbox-control-plane-with-kata-runtime.md`:

- running sandboxed agent tasks locally without a Kata cluster (Docker mode, or the legacy direct-Docker fallback)
- deploying OpenSandbox in production with Kubernetes mode and a Kata (or Kata+Firecracker) secure runtime
- the security differences between the runtimes a sandbox can sit on
- how to verify each path, automated where practical and manual where it requires infrastructure this repo doesn't own

Autofac is a *client* of OpenSandbox's REST API (`Autofac.Sandboxes.OpenSandboxApiClient`/`OpenSandboxSandboxExecutor`). It does not deploy OpenSandbox itself, and it does not create sandbox pods directly when `Sandboxes:Provider` is `opensandbox`. The reserved `kubernetes-kata` provider (`Autofac.Sandboxes.KubernetesKataSandboxExecutor`) is a stub — it is the fallback architecture only if the OpenSandbox spike fails, per ADR-003, and is not used by either path below.

## Prerequisites

- Docker (or an OCI-compatible daemon) for the local path.
- A Kubernetes cluster with a secure-runtime-capable node pool for the production path validation (not required to read this document or run the local path).
- `dotnet test` runs from the repository root.

## Part A — Local development: OpenSandbox in Docker mode

This is the contributor loop: no Kata cluster, no Kubernetes — OpenSandbox's own server runs as a container and talks to your local Docker daemon underneath.

### Step 0: Run the automated workflow/agent/OpenSandbox E2E stack

Use this first when you want to prove the full Autofac path:

```bash
scripts/run-opensandbox-e2e.sh
```

Equivalent manual sequence:

```bash
docker compose -f docker/docker-compose.e2e.yml \
  --profile opensandbox \
  up --build -d \
  postgres-opensandbox migrate-opensandbox wiremock opensandbox api-opensandbox

curl --fail --retry 60 --retry-delay 2 http://localhost:8083/api/health/live

docker compose -f docker/docker-compose.e2e.yml \
  --profile opensandbox \
  build e2e-tests-opensandbox

docker compose -f docker/docker-compose.e2e.yml \
  --profile opensandbox \
  run --rm e2e-tests-opensandbox
```

This profile builds and starts:

- Postgres for an isolated OpenSandbox E2E database.
- A local OpenSandbox server in Docker runtime mode (`http://localhost:8089/health` from the host).
- An Autofac API instance configured with `Sandboxes:Provider=opensandbox` (`http://localhost:8083/api/health/live` from the host).
- The `OpenSandboxWorkflowE2ETests` runner.

The E2E test uploads a temporary agent through `POST /api/agents/upload`, imports and publishes a BPMN workflow that references that agent, starts a run, and asserts the service task completed through the `sandbox.execute` tool with the `offline` sandbox profile.

Expected result: the `e2e-tests-opensandbox` container exits with code 0 and the run step output contains `autofac-sandbox: task complete`.

The OpenSandbox log line below is expected in this local scenario and is not the failure:

```text
server.api_key is not configured. Proceeding because OPENSANDBOX_INSECURE_SERVER explicitly acknowledges the insecure server mode.
```

This stack intentionally runs the server in insecure local-dev mode (`OPENSANDBOX_INSECURE_SERVER=YES`). The actual failure mode to watch for is `api-opensandbox` never becoming healthy, or the `e2e-tests-opensandbox` container exiting non-zero.

### Step 1: Start an OpenSandbox server locally

Follow the OpenSandbox project's own Docker-mode instructions: https://github.com/opensandbox-group/OpenSandbox/blob/main/docs/architecture.md. In Docker mode the server typically listens on a local port (this scenario assumes `http://localhost:8080/v1`, matching `OpenSandboxProviderOptions.ServerUrl`'s default).

Expected result: the server's health/readiness endpoint responds, and you can create a sandbox against it with the OpenSandbox CLI or a plain `curl` per its docs.

### Step 2: Point Autofac at the local server

Set in `src/Autofac.Api/appsettings.Development.json` (or environment variables):

```json
{
  "Sandboxes": {
    "Provider": "opensandbox",
    "OpenSandbox": {
      "Enabled": true,
      "ServerUrl": "http://localhost:8080/v1",
      "ApiKey": "",
      "DefaultImage": "alpine:3.19",
      "UseServerProxy": false
    }
  }
}
```

`UseServerProxy: false` is correct here — the server is reachable directly from the host running the Autofac worker.

### Step 3: Run a sandboxed workflow step

Start a workflow that routes a service task through the `sandbox.execute` tool (e.g. `wf-pilot-004` or any agent task with `Sandboxes:Provider` enabled — see `Autofac.Agents.Tools.SandboxExecutionTool`). Confirm in the run's tool-invocation history that the step completed and artifacts were captured.

### Step 4: Run the gated integration tests against the real server

```bash
export AUTOFAC_OPEN_SANDBOX_SERVER_URL="http://localhost:8080/v1"
export AUTOFAC_OPEN_SANDBOX_API_KEY=""   # if your server requires one
dotnet test tests/Autofac.Sandboxes.Tests/Autofac.Sandboxes.Tests.csproj \
  --filter "FullyQualifiedName~OpenSandboxIntegrationTests"
```

Expected result: all `OpenSandboxIntegrationTests` pass — smoke execution, non-zero exit handling, sandbox cleanup on success, sandbox retention on failure when `RetainSandboxOnFailure` is set, and execution under each named `Autofac.Sandboxes.SandboxProfileCatalog` profile. Without `AUTOFAC_OPEN_SANDBOX_SERVER_URL` set, these tests no-op and pass — that's the CI-safe default.

### Step 4b: Local fallback without OpenSandbox at all

If you don't want to run an OpenSandbox server, the legacy direct-Docker path still works (`Sandboxes:Provider: docker`, the chart/compose default for local stacks). Verify it end to end against your local Docker daemon:

```bash
export AUTOFAC_DOCKER_SANDBOX_E2E=1
# Only needed if `docker context ls` shows your active context isn't the
# default socket (e.g. Rancher Desktop, a remote context):
export AUTOFAC_DOCKER_ENDPOINT="unix:///path/to/docker.sock"
dotnet test tests/Autofac.Sandboxes.Tests/Autofac.Sandboxes.Tests.csproj \
  --filter "FullyQualifiedName~DockerSandboxLifecycleIntegrationTests"
```

Expected result: real container execution, artifact capture, cleanup-on-success, and retention-on-failure (per `SandboxCleanupPolicy.RetainSandboxOnFailure`) all pass against your local daemon. This is the path the Docker Compose stacks (`docker/docker-compose*.yml`) use; it has no meaningful isolation boundary beyond an ordinary container and is documented as local-only — see the security table below.

## Part B — Production: OpenSandbox in Kubernetes mode with a secure runtime

Production should default to `Sandboxes:Provider: opensandbox` with the server running in Kubernetes mode, never `docker`. The Helm chart's `sandbox` values block (`deploy/helm/autofac/values.yaml`) wires this:

```yaml
sandbox:
  provider: opensandbox
  openSandbox:
    enabled: true
    serverUrl: "http://opensandbox.opensandbox.svc.cluster.local:8080/v1"
    useServerProxy: true
```

Store the OpenSandbox API key (if the server requires one) in the `autofac-secrets` Secret under key `OPEN_SANDBOX_API_KEY` — the chart reads it as optional, so omitting it is fine for servers that don't require auth.

### Step 1: Deploy OpenSandbox itself

This is out of scope for Autofac's chart — deploy OpenSandbox's server in Kubernetes mode using its own manifests/chart per https://github.com/opensandbox-group/OpenSandbox. Configure its sandbox pod template to use a Kata `RuntimeClass` (or Kata+Firecracker, depending on your isolation requirement — see the table below). This is the cluster operator's responsibility, not Autofac's; Autofac never creates the sandbox pods directly when using the `opensandbox` provider.

### Step 2: Cluster validation checklist (manual)

- [ ] `kubectl get runtimeclass` shows the Kata runtime class the OpenSandbox server is configured to use.
- [ ] A sandbox created through OpenSandbox's own API/CLI shows `spec.runtimeClassName: kata` (or your Kata+Firecracker class name) on `kubectl get pod -n <opensandbox-namespace> <sandbox-pod> -o yaml`.
- [ ] `kubectl exec` into a sandbox pod and confirm the kernel is the Kata guest kernel, not the host kernel (e.g. `uname -r` differs from a host node's kernel version) — this is the concrete evidence the workload is in a microVM, not a shared-kernel container.
- [ ] The OpenSandbox server's network policy for sandbox pods matches what `Autofac.Sandboxes.SandboxProfileCatalog`'s profiles expect (e.g. the `repo-write` profile's `AllowedHosts` for GitHub).
- [ ] Deploy `deploy/helm/autofac` with `sandbox.openSandbox.serverUrl` pointing at the in-cluster OpenSandbox service, then run a real workflow through to completion and confirm the run's evidence pack records `Provider: OpenSandbox` (see `SandboxExecutionResult.Provider`).
- [ ] Kill a sandbox pod mid-execution and confirm Autofac surfaces a clean failure (not a hang) — exercises the same failure path covered by `OpenSandboxIntegrationTests.ExecuteAsync_NonZeroExit_ReturnsFailureWithExitCode`, but against a real disruption instead of a non-zero exit.

There is no automated CI job for this checklist — it requires a real cluster with Kata installed, which this repository does not provision. Treat it as a release-gate manual step before promoting a cluster to serve production traffic.

## Security differences: Docker/runc, gVisor, Kata, Kata+Firecracker

| Runtime | Isolation model | Kernel exposure | Relative overhead | Autofac's stance (ADR-003) |
| --- | --- | --- | --- | --- |
| Docker/runc | OS containers (namespaces + cgroups) | Shares the host kernel | Lowest | Local development and CI only. Not a production isolation boundary for untrusted or LLM-generated code. |
| gVisor | Userspace kernel intercepting syscalls | Shares the host kernel, but syscalls are intercepted/sandboxed by gVisor's Sentry | Low–medium | Valid secondary production option when operational simplicity is prioritized over the strongest isolation boundary. |
| Kata Containers | MicroVM (lightweight VM) per pod, OCI-compatible | Dedicated guest kernel per sandbox | Medium | Default production target. Strong boundary suitable for untrusted/LLM-generated code, fits Kubernetes/CRI/OCI deployment patterns Autofac already targets. |
| Kata + Firecracker | MicroVM via the Firecracker VMM specifically | Dedicated guest kernel, minimal device model | Medium-high (faster boot, smaller attack surface than other VMM backends) | Use when a platform owner wants the tightest microVM posture and can operate it. Same Kata RuntimeClass mechanism, different VMM underneath. |

The practical takeaway: only Kata and Kata+Firecracker give a sandboxed task its own kernel. Docker/runc and gVisor both still share the host kernel — gVisor narrows that exposure considerably via syscall interception, but it is not equivalent to a microVM boundary. Autofac treats Docker/runc as local-only, gVisor as an acceptable secondary production option, and Kata-class runtimes as the production default.

## References

- `docs/decisions/ADR-003-use-opensandbox-control-plane-with-kata-runtime.md`
- `docs/architecture-design.md` §6.5 (Sandbox Execution Manager, including the Sandbox Profiles subsection)
- `src/Autofac.Sandboxes/SandboxProfileCatalog.cs` — the four named profiles and what each maps to
- `tests/Autofac.Sandboxes.Tests/OpenSandboxIntegrationTests.cs` — gated by `AUTOFAC_OPEN_SANDBOX_SERVER_URL`
- `tests/Autofac.Sandboxes.Tests/DockerSandboxLifecycleIntegrationTests.cs` — gated by `AUTOFAC_DOCKER_SANDBOX_E2E`
- `deploy/helm/autofac/values.yaml` — the `sandbox` block
