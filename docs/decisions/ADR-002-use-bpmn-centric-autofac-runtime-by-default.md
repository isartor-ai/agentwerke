# ADR-002: Use BPMN-Centric Autofac Runtime by Default

## Status
Accepted

## Date
2026-06-17

## Supersedes
[ADR-001: Use Camunda 8 as the Production BPMN Runtime](ADR-001-use-camunda8-for-production-bpmn-runtime.md)

## Context
Autofac's product strategy is to be straightforward to use, extensible at the SDLC/agent/tool layer, immediately usable by German companies, and capable of growing into a governed dark software factory.

Earlier planning selected Camunda 8 as the production BPMN runtime. That decision correctly identified the value of BPMN, service tasks, user tasks, timers, retries, and job workers. It also created a risk: Camunda-first execution adds a separate production runtime, licensing and operations questions, and a larger installation burden before Autofac has proven the core product value with real agents, enterprise auth, evidence, and template-first SDLC flows.

The key product insight is that company SDLC processes are configurable but usually stable. Autofac does not need to execute arbitrary BPMN models as a general-purpose process engine. It needs to execute a governed catalog of SDLC templates and customer-tuned variants.

## Decision
Autofac remains BPMN-centric, but Camunda 8 is no longer the default production runtime.

Autofac will use its bounded, Postgres-backed workflow runtime as the default runtime for MVP, pilot, and first self-hosted deployments. BPMN XML remains the product workflow artifact and governance language. The runtime supports only the BPMN subset required by Autofac's curated SDLC templates.

Camunda 8 remains an optional enterprise adapter. It should be used only when a customer already requires Camunda, already operates it, or when measured production requirements exceed the Autofac runtime's bounded scope.

## Runtime Scope
The default Autofac runtime is not a general BPMN engine. It supports the governed subset needed by the built-in SDLC template catalog:

- start and end events
- service tasks backed by Autofac agent/tool/connector handlers
- user tasks for approval gates
- exclusive gateways for simple branch selection
- parallel gateways for bounded fan-out/fan-in
- intermediate timer events and boundary timeout events
- persisted checkpoints, run events, retry metadata, and recovery through PostgreSQL/outbox infrastructure

Unsupported BPMN features must fail validation for default-runtime deployment instead of being partially interpreted.

## Product Implications
Autofac should optimize authoring around SDLC templates rather than a blank BPMN canvas.

Users should start from validated golden paths such as issue-to-PR, bugfix, hotfix, security review, and deployment approval. The BPMN designer remains available as an advanced governance and editing surface, but normal users configure agents, approvals, policies, repositories, environments, and evidence requirements through SDLC-first UI.

Extensibility belongs primarily in:

- agent profiles and skills
- tool gateway actions
- connectors and triggers
- policy rules
- evidence/artifact handlers
- optional runtime adapters

It should not require expanding the default runtime into a general BPMN engine.

## Camunda Adapter Policy
Camunda work is allowed as an optional adapter behind the workflow runtime boundary, with these constraints:

- It must not become the default runtime mode.
- It must not block work on real agents, auth/SSO, evidence, templates, and pilot readiness.
- Camunda-specific publish/start/job-worker/user-task behavior must be guarded by explicit runtime configuration.
- Camunda terminology should not leak into normal authoring UX.
- Optional adapter code should be tested as compatibility infrastructure, not as the main product path.

## Re-Decision Triggers
Revisit the default runtime decision only when one or more of these conditions is true:

- a signed customer contractually requires Camunda execution
- a target customer already operates Camunda and wants Autofac to attach workers to that estate
- measured pilot load exceeds what the Postgres-backed runtime can safely support
- required BPMN semantics exceed the supported Autofac subset and cannot be expressed as SDLC templates
- operating the default runtime becomes more expensive than adopting an external engine

## Alternatives Considered

### Camunda 8 as the default runtime

Pros:

- Strong BPMN/runtime credibility.
- Natural job-worker model for external agent execution.
- Mature user task, timer, retry, and incident concepts.

Cons:

- Adds a separate stateful runtime and operational surface.
- Requires explicit licensing and deployment planning for production self-managed use.
- Increases first-install complexity for German companies that need simple self-hosted data residency.
- Pulls engineering effort toward engine migration before real agents, auth, evidence, and templates are pilot-ready.

Rejected as the default, retained as an optional enterprise adapter.

### General-purpose custom BPMN engine

Pros:

- Full control and no external runtime.

Cons:

- Too broad for Autofac's strategy.
- Would force Autofac to own arbitrary BPMN semantics, compatibility, and edge cases.

Rejected. Autofac owns a bounded SDLC runtime, not a general BPMN engine.

### Embedded third-party workflow engine

Pros:

- Could reduce runtime maintenance.
- May fit a single-process deployment model better than Camunda 8.

Cons:

- No current option clearly beats the existing Autofac runtime for .NET, PostgreSQL, BPMN artifact continuity, and template-first strategy.
- Migration would still distract from pilot-critical product work.

Deferred. Keep the runtime boundary clean enough to evaluate later.

## Consequences

- Architecture docs and issue plans must stop describing Camunda 8 as the production default.
- Existing Camunda PRs should be reframed as optional adapter groundwork or closed if they force the default path.
- The next implementation plan should prioritize runtime-mode clarity, template-first authoring, default-runtime conformance tests, real agents, auth/SSO, and evidence packs.
- The Camunda issue chain should be marked deferred unless a re-decision trigger fires.
- Manual testing should prove the default Autofac/Postgres runtime first; Camunda manual testing is optional adapter validation.

## References

- Camunda 8 licensing: https://docs.camunda.io/docs/reference/licenses/
- Camunda 8 job workers: https://docs.camunda.io/docs/components/concepts/job-workers/
- Camunda 7 EOL context: https://camunda.com/blog/2025/02/camunda-7-enterprise-end-of-life-extension/
- Existing spike: `docs/camunda8-spike.md`
