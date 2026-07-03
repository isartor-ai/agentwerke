# Agentwerke MVP GitHub Issue Drafts

Target repository: `isartor-ai/agentwerke-private`
Prepared from:
- `docs/functional-specification.md`
- `docs/architecture-design.md`
- `docs/mvp-implementation-plan.md`

## Issue 1: Align persistence model, API contracts, and repo docs

### Summary

Stabilize the current baseline so persistence schema, API contracts, frontend types, and documentation all describe the same MVP data model.

### Why

The current repo has useful scaffolding, but there is visible drift between:
- EF Core entities and migrations
- documented persistence schema
- API contract DTOs
- frontend mock types and responses

This drift will slow all later MVP work if not corrected first.

### Scope

- Align `docs/persistence-schema.md` with actual EF Core schema or update schema implementation
- Review `WorkflowDefinition`, `WorkflowRun`, `ApprovalRequest`, `WorkflowEvent`, and related DTOs
- Make API contracts the source of truth for frontend/backend communication
- Remove or document known mismatches

### Acceptance Criteria

- persistence schema and docs match
- API contracts and frontend types match
- no known contract drift remains for workflows, runs, approvals, and events
- solution builds and tests cleanly after refactor

## Issue 2: Implement workflow authoring application service and real publish flow

### Summary

Replace the current controller-level workflow handling with application services that support import, validate, publish, and version management for workflow definitions.

### Why

The workflow designer is one of the core MVP surfaces, but backend behavior is still thin and partially controller-driven.

### Scope

- add workflow authoring use cases in `Agentwerke.Application`
- move import, validate, and publish logic out of controllers
- define workflow versioning rules for MVP
- persist workflow metadata consistently

### Acceptance Criteria

- user can import BPMN definition
- backend validates BPMN and Agentwerke metadata
- publish updates workflow state and version
- workflow list and detail endpoints return persisted data

## Issue 3: Extend BPMN validation for Agentwerke custom nodes

### Summary

Enhance BPMN validation so Agentwerke-specific metadata is enforced at publish time, not just raw XML structure.

### Why

Agentwerke workflows depend on agent tasks, approval tasks, and policy metadata. MVP needs strong design-time validation.

### Scope

- validate supported BPMN node types
- validate `agentwerke:agentTask` metadata
- validate `agentwerke:approvalTask` metadata
- return actionable validation errors and warnings

### Acceptance Criteria

- missing required Agentwerke metadata blocks publish
- validation responses include element references and clear messages
- automated tests cover valid and invalid BPMN examples

## Issue 4: Replace mocked workflow designer API behavior with live backend integration

### Summary

Connect the React workflow designer to real workflow APIs for import, validation, list/detail, and publish.

### Why

The current UI is strong enough to demonstrate the product direction, but too much behavior still depends on mocked client data.

### Scope

- remove workflow mocks from `web/src/api/client.ts` where real APIs exist
- use real workflows list and detail APIs
- wire validate and publish actions to backend
- preserve existing template-first designer UX

### Acceptance Criteria

- workflow designer loads real persisted workflows
- validate and publish use live API responses
- reload preserves authored workflows
- frontend tests are updated to reflect live API contracts

## Issue 5: Implement workflow run orchestration service with pause/resume support

### Summary

Introduce application-level workflow run orchestration so a workflow can start, pause on approval, resume, and recover consistently.

### Why

This is the core execution loop for MVP.

### Scope

- add run orchestration service in `Agentwerke.Application`
- formalize start, resume, and recover use cases
- persist checkpoints and state transitions intentionally
- expose run events cleanly for UI consumption

### Acceptance Criteria

- published workflow can be started
- workflow with approval task pauses execution
- approval decision can resume the run
- recovery path works for waiting and completed states

## Issue 6: Create approval-request generation and runtime continuation flow

### Summary

Connect approval records directly to workflow runtime behavior so approval tasks generate approval items and decisions continue execution.

### Why

Human-in-the-loop approval is central to the Agentwerke MVP.

### Scope

- create approval request when user task is reached
- maintain pending approval counts on runs
- implement approval decision handling
- continue workflow after approval
- record decision metadata and audit details

### Acceptance Criteria

- approval requests are created automatically from workflow execution
- deciding approval updates approval status and run state
- run resumes after approval when allowed
- approval dashboard reflects real data

## Issue 7: Implement first agent execution contract and orchestrator

### Summary

Build the first real Agentwerke agent execution layer for BPMN service tasks.

### Why

Without this, workflows cannot do meaningful SDLC automation.

### Scope

- define agent execution contract
- add agent profile and skill reference model
- implement orchestrator for service-task execution
- persist agent outputs and status events

### Acceptance Criteria

- BPMN service tasks invoke Agentwerke agent execution
- agent output is stored and visible in run details
- task failures and completions are reflected in run state

