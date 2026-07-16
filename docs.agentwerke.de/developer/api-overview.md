# API Overview

The API is served by `src/Agentwerke.Api`. The OpenAPI document is available at:

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
| `POST /webhooks/events` | Receive a signed domain event from a registered external system. |

GitHub webhooks are anonymous at the HTTP auth layer but validated through signature checking.

### Generic event ingress

`POST /webhooks/events` lets any external system — a CI job, a test runner — resume a workflow run
that is waiting on a BPMN message, without Agentwerke needing a connector for that system. The
sender names the message, so a wait on `test.unit.completed` is satisfiable by whatever actually ran
the tests.

```json
{
  "messageName": "test.unit.completed",
  "correlationKey": "build-vmodel-001:unit",
  "payload": { "conclusion": "success", "report_url": "https://ci.example/junit.xml" }
}
```

The run resumes when `messageName` and `correlationKey` both match a waiting run; `payload`'s
top-level fields become run inputs. Unmatched events are recorded and return `resumed: false`.

Required headers:

| Header | Purpose |
| --- | --- |
| `X-Agentwerke-Source` | Registered source id from `Integrations:EventIngress:Sources`. |
| `X-Agentwerke-Signature-256` | `sha256=<hex>` HMAC-SHA256 of the raw body under that source's secret. |
| `X-Agentwerke-Delivery` | Optional idempotency key. Omitted, the body's signature digest is used. |

Configure senders under `Integrations:EventIngress`. The endpoint is disabled by default and
returns 404 until `Enabled` is true:

```json
{
  "Integrations": {
    "EventIngress": {
      "Enabled": true,
      "Sources": [
        {
          "Id": "ci",
          "Secret": "<shared secret>",
          "AllowedMessageNames": [ "test.unit.completed" ]
        }
      ]
    }
  }
}
```

Unlike the connector webhooks, an empty `Secret` does not skip signature validation — it disables
the source. This endpoint can satisfy a verification gate, so misconfiguration fails closed.
`AllowedMessageNames` is optional but recommended in production: it stops a leaked CI secret from
resuming unrelated waits. Redelivery is idempotent — a repeated delivery is recorded once and
resumes a run at most once.

## API usage pattern

1. Obtain a valid token for the target role.
2. Start or inspect a workflow run.
3. Follow pending approval or wait-state information.
4. Retrieve artifacts or evidence after completion.
5. Use audit and evidence data for investigation or records retention.
