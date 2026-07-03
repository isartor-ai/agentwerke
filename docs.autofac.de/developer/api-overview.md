# API Overview

The API is served by `src/Autofac.Api`. The OpenAPI document is available at:

```text
/openapi/v1.json
```

## Health and runtime

| Endpoint | Purpose |
| --- | --- |
| `GET /api/health/live` | Liveness probe. |
| `GET /api/health/ready` | Readiness probe with runtime information. |
| `GET /api/health/runtime` | Active workflow runtime mode. |
| `GET /api/health/camunda` | Camunda topology/status when that adapter is active. |

## Auth

| Endpoint | Purpose |
| --- | --- |
| `GET /api/auth/config` | Auth discovery/configuration. |
| `POST /api/auth/token` | Development token endpoint when enabled. |

Development token endpoints are not production authentication.

## Settings

| Endpoint | Purpose |
| --- | --- |
| `GET /api/settings` | Redacted Admin settings catalog. |
| `PATCH /api/settings` | Save supported non-secret values and rotate supported secrets. |
| `POST /api/settings/tests/{target}` | Run a dry-run readiness check. |

Settings requires Admin access.

## Workflows and runs

| Endpoint | Purpose |
| --- | --- |
| `GET /api/workflows` | List workflows. |
| `GET /api/workflows/{workflowId}` | Read one workflow. |
| `POST /api/runs` | Start a run. |
| `GET /api/runs` | List runs. |
| `GET /api/runs/{runId}` | Read run detail. |

## Approvals

| Endpoint | Purpose |
| --- | --- |
| `GET /api/approvals` | List approval requests. |
| `POST /api/approvals/{approvalId}/decision` | Approve, reject, or request changes. |

Approval decisions require the Approver role or a higher role with equivalent access.

## Artifacts and evidence

| Endpoint | Purpose |
| --- | --- |
| `POST /api/runs/{runId}/artifacts/{artifactName}` | Upload artifact bytes. |
| `GET /api/runs/{runId}/artifacts/{artifactName}` | Download an artifact. |
| `GET /api/runs/{runId}/evidence-pack` | Generate evidence-pack JSON. |
| `GET /api/runs/{runId}/evidence-pack/download` | Download evidence-pack JSON. |

## Webhooks

| Endpoint | Purpose |
| --- | --- |
| `POST /webhooks/github` | Receive signed GitHub events such as `issues`. |

GitHub webhooks are anonymous at the HTTP auth layer but validated through signature checking.

## API usage pattern

1. Obtain a valid token for the target role.
2. Start or inspect a workflow run.
3. Follow pending approval or wait-state information.
4. Retrieve artifacts or evidence after completion.
5. Use audit and evidence data for investigation or records retention.
