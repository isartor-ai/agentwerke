# Agentwerke MVP Implementation Plan

Version: Draft v0.1
Status: Working Draft
Related Documents:
- `docs/functional-specification.md`
- `docs/architecture-design.md`

## 1. Purpose

This document defines the recommended MVP implementation plan for Agentwerke based on:
- the current Functional Specification
- the current Architecture Design
- the current repository baseline

The goal is to move from the current scaffold to a usable first product release with the smallest coherent slice of Agentwerke's value:

- design a BPMN workflow
- publish it
- trigger a run
- pause at approval gates
- inspect run state and logs
- perform one real SDLC automation flow end-to-end

## 2. MVP Outcome

At MVP completion, a user should be able to:

1. Create or import a BPMN workflow in the React UI.
2. Configure Agentwerke task metadata for agent tasks and approval tasks.
3. Publish the workflow to the backend.
4. Trigger the workflow manually or from Jira webhook input.
5. Execute the workflow through the Agentwerke runtime.
6. Pause on human approval and resume after decision.
7. Run one agent-driven engineering flow:
   - generate technical specification
   - assign implementation work
   - create a GitHub pull request
   - run tester validation
   - trigger deployment workflow
8. Monitor workflow runs in the dashboard with steps, events, and approval state.
9. Store artifacts and maintain audit-friendly execution history.

## 3. MVP Scope

### In Scope

- React workflow designer
- BPMN import, validation, metadata editing, publish
- C# API for workflows, runs, approvals
- Camunda 8-backed workflow execution through the workflow engine adapter boundary
- In-process runtime retained only for tests, simulation, or temporary compatibility
- Manual trigger plus Jira webhook trigger
- GitHub integration for PR creation
- Approval workflow and resume handling
- Run monitoring dashboard
- Artifact storage
- Docker-based sandbox execution stub or first functional version
- Basic RBAC scaffolding
- Logging, audit events, and run telemetry

### Explicitly Deferred

- Slack and Teams interactive triggers
- full plugin SDK
- full multi-tenant identity and enterprise SSO
- Kubernetes production deployment automation
- advanced self-improving agents
- broad marketplace-style integrations
- production-grade policy engine breadth

## 4. Current Codebase Assessment

The repository already gives us a useful starting point.

### What Already Exists

- API host in `src/Agentwerke.Api`
- workflow validation and runtime scaffolding in `src/Agentwerke.Workflows`
- EF Core persistence in `src/Agentwerke.Infrastructure`
- workflow, run, approval, and event entities in `src/Agentwerke.Domain`
- artifact storage abstraction in `src/Agentwerke.Storage`
- React shell and key UI views in `web/`
- workflow designer, run board, run detail, and approvals dashboard
- tests for workflow runtime and frontend integration flows

### What Is Still Mostly Scaffold

- `src/Agentwerke.Agents`
- `src/Agentwerke.Integrations`
- `src/Agentwerke.Sandboxes`
- `src/Agentwerke.AgentSecOps`
- `src/Agentwerke.Observability`
- `src/Agentwerke.Application`

### Key Gaps Between Product Vision and Current Implementation

- no real BPMN execution engine integration yet
- no true workflow-to-agent execution path
- no persisted workflow metadata model for agent configuration beyond BPMN XML
- no real approval-to-resume API flow joined to runtime
- no actual Jira or GitHub connector implementation
- no real sandbox lifecycle implementation
- no event bus or background processing model
- no real authentication / RBAC enforcement
- frontend still uses substantial mocked API behavior

## 5. Product Strategy for MVP

The right MVP is not "build all platform modules lightly."
The right MVP is "finish one trustworthy SDLC flow end-to-end."

That MVP flow should be:

1. Jira requirement created
2. Agentwerke workflow triggered
3. analysis agent generates technical specification
4. human approves specification
5. implementation agent prepares code change and PR
6. human reviews PR
7. tester agent runs verification
8. DevOps step is triggered as a controlled deployment action
9. operator monitors the full run in Agentwerke

This gives Agentwerke a strong narrative and a meaningful proof of product value.

## 6. Delivery Principles

