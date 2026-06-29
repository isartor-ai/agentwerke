# Security model

Autofac's premise is **autonomy without blind trust**: agents can act, but every
action crosses a governance boundary and leaves evidence. This document explains
those boundaries. For vulnerability reporting see [SECURITY.md](../SECURITY.md).

## Layers of control

1. **Process boundary (BPMN).** What an agent may attempt is defined by a
   versioned workflow, not an open-ended prompt. Approval gates and wait states
   are first-class nodes.
2. **Policy-enforced Tool Gateway.** Every tool/connector call is brokered by the
   Tool Gateway, which evaluates policy *before* the action runs and records the
   invocation. Decisions are `allow`, `escalate`, or `reject`.
3. **Permissions.** Each agent task carries a `permissionLevel`
   (`read-only` / `read-write` / `full`) plus `allowedTools` / `deniedTools`
   lists. The gateway denies anything outside the granted set.
4. **Sandboxed execution.** Agent/tool steps can run in an isolated container
   (Docker / OpenSandbox / Kubernetes) selected by `sandboxProfile`.
5. **Human approval gates.** High-risk steps pause for an explicit decision.
6. **Evidence & audit.** Every run emits a tamper-evident evidence pack.

## Sandbox network policy

Sandbox profiles map to a network policy. With the Docker provider:

| Profile | Network |
| --- | --- |
| `offline` | none (no egress) |
| `repo-read` / `repo-write` | restricted (source-control hosts) |
| `deployment` | restricted (deployment targets) |

> Plain Docker bridge networking cannot enforce per-host allow-listing on its
> own — `None` maps to no network; any other mode maps to bridge networking.
> For kernel-enforced egress allow-lists use an egress proxy / the OpenSandbox or
> Kubernetes providers.

## Secrets

- Model API keys and the GitHub PAT are read via an `ISecretStore` (configuration
  or environment) and injected only where needed (e.g. forwarded to a sandbox as
  `AUTOFAC_MODEL_API_KEY`).
- Prompts are **redacted** in the persisted snapshot; do not place secrets in
  prompts, run context, or committed artifacts.
- Never commit secrets. The evidence pack and logs are designed to avoid leaking
  credentials — report any leak per [SECURITY.md](../SECURITY.md).

## Authentication

The API supports JWT auth with role-based policies (`Viewer`, `Operator`,
`Approver`, `Admin`). The dev stacks enable **dev tokens / a dev JWT secret** for
zero-friction local use — these are **not** for production. Enterprise SSO/OIDC +
fine-grained RBAC is the commercial tier (see
[deployment-auth-data-residency.md](deployment-auth-data-residency.md) and
[open-core.md](open-core.md)).

## Evidence pack

`GET /api/runs/{runId}/evidence-pack` returns a structured, schema-versioned
record of a run: prompts (redacted), model usage/cost, tool invocations, policy
decisions, sandbox executions, approvals, artifacts, and the audit log — with the
workflow's BPMN hash for integrity.
