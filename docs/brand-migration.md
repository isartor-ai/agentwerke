# Agentwerke brand migration

Agentwerke by Isartor AI is the new product name for the platform formerly known
as Autofac. The category is **Governed Lights-Out Software Factory**.

## What changed in this release

| Surface | New name | Legacy compatibility |
| --- | --- | --- |
| Product name | Agentwerke | "formerly Autofac" appears only as migration/history copy. |
| Runtime mode | `WorkflowRuntime:Mode=Agentwerke` | `WorkflowRuntime:Mode=Autofac` is accepted as an alias and emits a startup warning. |
| Helm chart | `deploy/helm/agentwerke` | Existing `deploy/helm/autofac` path is removed in the rename PR. |
| Default images | `agentwerke/api`, `agentwerke/web` | Registry aliases can be kept by release infrastructure during the transition. |
| Default issue label | `agentwerke` | Existing installs can keep `Integrations:GitHub:RequiredLabel=autofac`. |
| Default branch prefix | `agentwerke/run-` | Existing installs can keep `Integrations:GitHub:BranchPrefix=autofac/run-`. |
| Default service names | `agentwerke-api`, `agentwerke-worker` | Existing traces and dashboards may still contain `autofac-*` history. |
| BPMN XML prefix | `autofac:` | Intentionally retained as the stable workflow extension prefix. |
| .NET projects/namespaces | `Autofac.*` | Intentionally deferred to a separate technical migration. |

## Operator guidance

New deployments should use Agentwerke names in configuration, container images,
Helm releases, ingress hosts, and observability service names.

Existing deployments do not need a database migration for the brand change. Keep
current database names, schema names, branch prefixes, issue labels, and image
paths until you schedule an operational cutover. The only renamed runtime value
with built-in aliasing is `WorkflowRuntime:Mode`.

The public GitHub repository now resolves at `isartor-ai/agentwerke`; update
local remotes with:

```bash
git remote set-url origin https://github.com/isartor-ai/agentwerke.git
```

The private planning repository still resolves at `isartor-ai/autofac-private`
until a separate organization-level rename is scheduled.
