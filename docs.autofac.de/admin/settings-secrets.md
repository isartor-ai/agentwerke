# Settings And Secrets

Autofac has an Admin-only Settings control plane at `/settings` and `/api/settings`. It centralizes the values operators need most often: model provider, integrations, authentication, runtime mode, storage, sandboxing, observability, and Settings storage.

## Access model

- The Settings API is protected by the `Admin` policy.
- Non-admin users should not fetch or mutate settings.
- Every settings update and readiness check writes a redacted audit record.

## Configuration precedence

Autofac starts from normal .NET configuration:

- `appsettings*.json`
- environment variables
- user secrets
- Helm values
- Compose env files
- deployment-provided secret sources

The Settings layer adds two local files that are loaded after bootstrap configuration:

| File | Purpose |
| --- | --- |
| `config/settings.overrides.json` | Non-secret runtime overrides. |
| `config/settings.secrets.json` | Local secret rotations. |

Change the default paths:

```bash
Settings__OverridesPath=/path/to/settings.overrides.json
Settings__SecretsPath=/path/to/settings.secrets.json
```

Because Settings files load after appsettings and environment values, saved settings take precedence after API restart. The UI marks most saved changes as restart-required because services bind options at startup.

## Secret handling

Secret fields are never returned as raw values by `/api/settings`. The API returns:

- configured or missing status
- source
- short fingerprint
- whether local secret writes are enabled

Secret rotation inputs are write-only in the UI. After save, the browser clears the input. Audit records include the changed path but not the value.

## Production secret policy

Local file-backed secret writes are useful for development and simple self-hosted installs. Production should prefer Kubernetes Secrets, cloud secret managers, environment injection, or a platform secret store.

Disable local secret writes:

```bash
Settings__AllowLocalSecretWrites=false
```

Never commit:

- model API keys
- GitHub personal access tokens
- webhook secrets
- JWT signing keys
- database credentials
- identity-provider client secrets

## Settings groups

| Group | Examples |
| --- | --- |
| Model | Provider, model, limits, retries, prompt caching, API key status. |
| Integrations | GitHub, Jira, Slack, Teams, webhook secrets, repository defaults. |
| Authentication | JWT issuer, audience, authority, dev auth controls, role mappings. |
| Runtime | Workflow runtime, policy file, optional Camunda settings. |
| Storage | Postgres status, filesystem/S3 artifact settings. |
| Sandbox | Docker, OpenSandbox, Kubernetes Kata provider controls. |
| Observability | Tracing settings and Settings storage paths. |

## Readiness checks

Settings can run dry-run readiness checks for:

- model provider
- GitHub
- Jira
- Slack
- Teams
- Camunda

These checks validate configuration completeness and audit the result. They should not send chat messages or mutate external systems.

## API shape

Read the redacted catalog:

```bash
curl -sf "$API/api/settings" \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq
```

Patch supported values:

```bash
curl -sf "$API/api/settings" \
  -X PATCH \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "values": {
      "Anthropic:Provider": "mock",
      "Integrations:GitHub:Enabled": true
    },
    "secrets": {
      "Anthropic:ApiKey": "new-value-sent-once"
    }
  }'
```

Secret paths sent under `values` are rejected. Non-secret paths sent under `secrets` are rejected. Unknown paths are rejected.
