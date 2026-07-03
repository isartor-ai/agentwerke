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
| .NET projects/namespaces | `Agentwerke.*` | Renamed from `Autofac.*`. See "Internal .NET rename" below. |

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

## Internal .NET rename

The .NET solution, projects, assemblies, and namespaces were renamed from
`Autofac.*` to `Agentwerke.*` (issue #196, Phase 7 Option B):

- `Autofac.sln` → `Agentwerke.sln`; every `src/Autofac.*` and `tests/Autofac.*`
  project directory, `.csproj`, assembly, and namespace now uses `Agentwerke.*`.
- `using`/qualified type references, EF Core entity-identity strings in the
  migration snapshot/designers, `Dockerfile` entrypoints, Docker Compose build
  paths, CI/CodeQL/release workflows, and helper scripts were updated to match.
- Branding-only type names were also renamed: `AutofacDbContext` →
  `AgentwerkeDbContext`, `AutofacRoles`/`AutofacPolicies` →
  `AgentwerkeRoles`/`AgentwerkePolicies`, `AutofacRoleMapper` →
  `AgentwerkeRoleMapper`, and the `AddAutofac*`/`UseAutofacObservability` DI
  extension methods → `AddAgentwerke*`/`UseAgentwerkeObservability`.

Deliberately **not** renamed, because they are stable external/compat contracts,
not .NET identity:

- The BPMN extension prefix `autofac:` and namespace
  `https://autofac.de/bpmn/extensions/v1`, plus the C# types that model it
  (`AutofacTaskMetadata`, `AutofacApprovalMetadata`, `AutofacExternalEventMetadata`,
  `AutofacNs`).
- The `WorkflowRuntime:Mode=Autofac` legacy alias (`LegacyAutofacMode = "Autofac"`).
- Database table names, `autofac` issue label / branch prefix defaults, and
  persisted runtime values (no database migration is introduced).

No table names or persisted values changed, so existing databases keep working
without migration. Verified with `dotnet build Agentwerke.sln` and
`dotnet test Agentwerke.sln --filter "FullyQualifiedName!~Agentwerke.E2ETests"`
(the E2E suite requires the Docker stack and is excluded from unit CI).
