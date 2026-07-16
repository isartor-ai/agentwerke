# Deployment

Agentwerke is self-hostable: Postgres + the API (+ optional Web UI), configured via
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

## Configuration

Agentwerke can be bootstrapped with environment variables and then operated through
the Admin-only Settings page. See [settings.md](settings.md) for the Settings UI,
redaction rules, local override files, and configuration precedence.

### Environment variables

Settings use the standard .NET `Section__Key` env mapping.

**Database**
- `ConnectionStrings__Postgres` — Postgres connection string.

**Workflow runtime**
- `WorkflowRuntime__Mode` — `Agentwerke` (default), `Camunda`, or the legacy `Agentwerke` alias.

**Model provider**
- `Anthropic__Provider` — `anthropic` | `mock` | (empty = auto).
- `Anthropic__ApiKey`, `Anthropic__Model` (default `claude-sonnet-4-6`),
  `Anthropic__MaxTokens`, `Anthropic__MaxToolIterations`.

**GitHub integration**
- `Integrations__GitHub__Enabled`, `__RepositoryOwner`, `__RepositoryName`,
  `__PersonalAccessToken`, `__DefaultBaseBranch`, `__BranchPrefix`,
  `__CreateDraftPullRequests`.

**Agent interaction channels**
- `Integrations__Interactions__Enabled`, `__DefaultChannels`, `__ChannelsByWorkflow`,
  `__ChannelsByAgent`, `__DefaultTimeoutSeconds`, `__SweepIntervalSeconds`,
  `__MaxDeliveryAttempts`, `__RetryBaseDelayMs`, and `__RespondUrlBase`.
- `Integrations__Slack__Enabled`, `__WebhookUrl`, `__SigningSecret`, optional `__BotToken`, and
  `__ToleranceSeconds`.
- `Integrations__Teams__Enabled`, `__WebhookUrl`. Teams incoming webhooks are outbound-only in v1;
  questions must be answered in Agentwerke or another response-capable channel.
- `Integrations__InteractionWebhook__Enabled`, `__Endpoint`, `__Secret`, `__TimeoutSeconds`, and
  `__ToleranceSeconds`.

See [interaction-channels.md](interaction-channels.md) for configuration precedence, provider setup,
the signed webhook contract, API reference, and the operational runbook.

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

### Settings override files

When an Admin saves supported values in `/settings`, Agentwerke writes non-secret
overrides to `config/settings.overrides.json` and local secret rotations to
`config/settings.secrets.json` by default. These files are loaded after
appsettings/env configuration and take effect after API restart. Use
`Settings__AllowLocalSecretWrites=false` in production if local secret files
should be disabled.

## Production hardening

Do **not** ship the dev defaults. Before production:

- **Auth:** disable dev tokens; configure a real `Jwt__SecretKey` (or enterprise
  SSO/OIDC — commercial tier). Put the API behind TLS.
- **Secrets:** inject `Anthropic__ApiKey`, GitHub PAT, and DB credentials from a
  secret manager, not plaintext compose. Settings never returns raw secrets, but
  production should still prefer deployment-managed secret stores over local
  file-backed writes.
- **Sandboxing:** enable a sandbox provider with an appropriate network policy for
  any agent that executes code; prefer OpenSandbox/Kubernetes for enforced egress.
- **Database:** managed Postgres with backups; run EF migrations (`migrate`
  service) on deploy.
- **Observability:** wire `Tracing__OtlpEndpoint` and scrape `/metrics`.

## Production deployment

Two production paths, both with real auth (no dev tokens), env/secret-store
config, and resource limits — see [`deploy/README.md`](../deploy/README.md):

- **Kubernetes (Helm):** [`deploy/helm/agentwerke`](../deploy/helm/agentwerke) — API +
  dispatch worker + Web (+ optional Postgres), HPA, RBAC, ingress with TLS,
  and an OpenSandbox sandbox provider.
- **Docker Compose (single host):** [`docker/docker-compose.prod.yml`](../docker/docker-compose.prod.yml)
  — pinned published images, dev modes off, secrets from env, behind your TLS proxy.

Kubernetes-native sandbox isolation is tracked in #36.

## Container images

Tagged releases publish `api`, `web`, and `agent-runner` images (see the release
workflow). Pin a released tag for production rather than building from `main`.