- Finish vertical slices, not disconnected modules.
- Keep workflow execution observable from day one.
- Avoid building a full plugin framework before the first real connectors work.
- Keep Camunda-specific concerns behind an Agentwerke workflow adapter boundary.
- Convert mocked frontend flows to real APIs incrementally, one screen at a time.
- Use Docker sandboxing in a constrained but real form rather than a purely conceptual abstraction.

## 7. MVP Workstreams

Implementation should run through six coordinated workstreams:

1. Workflow and BPMN platform
2. Agent execution and sandboxing
3. Integrations and triggers
4. Approvals, policy, and audit
5. Frontend product surfaces
6. DevEx, testing, and deployment readiness

## 8. Step-by-Step Implementation Plan

## Phase 0: Baseline Stabilization

Objective: make the current scaffold trustworthy before adding major features.

### Steps

1. Normalize the persistence model and documentation.
   - Align `docs/persistence-schema.md` with the actual EF Core schema and entity model.
   - Decide which schema is authoritative for MVP.

2. Remove frontend/backend contract drift.
   - Compare `web/src/types`, API contracts, controllers, and mocked client responses.
   - Make the API contracts authoritative.

3. Establish module ownership.
   - `Agentwerke.Api` for transport
   - `Agentwerke.Application` for orchestration use cases
   - `Agentwerke.Domain` for core entities and rules
   - `Agentwerke.Infrastructure` for persistence and adapters
   - `Agentwerke.Workflows` for workflow runtime abstraction
   - `Agentwerke.Agents` for agent execution
   - `Agentwerke.Integrations` for connectors
   - `Agentwerke.Sandboxes` for sandbox runtime
   - `Agentwerke.AgentSecOps` for policy and approvals

4. Add missing solution-level test projects where needed.
   - application tests
   - integration tests
   - connector tests

### Exit Criteria

- consistent API and data model
- consistent docs
- green build and test baseline

## Phase 1: Workflow Definition Vertical Slice

Objective: make workflow definition and publishing real end-to-end.

### Steps

1. Finalize the workflow aggregate.
   - enrich `WorkflowDefinition`
   - add workflow key/versioning strategy
   - persist metadata needed for publish lifecycle

2. Introduce application services for workflow authoring.
   - import workflow
   - validate workflow
   - publish workflow
   - list workflow versions

3. Strengthen BPMN validation.
   - validate required Agentwerke extension metadata
   - validate supported node types
   - validate approval tasks and agent tasks

4. Replace mocked workflow designer save/publish behavior with real APIs.
   - real import
   - real validate
   - real publish
   - real list/detail

5. Keep the React designer template-first.
   - continue with current template gallery
   - store actual BPMN XML and metadata via backend

### Repo Areas

- `src/Agentwerke.Api/Controllers/WorkflowsController.cs`
- `src/Agentwerke.Workflows/Bpmn/*`
- `src/Agentwerke.Application`
- `web/src/views/WorkflowDesigner.tsx`
- `web/src/api/client.ts`

### Exit Criteria

- user can import, validate, and publish workflow definitions
- published workflow is persisted and visible across reloads
- validation errors come from backend, not local-only UI logic

## Phase 2: Workflow Run and Approval Vertical Slice

Objective: make workflow execution and approval pause/resume real.

### Steps

1. Promote the current workflow runtime from demo state to MVP state.
   - persist checkpoints intentionally
   - make run recovery deterministic
   - make user-task approval pause/resume a first-class flow

2. Introduce application-level run orchestration service.
   - start workflow run
   - resume workflow run
   - recover workflow run
   - emit structured run events

3. Extend approvals domain and runtime integration.
   - create approval requests automatically from user tasks
   - tie approval decisions to run resumption
   - update run status and pending approval counts

4. Add backend endpoints for:
   - run resume
   - run events
   - approval detail
   - approval decision with runtime continuation

5. Replace mocked run and approval data in the frontend incrementally.
   - run board uses live runs
   - run detail uses live step and event data
   - approvals dashboard uses live approvals and decisions

### Repo Areas

- `src/Agentwerke.Workflows/Runtime/*`
- `src/Agentwerke.Api/Controllers/RunsController.cs`
- `src/Agentwerke.Api/Controllers/ApprovalsController.cs`
- `src/Agentwerke.Infrastructure/Persistence/*`
- `web/src/views/RunBoard.tsx`
- `web/src/views/RunDetail.tsx`
- `web/src/views/ApprovalsDashboard.tsx`

