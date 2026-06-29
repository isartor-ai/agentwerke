<div align="center">

# Autofac

### A dark software factory for governed AI delivery

**Autofac turns autonomous coding agents into a controlled production line: BPMN workflows, sandboxed execution, policy gates, human approvals, and tamper-evident evidence for every artifact the factory emits.**

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](global.json)
[![Runtime](https://img.shields.io/badge/runtime-BPMN--native-0A7BBB)](docs/decisions/ADR-002-use-bpmn-centric-autofac-runtime-by-default.md)
[![Model](https://img.shields.io/badge/agents-Claude-D97757)](src/Autofac.Agents/Models/AnthropicLanguageModelClient.cs)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

[Premise](#premise) | [Factory line](#factory-line) | [Quick start](#quick-start) | [Architecture](#architecture) | [API](#api-reference) | [Docs](#documentation)

</div>

---

## Premise

Autofac takes its name from Philip K. Dick's 1955 story [Autofac](https://en.wikipedia.org/wiki/Autofac): a postwar world where automatic factories keep manufacturing after meaningful human control has slipped away.

That warning is the product thesis.

AI agents can now plan, code, test, review, and open pull requests. Left alone, that power becomes another opaque production system: fast, tireless, difficult to interrogate, and too easy to trust because it looks useful.

Autofac is the countermeasure. It is not another coding agent. It is the control plane around the agents: the dark factory floor where every job has a process model, every tool call crosses a policy gate, every sandbox has a boundary, and every run leaves evidence behind.

- **Autonomy without blind trust.** Agents can act, but they do not get direct access to sensitive tools, credentials, networks, repositories, or deployment paths.
- **Workflow over vibes.** Software delivery is modeled as versioned BPMN, not hidden in a prompt transcript.
- **Sandboxes by default.** Agent work happens inside controlled Docker or OpenSandbox execution environments.
- **Humans at the right choke points.** Approval gates, wait states, and policy outcomes are part of the process, not exceptions bolted on later.
- **Evidence as exhaust.** Prompts, redactions, tool calls, policy decisions, costs, artifacts, and outcomes are captured into evidence packs.
- **Self-hosted by design.** Run the factory on your infrastructure with your model keys and your data boundary.

> The point is not to make agents harmless. The point is to make powerful automation inspectable, interruptible, and accountable.

## Factory line

```mermaid
flowchart LR
    A[Trigger<br/>API or webhook] --> B[BPMN factory run]
    B --> C{Work cell}
    C -->|agent task| D[Agent orchestrator]
    D --> E[Policy gate]
    E --> F[Claude<br/>tool-use loop]
    F --> G[Tool gateway]
    G --> H[(GitHub, CI/CD, MCP tools)]
    C -->|approval task| I[Human approval]
    C -->|wait state| K[External signal]
    B --> J[Evidence pack<br/>audit, artifacts, cost]
```

A run moves through the nodes of a BPMN model. When it reaches an agent task, the orchestrator assembles the agent profile, skill, run context, and available tools, then evaluates policy before the model receives work. Claude can request tool calls, but every call is brokered through the Tool Gateway, checked against policy, executed in the right boundary, and recorded. Approval tasks pause for a human. Wait states resume from external signals such as green CI or a merged PR. The final output is not just code; it is code plus the evidence of how it was produced.

## What is built

| Capability | Status |
| --- | --- |
| BPMN-native workflow runtime with durable checkpoints | Built |
| Real Claude-backed agents with a policy-enforced tool-use loop | Built |
| Tool Gateway, Hook Gateway, Skill repository, and prompt assembler | Built |
| Docker-sandboxed agent execution with offline and controlled profiles | Built |
| GitHub connector for issues, branches, pull requests, reviews, and CI triggers | Built |
| Evidence-pack export and artifact storage | Built |
| End-to-end `autonomous-sdlc` template: BA to architecture to implementation to review to CI/CD to test | In progress; live proof is tracked in the project backlog |
| Enterprise SSO, RBAC, and data-residency hardening | Roadmap |
| Optional Camunda 8 runtime adapter | Available; see ADR-002 |

## Quick start

**Try it in 5 minutes, no API keys** — the tokenless quickstart runs the full
platform (API + Web UI) on a deterministic mock model provider:

```bash
docker compose -f docker/docker-compose.quickstart.yml up --build
# → Web UI http://localhost:3002 · API http://localhost:8081
```

Then follow [docs/getting-started.md](docs/getting-started.md) to start the
seeded sample workflow through an agent step, an approval gate, and an evidence
pack.

### Build from source

```bash
dotnet restore Autofac.sln
dotnet build Autofac.sln
dotnet test Autofac.sln --no-build
```

Run the API locally:

```bash
dotnet run --project src/Autofac.Api/Autofac.Api.csproj
```

The OpenAPI document is served at `/openapi/v1.json`.

### Enabling real agents

Agents run against Claude when an API key is configured. Without a key, Autofac uses a safe null client and agent steps report that no model is configured.

```jsonc
// appsettings.json, environment variables, or user-secrets
"Anthropic": {
  "ApiKey": "sk-ant-...",
  "Model": "claude-sonnet-4-6",
  "MaxTokens": 4096,
  "MaxToolIterations": 10
}
```

## Architecture

Autofac is a layered .NET control plane. The domain model stays at the center; model providers, storage, sandboxes, workflow adapters, and external systems sit at the edge.

| Project | Responsibility |
| --- | --- |
| `src/Autofac.Api` | ASP.NET Core API host |
| `src/Autofac.Application` | Application use cases and orchestration contracts |
| `src/Autofac.Domain` | Core domain model and rules |
| `src/Autofac.Infrastructure` | Infrastructure adapters and implementations |
| `src/Autofac.Workflows` | BPMN runtime concerns |
| `src/Autofac.Agents` | Agent orchestration, model client, tool and hook gateways |
| `src/Autofac.AgentSecOps` | Policy enforcement and action governance |
| `src/Autofac.Sandboxes` | Sandbox lifecycle and controls |
| `src/Autofac.Integrations` | External platform connectors for GitHub, CI/CD, Jira, Slack, Teams, and more |
| `src/Autofac.Storage` | Artifact and blob abstractions |
| `src/Autofac.Observability` | Logging, metrics, and tracing wiring |
| `tests/` | Domain, agent, workflow, integration, and end-to-end tests |

### Workflow runtime mode

Autofac selects its execution runtime through the `WorkflowRuntime:Mode` setting:

| Value | Behavior |
| --- | --- |
| `Autofac` | Default. Uses the bounded, Postgres-backed Autofac runtime. Camunda configuration is not read and no Camunda client is constructed. |
| `Camunda` | Opt-in enterprise adapter. Camunda options, client, health probe, and status are wired. |

```jsonc
// appsettings.json
"WorkflowRuntime": {
  "Mode": "Autofac"
}
```

- Unsupported values, such as `WorkflowRuntime:Mode=Temporal`, fail fast at startup with an actionable error.
- The active mode is logged once on startup and exposed at `GET /api/health/runtime` and `GET /api/health/ready`.
- Camunda settings under the `Camunda` section are consumed only when `Mode=Camunda`.

## API reference

**Health and runtime**

- `GET /api/health/live`
- `GET /api/health/ready` - includes the active `runtimeMode`
- `GET /api/health/runtime` - active workflow runtime mode
- `GET /api/health/camunda` - Camunda topology; inactive unless runtime mode is `Camunda`

**Auth**

- `GET /api/auth/config`
- `POST /api/auth/token`

**Workflows and runs**

- `GET /api/workflows`
- `GET /api/workflows/{workflowId}`
- `GET /api/runs`
- `GET /api/runs/{runId}`
- `POST /api/runs`

**Artifacts by run ID**

- `POST /api/runs/{runId}/artifacts/{artifactName}` - upload artifact bytes
- `GET /api/runs/{runId}/artifacts/{artifactName}` - download artifact

**Evidence pack by run ID**

- `GET /api/runs/{runId}/evidence-pack` - generate evidence pack JSON
- `GET /api/runs/{runId}/evidence-pack/download` - download evidence pack JSON

### Persistence and storage

- Schema documentation: `docs/persistence-schema.md`
- Local artifact storage: `Storage:RootPath`
- Defaults: `./storage` for production-like runs, `./storage-dev` in development

### Authentication and data residency

- Enterprise SSO/RBAC and self-hosted data boundary guidance: `docs/deployment-auth-data-residency.md`

## Documentation

- **Architecture decisions**
  - Default workflow runtime: `docs/decisions/ADR-002-use-bpmn-centric-autofac-runtime-by-default.md`
  - Superseded Camunda-first decision: `docs/decisions/ADR-001-use-camunda8-for-production-bpmn-runtime.md`
  - OpenSandbox control plane with Kata runtime: `docs/decisions/ADR-003-use-opensandbox-control-plane-with-kata-runtime.md`
- **Plans and scenarios**
  - Architecture design: `docs/architecture-design.md`
  - Functional specification: `docs/functional-specification.md`
  - End-to-end autonomous SDLC scenario: `docs/manual-test-sdlc-e2e.md`
  - OpenSandbox manual test scenario: `docs/manual-test-opensandbox.md`
  - UI cleanup/refactor plan: `docs/ui-cleanup-refactor-plan.md`
- **Contributing**: see `CONTRIBUTING.md`

## License

[Apache-2.0](LICENSE). Autofac is **open core** — the full self-hostable platform is Apache-2.0; a separately-licensed commercial tier adds enterprise governance, SSO/RBAC, scale, and compliance features. See [docs/open-core.md](docs/open-core.md) for the boundary.

---

<div align="center">
<sub>Autofac | dark software factory | BPMN-native, sandboxed, policy-governed autonomous delivery</sub>
</div>
