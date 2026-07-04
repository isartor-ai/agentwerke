# Auth And Data Residency

Agentwerke protects product APIs with JWT bearer authentication and role-based authorization. Health probes, auth discovery/dev-token endpoints, and signed integration webhooks remain anonymous by design.

## Authentication modes

### Enterprise OIDC/JWT

Configure the API to validate tokens from your identity provider.

Microsoft Entra ID example:

```bash
Jwt__Authority=https://login.microsoftonline.com/<tenant-id>/v2.0
Jwt__Audience=api://agentwerke
```

Keycloak example:

```bash
Jwt__Authority=https://keycloak.example.com/realms/<realm>
Jwt__Audience=agentwerke-api
```

Agentwerke normalizes role claims from `role`, `roles`, `groups`, and the standard .NET role claim type. Configure additional claim names when needed:

```bash
Jwt__RoleClaimTypes__0=roles
Jwt__RoleClaimTypes__1=groups
Jwt__NameClaimTypes__0=preferred_username
Jwt__NameClaimTypes__1=email
```

Map external groups to Agentwerke roles:

```bash
Jwt__RoleMappings__agentwerke_viewers__0=Viewer
Jwt__RoleMappings__agentwerke_operators__0=Operator
Jwt__RoleMappings__agentwerke_approvers__0=Approver
Jwt__RoleMappings__agentwerke_admins__0=Admin
```

### Symmetric JWT for local stacks

Local and manual-test stacks can use a development signing key:

```bash
Jwt__SecretKey=agentwerke-dev-manual-testing-key-2026
Jwt__Issuer=agentwerke-dev
Jwt__Audience=agentwerke-dev
Jwt__DevTokensEnabled=true
```

Do not enable development tokens in production.

### Development identity mode

`appsettings.Development.json` can authenticate requests without a bearer token as `dev:admin` when `ASPNETCORE_ENVIRONMENT=Development`. Disable this in shared development environments that need explicit bearer tokens:

```bash
Jwt__DevIdentityEnabled=false
```

## Roles

| Role | Access |
| --- | --- |
| `Viewer` | Read workflows, runs, approvals, templates, agents, and skills. |
| `Operator` | Viewer access plus workflow import/validation, run start/cancel/recover, policy simulation, and artifact upload. |
| `Approver` | Viewer access plus approval decisions. |
| `Admin` | Operator and approver access plus workflow publish, settings, and policy-rule management. |

## Data residency boundaries

The default self-hosted deployment keeps workflow definitions, runs, approvals, audit records, run context, and outbox state in the configured PostgreSQL database.

Artifact bytes stay in the configured storage provider:

| Provider | Boundary |
| --- | --- |
| `filesystem` | Files under `Storage:RootPath`. |
| `s3` | Bucket and region configured by the operator. |

The default `WorkflowRuntime:Mode=Agentwerke` does not require Camunda. Camunda settings are consumed only when `WorkflowRuntime:Mode=Camunda`.

## External data flows

Data leaves the self-hosted boundary only through explicitly enabled integrations and model providers:

- GitHub, Jira, Slack, and Teams connectors.
- Model-provider endpoints.
- Artifact storage if it is outside the host or cluster.
- Telemetry endpoints such as OTLP collectors.

For German or EU residency requirements, choose customer-approved regions for Postgres, artifacts, and model endpoints, and verify connector tenant boundaries.

## Production checklist

- Disable dev identity and dev tokens.
- Use real JWT/OIDC authority and audience settings.
- Map enterprise roles deliberately.
- Store identity-provider secrets outside the repository.
- Document the configured data boundary.
- Review enabled integrations before production traffic.
- Confirm evidence packs do not contain unexpected sensitive content.