### Exit Criteria

- a published workflow can be started from the API
- a user task pauses execution and creates approval record
- approval decision resumes execution
- run timeline and events reflect real transitions

## Phase 3: Agent Execution Framework

Objective: make Agentwerke agent tasks execute through a controlled runtime.

### Steps

1. Define the agent execution contract in `Agentwerke.Agents`.
   - agent definition
   - skill manifest reference
   - tool permission set
   - execution request
   - execution result

2. Build the first `Agent Orchestrator` service.
   - map BPMN agent task to an Agentwerke agent
   - build task context
   - invoke LLM-backed execution adapter
   - emit logs and artifacts

3. Implement skill loading from Markdown files.
   - define directory convention
   - define parsing and versioning rules
   - bind skill references to agent profiles

4. Implement tool invocation abstraction.
   - start with controlled tool types only
   - GitHub action adapter
   - file write adapter
   - shell command adapter with guardrails

5. Update runtime engine to execute service tasks through the Agent Orchestrator instead of placeholder behavior.

### Repo Areas

- `src/Agentwerke.Agents`
- `src/Agentwerke.Application`
- `src/Agentwerke.Workflows/Runtime`
- `src/Agentwerke.AgentSecOps`

### Exit Criteria

- service task execution calls Agentwerke agent runtime
- task output and result are persisted
- run events show agent execution lifecycle

## Phase 4: Sandbox and Safe Execution

Objective: make agent execution safe enough for MVP.

### Steps

1. Implement the first sandbox manager in `Agentwerke.Sandboxes`.
   - create sandbox request model
   - define local Docker execution profile
   - mount workspace and artifact paths safely

2. Add runtime controls.
   - command allowlist
   - environment variable shaping
   - network policy mode for MVP
   - timeout and resource limits

3. Integrate sandbox lifecycle with agent execution.
   - provision sandbox
   - execute task
   - collect outputs and logs
   - teardown sandbox

4. Persist sandbox execution records as events or artifacts.

### Exit Criteria

- at least one agent task can run inside Docker sandbox
- logs and outputs are captured
- failures and timeouts are visible in run detail

## Phase 5: First Real Connectors

Objective: deliver the first meaningful external workflow.

### Step 5A: Jira Inbound Trigger

1. Implement webhook endpoint in `Agentwerke.Integrations`.
2. Validate Jira event payload.
3. Map payload to workflow trigger input.
4. Start workflow run from inbound event.

### Step 5B: GitHub Outbound Actions

1. Implement GitHub connector contract.
2. Add repository configuration and secrets handling.
3. Support MVP operations:
   - create branch
   - create pull request
   - post PR comment or status

### Step 5C: CI/CD Trigger Stub

1. Implement one minimal deployment connector.
2. Trigger external pipeline or deployment webhook.
3. Record deployment action event and result.

### Exit Criteria

- Jira webhook can start a workflow
- workflow can create a GitHub PR
- workflow can trigger one deployment-style external action

## Phase 6: Policy, Approval, and Audit Hardening

Objective: add the minimum governance Agentwerke needs to be trusted.

### Steps

1. Build policy evaluation service in `Agentwerke.AgentSecOps`.
   - evaluate risky tool actions
   - decide allow, escalate, or reject

2. Add approval requirement mapping.
   - PR creation review
   - PR merge
   - deployment action
   - secret access

3. Add audit event model.
   - who initiated action
   - what was attempted
   - what policy decided
   - who approved or rejected

4. Add basic RBAC enforcement.
   - workflow designer role
   - approver role
   - operator role
   - admin role

### Exit Criteria

- sensitive actions cannot proceed without policy evaluation
- approval trail is persisted
- core UI screens respect role boundaries

## Phase 7: Monitoring and Operational UX

Objective: make Agentwerke usable as an operations product, not just an API demo.

### Steps

1. Finish the run dashboard against live data.
2. Add run detail drill-down for:
   - steps
   - events
   - approvals
   - artifacts
   - agent outputs

