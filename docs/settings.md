# Settings

Autofac now has an Admin-only Settings control plane at `/settings` and
`/api/settings`. It centralizes the configuration operators need most often:
model provider, integrations, authentication, runtime mode, storage, sandboxing,
observability, and Settings storage itself.

## Access model

- The Settings API is protected by the `Admin` policy.
- The web page is hidden behind the same Admin intent and does not fetch settings
  for non-admin users.
- Every settings update and readiness check writes a redacted audit record.

## Configuration precedence

Autofac still boots from normal .NET configuration: `appsettings*.json`,
environment variables, user-secrets, Helm values, Compose env files, and any
deployment-provided secret source. The Settings layer adds two local files that
are loaded after bootstrap configuration:

1. `config/settings.overrides.json` for non-secret runtime overrides.
2. `config/settings.secrets.json` for local secret rotations.

Both paths can be changed with:

```text
Settings__OverridesPath=/path/to/settings.overrides.json
Settings__SecretsPath=/path/to/settings.secrets.json
```

Because these files are loaded after appsettings/env configuration, values saved
from Settings take precedence after the API restarts. The UI marks saved changes
as restart-required because most existing services bind options at startup.

The default settings files are ignored by git. Do not commit environment-specific
runtime overrides or local secret files.

## Secret handling

Secret fields are never returned as raw values by `/api/settings`. The API returns
only:

- configured or missing status
- source (`configuration`, `settings-secret-file`, or `missing`)
- a short fingerprint
- whether local secret writes are enabled

Secret rotation inputs are write-only in the UI. After save, the browser clears
the input and only displays redacted status. Audit events include the secret path
that changed but not the secret value.

Local file-backed secret writes are intended for development and simple
self-hosted installs. Production deployments should prefer Kubernetes Secrets,
cloud secret managers, or environment injection. Disable local writes with:

```text
Settings__AllowLocalSecretWrites=false
```

## Settings groups

The Settings catalog currently includes:

- **Model:** `Anthropic:*` provider, model, limits, retries, prompt caching, and
  API key status.
- **Integrations:** Slack, Teams, Jira, GitHub, notification behavior, webhook
  secrets, tokens, repo defaults, and trigger actions.
- **Authentication:** `Jwt:*` issuer/audience/authority, dev auth controls,
  claim types, role mappings status, and JWT signing key status.
- **Runtime:** `WorkflowRuntime:*`, policy file, and optional Camunda adapter
  settings.
- **Storage:** Postgres connection status, filesystem/S3 artifact settings, and
  S3 secret key status.
- **Sandbox:** Docker, OpenSandbox, and Kubernetes Kata provider controls.
- **Observability:** tracing settings plus Settings storage paths.

Some fields remain read-only from the Settings UI because changing them can lock
out operators or requires deployment-level coordination. Examples include the
Postgres connection string and Settings file paths.

## Readiness checks

The Settings page includes dry-run readiness checks for:

- model provider
- GitHub
- Jira
- Slack
- Teams
- Camunda

These checks validate local configuration completeness and audit the result. They
do not send chat messages or mutate external systems.

## API shape

- `GET /api/settings` returns the redacted settings catalog and current values.
- `PATCH /api/settings` accepts:

```json
{
  "values": {
    "Anthropic:Provider": "mock",
    "Integrations:GitHub:Enabled": true
  },
  "secrets": {
    "Anthropic:ApiKey": "new-value-sent-once"
  }
}
```

- `POST /api/settings/tests/{target}` runs a dry-run readiness check.

Secret paths sent under `values` are rejected. Non-secret paths sent under
`secrets` are rejected. Unknown paths are rejected.
