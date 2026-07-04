# Deployment

Agentwerke is self-hostable. A production deployment needs the API, web UI, Postgres, artifact storage, a model provider, integration credentials, and a sandbox strategy for agent execution.

## Deployment choices

| Path | Use when |
| --- | --- |
| Quickstart Compose | You want a five-minute local demo with mock model output. |
| Development Compose | You are developing or manually testing with local services and optional credentials. |
| Production Compose | You want a single-host deployment behind your own TLS proxy. |
| Kubernetes Helm | You need production scheduling, scaling, ingress, RBAC, and stronger sandbox integration. |

## Compose stacks

| File | Purpose |
| --- | --- |
| `docker/docker-compose.quickstart.yml` | Tokenless demo: mock model, GitHub off. |
| `docker/docker-compose.manual.yml` | Manual UI testing with simulated services. |
| `docker/docker-compose.yml` | Development stack with Postgres, API, Web, and Jaeger. |
| `docker/docker-compose.e2e.yml` | End-to-end test stack. |
| `docker/docker-compose.prod.yml` | Single-host production-style deployment using published images. |

## Production configuration checklist

- Disable development tokens and development identity.
- Configure real JWT/OIDC settings.
- Inject model keys, GitHub tokens, database credentials, and webhook secrets through a secret manager or deployment environment.
- Configure Postgres with backups.
- Configure artifact storage and retention.
- Enable a sandbox provider appropriate for code execution.
- Put the API and web UI behind TLS.
- Enable tracing and scrape `/metrics`.
- Pin released container image tags instead of building production from `main`.

## Core environment variables

### Database

```bash
ConnectionStrings__Postgres=<postgres-connection-string>
```

### Workflow runtime

```bash
WorkflowRuntime__Mode=Agentwerke
```

Use `Camunda` only when the enterprise adapter is enabled and configured.

### Model provider

```bash
Anthropic__Provider=anthropic
Anthropic__ApiKey=<secret>
Anthropic__Model=claude-sonnet-4-6
```

### GitHub integration

```bash
Integrations__GitHub__Enabled=true
Integrations__GitHub__RepositoryOwner=<owner>
Integrations__GitHub__RepositoryName=<repo>
Integrations__GitHub__PersonalAccessToken=<secret>
Integrations__GitHub__WebhookSecret=<secret>
```

### Storage and observability

```bash
Storage__Provider=filesystem
Storage__RootPath=/var/lib/agentwerke/artifacts
Tracing__OtlpEndpoint=http://jaeger:4317
Tracing__ServiceName=agentwerke-api
```

## Kubernetes path

Use the Helm chart under `deploy/helm/agentwerke` for API, dispatch worker, Web, optional Postgres, HPA, RBAC, ingress, TLS, and sandbox-provider wiring. Review `deploy/README.md` before installing into a real cluster.

## Single-host Compose path

Use `docker/docker-compose.prod.yml` behind a TLS reverse proxy. Keep secrets outside the compose file, pin image tags, and ensure persistent volumes cover database and artifact storage.

## Smoke test after deploy

```bash
curl -sf https://<host>/api/health/live
curl -sf https://<host>/api/health/ready
```

Then:

1. Sign in or obtain a valid token.
2. Open the web UI.
3. Start a low-risk mock or test workflow.
4. Approve its gate.
5. Download the evidence pack.
6. Confirm audit logs and metrics are visible.