## Issue 8: Add Markdown-based skill loading for agents

### Summary

Support agent skills defined by Markdown files and bind them into the execution model.

### Why

The product concept explicitly depends on configurable LLM skills.

### Scope

- define skill directory and manifest convention
- implement Markdown skill loader
- bind skill references to agent definitions
- version or fingerprint loaded skills for traceability

### Acceptance Criteria

- an agent can be configured with a Markdown skill
- skill content participates in execution context
- skill identity is logged or recorded for audit/debugging

## Issue 9: Implement Docker sandbox manager for MVP agent tasks

### Summary

Create the first Docker-based sandbox execution path for agent tasks.

### Why

Sandboxed execution is part of the MVP trust model and architecture direction.

### Scope

- define sandbox execution contract
- provision Docker-based runtime
- enforce initial resource/time limits
- collect logs and artifacts from sandbox runs

### Acceptance Criteria

- at least one agent task runs in Docker sandbox
- logs and artifacts are captured
- timeout and failure paths are visible in run detail

## Issue 10: Implement Jira webhook trigger for MVP workflow start

### Summary

Add the first real inbound integration: Jira requirement event to workflow run.

### Why

This is the chosen MVP business entry point.

### Scope

- add Jira webhook endpoint
- validate and map Jira payload
- connect trigger to workflow start
- record trigger event in run history

### Acceptance Criteria

- valid Jira webhook can start configured workflow
- invalid payload returns useful error
- triggered run is visible in dashboard with source metadata

## Issue 11: Implement GitHub connector for branch and pull request creation

### Summary

Add the first real outbound engineering connector for implementation flow.

### Why

Creating a PR is one of the defining MVP outcomes.

### Scope

- add GitHub connector contract
- configure repo and credential settings
- implement create branch and create PR actions
- record external action events

### Acceptance Criteria

- workflow can create GitHub branch and PR through connector
- action results are stored in run events or artifacts
- failure and retry behavior is visible

## Issue 12: Add first policy evaluation service for sensitive tool actions

### Summary

Create the MVP policy layer for risky actions such as PR merge, deployment, and secret access.

### Why

Agentwerke must be governable, not just automated.

### Scope

- implement policy evaluation service in `Agentwerke.AgentSecOps`
- support allow, escalate, reject decisions
- connect policy checks to tool invocation path
- persist policy decision events

### Acceptance Criteria

- risky tool actions are policy-evaluated before execution
- policy decisions are visible in run and approval detail
- blocked actions do not proceed without approval

## Issue 13: Replace run and approval dashboard mocks with live API data

### Summary

Finish the operational UX by moving the dashboard and approval views to real backend data.

### Why

MVP needs real monitoring, not static demos.

### Scope

- connect run board to live runs and events
- connect run detail to persisted steps, events, and artifacts
- connect approvals dashboard to real approvals and decisions
- improve filtering and error handling

### Acceptance Criteria

- dashboard surfaces real workflow runs
- run detail shows live persisted execution data
- approval inbox shows live queue and decision updates

## Issue 14: Add structured observability and audit trail for workflow execution

### Summary

Implement the minimum logging, tracing, metrics, and audit model needed to operate and debug Agentwerke.

### Why

Observability is one of the product’s core promises.

### Scope

- structured logs for workflow and agent events
- trace correlation IDs across run lifecycle
- metrics for run counts, durations, approvals, failures
- audit records for user and agent actions

### Acceptance Criteria

- workflow execution emits structured logs
- run events and audit evidence are correlated
- basic metrics are available for MVP flows

## Issue 15: Introduce workflow engine adapter seam and Camunda 8 spike

### Summary

Prepare the runtime architecture for Camunda 8 without blocking MVP delivery.

### Why

The architecture design selects Camunda 8 as the strategic BPMN engine, but MVP should not stall on a full engine migration.

### Scope

- define `IWorkflowEngineAdapter`
- move current runtime behind adapter boundary
- create Camunda 8 spike for service task, user task, and trigger mapping
- document integration findings

### Acceptance Criteria

- current runtime is hidden behind adapter abstraction
- Camunda 8 spike proves viability for key MVP task types
- no major code path depends directly on a single engine implementation

## Recommended Creation Order

1. Issue 1
2. Issue 2
3. Issue 3
4. Issue 4
5. Issue 5
6. Issue 6
7. Issue 7
8. Issue 8
9. Issue 9
10. Issue 10
11. Issue 11
12. Issue 12
13. Issue 13
14. Issue 14
15. Issue 15

## GitHub Publish Note

These issue drafts were prepared locally because GitHub write access could not be completed from the current environment.

Observed blockers:
- local checkout remote is `isartor-ai/agentwerke`
- requested issue target is `isartor-ai/agentwerke-private`
- local `gh` authentication is currently invalid for `github.com`
