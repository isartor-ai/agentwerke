# ADR-001: Use Camunda 8 as the Production BPMN Runtime

## Status
Superseded by [ADR-002: Use BPMN-Centric Agentwerke Runtime by Default](ADR-002-use-bpmn-centric-agentwerke-runtime-by-default.md)

This ADR is retained for historical context only. Do not use the implementation guidance below for new work unless ADR-002's re-decision triggers are met.

## Date
2026-06-16

## Context
Agentwerke is intended to become a dark software factory: a straightforward SDLC automation product where users configure their delivery process, assign agent work, set policy gates, and observe execution.

The current architecture and MVP plan describe BPMN as the primary workflow format, but they also include a custom in-process C# BPMN runtime. That custom runtime has been useful for early validation, but continuing to expand it would move engineering effort away from the product differentiators:

- SDLC process authoring
- agent execution contracts
- policy and approval gates
- GitHub, Jira, CI, and sandbox integrations
- evidence capture and auditability
- operator-facing monitoring

Dark software factory workflows are long-running and durable. They wait on agent jobs, human approvals, webhooks, timers, retries, CI results, and external system callbacks. These concerns are core BPMN engine responsibilities.

## Decision
Use Camunda 8 as the production BPMN execution runtime from the start of the next implementation phase.

Agentwerke will own:

- SDLC process builder UX
- Agentwerke task metadata and agent assignment semantics
- BPMN generation and validation for Agentwerke-supported patterns
- agent worker execution
- sandboxing, policy, evidence, audit, and artifacts
- the operational UI and API surface

Camunda 8 will own:

- BPMN deployment
- durable process instance execution
- service task job scheduling
- user task waiting states
- timers, retries, incidents, and process state
- process variables and execution keys

The current in-process runtime should stop growing as a product runtime. It may remain temporarily for unit tests, local simulation, or migration support, but production execution should go through a Camunda-backed `IWorkflowEngineAdapter`.

## Mapping

| Agentwerke concept | Camunda 8 construct |
| --- | --- |
| Agent task | BPMN service task with `zeebe:taskDefinition` and an Agentwerke job worker |
| Human approval or review | BPMN user task |
| Manual start | Process instance start API |
| Jira or GitHub event trigger | Message start event, webhook connector, or Agentwerke inbound integration that starts a process |
| Agent output | Camunda process variables plus Agentwerke artifact store records |
| Evidence | Agentwerke artifact store and audit records, referenced by process variables |
| Policy block | Failed job, incident, boundary flow, or explicit user task depending on policy decision |
| Run monitoring | Agentwerke read model joined with Camunda process, job, and user task state |

## Alternatives Considered

### Continue building a custom C# BPMN runtime

Pros:

- Full control over the execution model.
- No additional engine deployment.
- Fast for simple demos.

Cons:

- High risk of building a BPMN-shaped subset rather than a trustworthy process engine.
- Durable waits, timers, retries, user tasks, recovery, incidents, and parallel semantics become Agentwerke-owned maintenance burden.
- Delays the real product work: agents, evidence, integrations, policy, and UI.

Rejected for production runtime.

### Use Flowable

Pros:

- Mature BPMN engine.
- Strong self-hosted and embeddable story.
- Good fit if the platform chooses a Java engine stack.

Cons:

- Camunda 8 has a better fit for the job-worker pattern Agentwerke needs for external agent execution.
- Agentwerke is a .NET application, so either option is external; Camunda 8's REST APIs and operational model fit the adapter boundary well.

Kept as a fallback if Camunda licensing or operational constraints block adoption.

### Use a general durable workflow engine instead of BPMN

Examples include Temporal, Durable Functions, or custom queue orchestration.

Pros:

- Excellent code-first durable execution.
- Strong developer ergonomics for some workflows.

Cons:

- Loses the classic BPMN process model the product is intentionally built around.
- Harder to offer a business-readable SDLC process builder and BPMN import/export.

Rejected for the core product direction.

## Consequences

- The next implementation plan should prioritize a Camunda adapter and job worker path.
- Agentwerke BPMN XML must be deployable to Camunda. Rich `agentwerke:*` metadata should either be translated to Zeebe task definitions and task headers or stored in Agentwerke's database keyed by BPMN element id.
- UI should present simple SDLC concepts, while generating Camunda-compatible BPMN underneath.
- The in-process runtime should be treated as legacy/test support and not expanded for production features.
- Manual testing and E2E testing must include a real Camunda-backed workflow run.

## References

- Camunda 8 service tasks: https://docs.camunda.io/docs/components/modeler/bpmn/service-tasks/
- Camunda 8 job workers: https://docs.camunda.io/docs/components/concepts/job-workers/
- Camunda 8 user tasks: https://docs.camunda.io/docs/components/modeler/bpmn/user-tasks/
- Camunda 8 REST API overview: https://docs.camunda.io/docs/apis-tools/orchestration-cluster-api-rest/orchestration-cluster-api-rest-overview/
- Existing spike: `docs/camunda8-spike.md`