3. Add workflow status surfaces.
   - draft
   - active
   - archived
   - validation state

4. Add approval inbox productivity improvements.
   - filters
   - SLA status
   - decision history

5. Add operational telemetry hooks in `Agentwerke.Observability`.
   - structured logs
   - traces
   - metrics counters and histograms

### Exit Criteria

- operators can understand what happened in a run without database access
- approvals and failures are easy to inspect
- workflow execution telemetry is visible in app and logs

## Phase 8: Camunda 8 Production Runtime

Objective: make Camunda 8 the production BPMN runtime rather than growing the local runtime.

### Recommended Approach

Use Camunda 8 from the beginning of the next execution phase. The in-process runtime should not receive new production features. It may remain available for tests, local simulation, or temporary compatibility while the Camunda adapter reaches parity.

### Steps

1. Add a Camunda 8 local runtime profile.
2. Add Camunda configuration and REST client infrastructure.
3. Project Agentwerke task metadata to Camunda-compatible BPMN.
4. Deploy workflows to Camunda during publish.
5. Start Camunda process instances from Agentwerke run APIs.
6. Execute `autofac.agent` service tasks with Agentwerke job workers.
7. Bridge Camunda user tasks to Agentwerke approval requests.
8. Surface retries, incidents, evidence, and artifacts in Agentwerke run views.

### Exit Criteria

- Camunda 8 is the default production workflow runtime
- agent service tasks execute through Agentwerke workers
- Camunda user tasks create and resolve Agentwerke approvals
- operators can inspect Camunda-backed run state, evidence, failures, and approvals in Agentwerke

## 9. Recommended Delivery Order

This is the exact order I would implement:

1. Stabilize contracts and persistence
2. Add Camunda runtime foundation
3. Make workflow authoring publish to Camunda
4. Make Camunda-backed run and approval flow real
5. Add agent job workers and orchestration
6. Add evidence, artifacts, retry, and incident visibility
7. Add sandbox execution
8. Add Jira trigger and GitHub PR connector
9. Add policy and audit hardening
10. Finish monitoring UX

## 10. Suggested MVP Milestones

### Milestone 1: Design and Publish

User can create, validate, and publish workflow definitions.

### Milestone 2: Run and Approve

User can start a workflow, see it pause for approval, approve it, and resume it.

### Milestone 3: Agent Task Execution

Workflow service tasks run through Agentwerke agent execution and persist outputs.

### Milestone 4: Real SDLC Flow

Jira-triggered workflow creates technical spec, creates PR, pauses for human review, and records results.

### Milestone 5: MVP Hardening

Observability, audit, policy controls, and deployment readiness are in place.

## 11. Team Plan

For a focused MVP, the ideal team is:

- 1 product owner / solution lead
- 1 senior backend engineer
- 1 senior frontend engineer
- 1 full-stack engineer
- 1 platform or DevOps engineer part-time
- 1 QA or automation engineer part-time

If the team is smaller, prioritize in this order:
- backend workflow/runtime
- frontend workflow + run visibility
- GitHub and Jira integration
- sandboxing
- policy hardening

## 12. Definition of Done for MVP

Agentwerke MVP is done when all of the following are true:

- a workflow can be authored and published in the UI
- a workflow can be triggered manually and from Jira
- workflow execution supports service tasks and approval tasks
- at least one agent task runs through Agentwerke runtime
- human approval gates block and resume execution correctly
- GitHub PR creation works from workflow execution
- run monitoring is live and usable
- artifacts and audit trails are stored
- core flows are covered by automated tests
- the system can run locally and in a basic containerized environment

## 13. Risks

- If we overbuild the engine abstraction too early, we will slow MVP significantly.
- If we keep too much mocked frontend behavior for too long, integration risk will pile up late.
- If we attempt too many connectors before one real vertical slice works, the product will look broad but weak.
- If sandboxing is deferred too far, we risk building an unsafe execution model that must later be reworked.

## 14. Recommendation

The right MVP implementation strategy is:

- finish one real SDLC automation journey
- keep the platform architecture modular
- postpone breadth until the first execution loop is trustworthy

From a product and engineering perspective, Agentwerke should win first by being credible, observable, and governable in one concrete workflow before it tries to be a general automation universe.
