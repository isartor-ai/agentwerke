# Enterprise authentication and data residency

Agentwerke protects product APIs with JWT bearer authentication and role-based
authorization. Health probes, auth discovery/dev-token endpoints, and signed
integration webhooks remain anonymous by design.

## Authentication modes

### Enterprise OIDC/JWT

Configure the API to validate tokens from the enterprise identity provider:

```bash
Jwt__Authority=https://login.microsoftonline.com/<tenant-id>/v2.0
Jwt__Audience=api://agentwerke
```

For Keycloak, use the realm issuer:

```bash
Jwt__Authority=https://keycloak.example.com/realms/<realm>
Jwt__Audience=agentwerke-api
```

Agentwerke normalizes role claims from `role`, `roles`, `groups`, and the standard
.NET role claim type into its internal role checks. If your provider emits
roles under a different claim, configure:

```bash
Jwt__RoleClaimTypes__0=roles
Jwt__RoleClaimTypes__1=groups
Jwt__NameClaimTypes__0=preferred_username
Jwt__NameClaimTypes__1=email
```

If the provider emits enterprise group IDs or external app-role names, map them
to the built-in Agentwerke roles explicitly:

```bash
Jwt__RoleMappings__agentwerke_viewers__0=Viewer
Jwt__RoleMappings__agentwerke_operators__0=Operator
Jwt__RoleMappings__agentwerke_approvers__0=Approver
Jwt__RoleMappings__agentwerke_admins__0=Admin
```

Use JSON configuration or a secret/config provider for external group IDs that
contain punctuation not allowed in shell variable names.

For Microsoft Entra ID, assign app roles matching the Agentwerke roles below and
emit them in the `roles` claim. For Keycloak, add a protocol mapper that emits
realm or client roles into a top-level `roles` claim.

### Symmetric JWT for local stacks

Local and manual-test stacks can use a development signing key:

```bash
Jwt__SecretKey=agentwerke-dev-manual-testing-key-2026
Jwt__Issuer=agentwerke-dev
Jwt__Audience=agentwerke-dev
Jwt__DevTokensEnabled=true
```

When `Jwt:DevTokensEnabled=true`, `POST /api/auth/token` can issue a short-lived
development token for one of the Agentwerke roles. Do not enable this in production.

### Development identity mode

`appsettings.Development.json` enables `Jwt:DevIdentityEnabled=true`, which
authenticates requests without a bearer token as `dev:admin` only when
`ASPNETCORE_ENVIRONMENT=Development`. Set `Jwt__DevIdentityEnabled=false` in
shared development environments that must require explicit bearer tokens.

## Roles and protected actions

| Role | Access |
| --- | --- |
| `Viewer` | Read product surfaces such as workflows, runs, approvals, templates, agents, and skills. |
| `Operator` | Viewer access plus workflow import/validation, template clone, run start/cancel/recover, policy simulation, and artifact upload. |
| `Approver` | Viewer access plus approval decisions. |
| `Admin` | All operator and approver access plus workflow publish and policy-rule management. |

Run start audit records use the authenticated principal as the run initiator.
Approval decisions write the authenticated principal into `ApprovalRequest.DecidedBy`
and the audit actor.

## Data residency boundaries

The default self-hosted deployment keeps workflow definitions, runs, approvals,
audit records, run context, and outbox state in the configured PostgreSQL
database. Artifact bytes stay in the configured storage provider:

- `Storage:Provider=filesystem` stores artifacts under `Storage:RootPath`.
- `Storage:Provider=s3` stores artifacts in the configured bucket and region.

The default `WorkflowRuntime:Mode=Agentwerke` does not require Camunda. The
legacy `WorkflowRuntime:Mode=Agentwerke` value is accepted as an alias during the
rename transition. Camunda settings are only consumed when
`WorkflowRuntime:Mode=Camunda`.

External data leaves the self-hosted boundary only through explicitly enabled
integrations and model providers:

- Configure Jira, GitHub, Slack, and Teams only with tenant-approved endpoints.
- Configure model-provider endpoints and API keys through environment variables
or secret stores, not committed configuration.
- Prefer EU or customer-dedicated regions for PostgreSQL, artifact storage, and
model endpoints when the deployment has German or EU residency requirements.

Never commit real identity-provider secrets, signing keys, connector tokens, or
model-provider API keys. Use environment variables, Kubernetes secrets, or the
platform secret store for all deployment credentials.
