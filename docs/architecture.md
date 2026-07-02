# Architecture (as-built)

This is the current architecture of Autofac. (The older
[`architecture-design.md`](architecture-design.md) captures the longer-term
target design; this document reflects what's actually built.)

## Overview

Autofac is a layered .NET control plane that runs AI agents inside a governed,
BPMN-defined software factory. A run walks the nodes of a BPMN model; agent
service tasks are dispatched to the agent layer, which calls a model and brokers
every tool call through a policy-enforced gateway, optionally inside a sandbox.

```
Trigger (API / GitHub webhook)
   → BPMN workflow run (engine)
       → service task → Agent Orchestrator → model (Claude / mock)
                              → Tool Gateway (policy) → connectors / sandbox
       → user task → approval gate (human)
       → wait state → external event (webhook) / timer
   → Evidence pack (audit, artifacts, cost)
```

## Components (by project)

| Project | Responsibility |
| --- | --- |
| `Autofac.Api` | ASP.NET Core host: workflows, runs, approvals, artifacts, evidence, health. |
| `Autofac.Application` | Use cases & orchestration contracts (run orchestration, evidence builder, run context). |
| `Autofac.Domain` | Core domain model (agent runtime contracts, persistence entities). |
| `Autofac.Workflows` | BPMN validator + the in-process workflow engine. |
| `Autofac.Agents` | Agent orchestrator, model client(s), Tool Gateway, Hook Gateway, skills, prompt assembler. |
| `Autofac.AgentSecOps` | Policy evaluation & action governance. |
| `Autofac.Sandboxes` | Sandbox lifecycle (Docker / OpenSandbox / Kubernetes). |
| `Autofac.Integrations` | Connectors (GitHub, CI/CD, …). |
| `Autofac.Storage` | Artifact / blob storage. |
| `Autofac.Observability` | Logs, metrics, tracing. |
| `Autofac.Infrastructure` | EF Core / Postgres persistence, outbox + dispatch worker. |
| `Autofac.AgentRunner` | The in-sandbox runner image entrypoint. |

## Execution runtime

- **Default (`WorkflowRuntime:Mode=Autofac`):** a bounded, Postgres-backed
  in-process engine (`EngineId = "in-process"`) with event-sourced checkpoints.
  This is the default ([ADR-002](decisions/ADR-002-use-bpmn-centric-autofac-runtime-by-default.md)).
- **Opt-in (`Mode=Camunda`):** an enterprise Camunda 8 adapter.

Runs are dispatched asynchronously via a transactional **outbox** drained by a
background `RunDispatchWorker`, so the API returns immediately and the engine
advances the run (and resumes it on approval / external event / timer).

## Model providers

The agent layer talks to an `ILanguageModelClient`, selected by
`Anthropic:Provider`: `anthropic` (real, via `IHttpClientFactory` with retries +
prompt caching), `openai`/`litellm` (any OpenAI Chat Completions-compatible
endpoint — OpenAI, Azure OpenAI, or a LiteLLM proxy), `mock` (deterministic,
zero-cost — for demos/CI), or a null client when nothing is configured. Per-run
**cost/token budgets** (`MaxRunCostUsd`/`MaxRunTokens`) halt a run's model calls
once exceeded.

## Agent capabilities

Beyond the model loop, agents have policy-gated tools for **knowledge retrieval**
(`knowledge.search` over `IKnowledgeRetriever`, with citations), **inter-agent
coordination** (`agent.post_message`/`agent.read_messages` on a run-scoped bus),
**delegation** (`agent.request` runs another agent inline and returns its result), and
**asking a human** (`human.ask` pauses the run until the person answers; `human.notify`
sends a non-blocking heads-up).

A blocking `human.ask` suspends the run (`waiting_user`) with the checkpoint pointing
back at the same step, so answering re-runs the step and the agent proceeds with the
answer in hand — no thread is held while a person is away. All of these exchanges are
persisted as **`AgentInteraction`** records — one run-scoped store that also backs the
run **Conversation** view — so the full agent-to-agent and agent-to-human history is
auditable and survives restart. `agent.request` is depth-guarded: the callee runs
read-only and cannot delegate again or pause the run. Human approval decisions are
captured as per-agent **feedback** and aggregated into a scorecard.

## Governance & evidence

The Tool Gateway enforces policy on every action; approval gates add humans where
needed (including interactive **approve/reject from Slack**); and each run produces
a tamper-evident evidence pack. Policy rules are data-driven with a
**draft → simulate → publish** lifecycle and impact analysis, and every decision
carries a purpose-confidence + risk score. Enterprise auth covers OIDC SSO, RBAC,
and **LDAP/AD** group-to-role mapping. See [security-model.md](security-model.md).

## Web UI

A React + bpmn-js front end: workflow designer, run board, run detail (BPMN with
live per-step status), and approvals.
