# Architecture

Agentwerke is a layered .NET control plane around governed agent execution.

```text
Trigger
  -> BPMN workflow run
      -> agent task -> Agent Orchestrator -> model
                              -> Tool Gateway -> policy -> connector or sandbox
      -> approval task -> human decision
      -> wait state -> timer or external event
  -> evidence pack
```

## Projects

| Project | Responsibility |
| --- | --- |
| `Agentwerke.Api` | API host for workflows, runs, approvals, artifacts, evidence, health, and webhooks. |
| `Agentwerke.Application` | Application use cases, run orchestration contracts, evidence builder, run context. |
| `Agentwerke.Domain` | Core entities and runtime contracts. |
| `Agentwerke.Workflows` | BPMN validator and in-process workflow engine. |
| `Agentwerke.Agents` | Agent orchestrator, model clients, Tool Gateway, Hook Gateway, skills, prompt assembler. |
| `Agentwerke.AgentSecOps` | Policy evaluation and action governance. |
| `Agentwerke.Sandboxes` | Docker, OpenSandbox, and Kubernetes sandbox providers. |
| `Agentwerke.Integrations` | External connectors such as GitHub and CI/CD. |
| `Agentwerke.Storage` | Artifact and blob storage abstractions. |
| `Agentwerke.Observability` | Logging, metrics, and tracing setup. |
| `Agentwerke.Infrastructure` | EF Core/Postgres persistence, outbox, dispatch worker. |
| `Agentwerke.AgentRunner` | In-sandbox runner image entrypoint. |

## Workflow runtime

`WorkflowRuntime:Mode=Agentwerke` is the default. It uses a bounded Postgres-backed in-process engine with durable checkpoints and asynchronous dispatch through a transactional outbox.

`WorkflowRuntime:Mode=Camunda` is an opt-in enterprise adapter. Camunda settings should not be consumed when the Agentwerke runtime is active.

## Agent layer

The agent layer selects an `ILanguageModelClient`, assembles prompts from agent profile, task prompt, skills, and run context, and enforces model budgets. Tool requests from the model do not execute directly. They go through the Tool Gateway.

Agent capabilities include:

- Knowledge retrieval with citations.
- Run-scoped agent messages.
- Inline agent delegation through `agent.request`.
- Blocking human questions through `human.ask`.
- Non-blocking human notifications.

## Governance layer

The Tool Gateway checks:

- agent permission level
- allowed and denied tools
- policy rules
- purpose and risk
- sandbox/profile boundaries

Policy decisions are recorded with rationale and risk factors. Human approval gates add explicit decisions to the workflow state.

## Persistence

Postgres stores workflows, runs, steps, events, run context, approvals, agent interactions, outbox state, and related metadata. Artifact bytes live in the configured storage provider.

See [Persistence Schema](/reference/persistence-schema) for table-level details.

## Web UI

The web UI is a React/Vite application that provides:

- workflow designer
- run board
- run detail with BPMN status
- approvals
- settings
- agent and skill authoring surfaces

The UI calls the API; it does not bypass product policy.
