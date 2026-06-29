# Deployment

Autofac is self-hostable: Postgres + the API (+ optional Web UI), configured via
environment variables. This guide covers the shipped stacks, key configuration,
and production hardening. For auth/data-residency specifics see
[deployment-auth-data-residency.md](deployment-auth-data-residency.md).

## Compose stacks

| File | Purpose | Credentials |
| --- | --- | --- |
| `docker/docker-compose.quickstart.yml` | Try it in 5 min (mock model, GitHub off) | none |
| `docker/docker-compose.manual.yml` | Manual UI testing (WireMock, simulated) | none |
| `docker/docker-compose.yml` | Dev stack (Postgres + API + Web + Jaeger) | optional |
| `docker/docker-compose.e2e.yml` | E2E test stack | none |

Quickest path: see [getting-started.md](getting-started.md).

## Configuration (environment variables)

Settings use the standard .NET `Section__Key` env mapping.

**Database**
- `ConnectionStrings__Postgres` — Postgres connection string.

**Workflow runtime**
- `WorkflowRuntime__Mode` — `Autofac` (default) or `Camunda`.

**Model provider**
- `Anthropic__Provider` — `anthropic` | `mock` | (empty = auto).
- `Anthropic__ApiKey`, `Anthropic__Model` (default `claude-sonnet-4-6`),
  `Anthropic__MaxTokens`, `Anthropic__MaxToolIterations`.

**GitHub integration**
- `Integrations__GitHub__Enabled`, `__RepositoryOwner`, `__RepositoryName`,
  `__PersonalAccessToken`, `__DefaultBaseBranch`, `__BranchPrefix`,
  `__CreateDraftPullRequests`.

**Sandboxes**
- `Sandboxes__Enabled`, `Sandboxes__Provider` (`docker` / OpenSandbox / k8s),
  `Sandboxes__Docker__Enabled`, `Sandboxes__Docker__DockerEndpoint`.

**Storage / observability**
- `Storage__RootPath` (artifacts).
- `Tracing__OtlpEndpoint` (e.g. Jaeger), `Tracing__ServiceName`; Prometheus
  metrics at `/metrics`.

**Auth**
- `Jwt__SecretKey`, `Jwt__Issuer`, `Jwt__Audience`. Dev stacks also set
  `Jwt__DevTokensEnabled=true` — **development only**.

## Production hardening

Do **not** ship the dev defaults. Before production:

- **Auth:** disable dev tokens; configure a real `Jwt__SecretKey` (or enterprise
  SSO/OIDC — commercial tier). Put the API behind TLS.
- **Secrets:** inject `Anthropic__ApiKey`, GitHub PAT, and DB credentials from a
  secret manager, not plaintext compose.
- **Sandboxing:** enable a sandbox provider with an appropriate network policy for
  any agent that executes code; prefer OpenSandbox/Kubernetes for enforced egress.
- **Database:** managed Postgres with backups; run EF migrations (`migrate`
  service) on deploy.
- **Observability:** wire `Tracing__OtlpEndpoint` and scrape `/metrics`.

A production-grade compose/Helm profile is tracked for 1.0
(isartor-ai/autofac-private#160); Kubernetes sandboxing is #36.

## Container images

Tagged releases publish `api`, `web`, and `agent-runner` images (see the release
workflow). Pin a released tag for production rather than building from `main`.
