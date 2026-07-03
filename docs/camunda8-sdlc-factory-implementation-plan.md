# Camunda 8 SDLC Factory Implementation Plan

Version: Draft v0.1
Status: Ready for issue breakdown
Date: 2026-06-16
Decision: `docs/decisions/ADR-001-use-camunda8-for-production-bpmn-runtime.md`

## Objective
Move Agentwerke from a local BPMN subset runtime to a real Camunda 8-backed dark software factory execution path.

The target user journey is:

1. User selects an SDLC workflow template.
2. User configures agent tasks, approval gates, evidence requirements, and policy tags.
3. Agentwerke generates and validates Camunda-compatible BPMN.
4. User publishes the workflow to Camunda.
5. User starts a run manually or from an inbound event.
6. Camunda schedules agent service-task jobs.
7. Agentwerke workers execute jobs through policy, sandbox, tools, and evidence capture.
8. Camunda pauses at user tasks.
9. Agentwerke approval UI completes the user task.
10. User monitors the full run, artifacts, decisions, and exceptions.

## Architecture Rules

- Camunda 8 is the production workflow engine.
- The current in-process runtime is not expanded except for tests or temporary compatibility.
- Agentwerke owns SDLC semantics, agent execution, evidence, policy, artifacts, and UI.
- BPMN shown to users should be template-first and SDLC-oriented, not raw-engine-first.
- Camunda-specific details stay behind `IWorkflowEngineAdapter` and infrastructure adapters.

## Task List

### Phase 1: Runtime Foundation

#### Task 1: Add Camunda 8 local runtime profile

Description: Add a local Docker Compose profile for Camunda 8 and document how developers start and verify it.

Acceptance criteria:

- Docker Compose can start Camunda services for local development.
- Health or topology check is documented.
- Existing non-Camunda manual stack still works.

Verification:

- `docker compose` starts the Camunda profile.
- Camunda topology or health endpoint responds.
- Existing API health endpoint still responds.

Dependencies: None

Estimated scope: Medium

#### Task 2: Add Camunda configuration and HTTP client abstraction

Description: Add typed options and an HTTP client wrapper for Camunda REST calls.

Acceptance criteria:

- API reads Camunda base URL and auth settings from configuration.
- Infrastructure has a small Camunda client abstraction.
- Unit tests cover option binding and request URI construction.

Verification:

- `dotnet test` for infrastructure/configuration tests.
- Manual call can reach Camunda topology or equivalent health endpoint.

Dependencies: Task 1

Estimated scope: Small

#### Task 3: Implement Camunda-compatible BPMN projection

Description: Convert Agentwerke agent and approval metadata into Camunda-deployable BPMN.

Acceptance criteria:

- Agent tasks become `bpmn:serviceTask` elements with `zeebe:taskDefinition`.
- Static Agentwerke metadata needed by workers is available as Zeebe task headers or persisted metadata keyed by BPMN element id.
- User approval tasks remain BPMN user tasks.
- Invalid or unsupported metadata returns actionable validation errors.

Verification:

- Unit tests cover agent task, user task, missing metadata, and unsupported element projection.
- A projected BPMN file deploys to local Camunda.

Dependencies: Task 2

Estimated scope: Medium

#### Task 4: Implement Camunda workflow deployment adapter

Description: Replace publish-time production execution registration with Camunda deployment through `IWorkflowEngineAdapter`.

Acceptance criteria:

- Publishing a workflow deploys Camunda-compatible BPMN.
- Agentwerke stores Camunda deployment/process identifiers.
- Publish failure returns actionable API errors and does not mark the workflow active.

Verification:

- Integration test deploys a minimal workflow to Camunda.
- API publish endpoint surfaces deployment errors cleanly.

Dependencies: Task 3

Estimated scope: Medium

#### Task 5: Implement Camunda process start adapter

Description: Start Camunda process instances from Agentwerke run APIs and map Camunda process instance keys to Agentwerke run records.

Acceptance criteria:

- `POST /api/runs` starts a Camunda process instance for a published workflow.
- Agentwerke run record stores Camunda process instance key and correlation id.
- Initial process variables include initiator and workflow input context.

Verification:

- Integration test starts a published process and stores the mapping.
- Run detail can show the Camunda-backed run id linkage.

Dependencies: Task 4

Estimated scope: Medium

### Phase 2: Agent Jobs and Approval Gates

#### Task 6: Implement Agentwerke agent job worker

Description: Add a background worker that activates Camunda jobs for the Agentwerke agent task type.

Acceptance criteria:

- Worker subscribes to `agentwerke.agent` jobs.
- Worker resolves BPMN element metadata and process variables.
- Worker invokes the existing Agent Orchestrator contract.
- Worker records start, completion, and failure events in Agentwerke.

Verification:

- Integration test executes a service task job with a no-op agent executor.
- Run events show job activation and completion.

Dependencies: Task 5

Estimated scope: Medium

#### Task 7: Complete and fail Camunda jobs from agent outcomes

Description: Map agent execution results to Camunda job complete, fail, and incident behavior.

Acceptance criteria:

- Successful agent output completes the Camunda job with output variables.
- Failed agent execution decrements retries or creates an incident.
- Policy rejection is visible as blocked, failed, or approval-required according to policy result.

Verification:

- Tests cover success, retryable failure, exhausted retries, and policy rejection.
- Camunda state and Agentwerke run events agree.

Dependencies: Task 6

Estimated scope: Medium

#### Task 8: Implement Camunda user task approval bridge

Description: Sync Camunda user tasks into Agentwerke approval requests and complete user tasks from approval decisions.

Acceptance criteria:

- When Camunda reaches a user task, Agentwerke creates a pending approval.
- Approval decision completes or rejects the Camunda user task.
- Approval metadata and comments are recorded in Agentwerke audit records.

Verification:

- Integration test reaches a user task, approves it, and process continues.
- Rejection path is documented and visible in run detail.

Dependencies: Task 5

Estimated scope: Medium

#### Task 9: Persist evidence and artifacts from Camunda-backed runs

Description: Store agent evidence and artifact references in Agentwerke while passing only durable references through Camunda variables.

Acceptance criteria:

- Agent outputs can create artifact records.
- Camunda variables contain stable artifact ids or URLs, not large blobs.
- Run detail can display evidence required versus evidence produced.

Verification:

- Test agent output creates an artifact reference.
- Run detail API includes artifact/evidence references.

Dependencies: Task 6

Estimated scope: Medium

### Phase 3: Product Flow

#### Task 10: Add manual start and trigger input mapping

Description: Normalize manual run input and inbound event payloads into process variables.

Acceptance criteria:

- Manual start supports title, body, external URL, and arbitrary typed input fields.
- Trigger metadata is written to Agentwerke run context and Camunda variables.
- Invalid input returns structured validation errors.

Verification:

- API contract tests cover valid and invalid start requests.
- Manual test can start a run with input visible to the first agent job.

Dependencies: Task 5

Estimated scope: Small

#### Task 11: Add first SDLC factory template for Camunda

Description: Add a Camunda-compatible template for issue-to-PR delivery.

Acceptance criteria:

- Template includes intake, spec agent, approval, implementation agent, test agent, PR creation, and final approval.
- Template deploys to local Camunda.
- Template metadata uses the supported Agentwerke agent task contract.

Verification:

- Template validates, publishes, starts, and reaches the first agent job.
- Tests or fixtures cover the BPMN projection.

Dependencies: Tasks 3, 4, 6, 8

Estimated scope: Medium

#### Task 12: Replace production registration of the in-process runtime

Description: Make Camunda the default production workflow adapter and demote the in-process runtime to test or explicit dev mode.

Acceptance criteria:

- Production configuration uses the Camunda adapter.
- In-process runtime is opt-in and documented as non-production.
- Tests are updated to target the right runtime layer.

Verification:

- `dotnet test` passes.
- Local manual Camunda scenario uses the Camunda adapter by default.

Dependencies: Tasks 4, 5, 6, 8

Estimated scope: Medium

### Phase 4: Verification and Hardening

#### Task 13: Add Camunda-backed end-to-end test

Description: Add an E2E test that deploys a workflow, starts it, completes an agent job, pauses for approval, approves, and completes.

Acceptance criteria:

- Test is gated behind a Camunda-enabled environment variable or compose profile.
- Test proves service task and user task mappings.
- Failure output includes Camunda response details.

Verification:

- E2E test passes with the Camunda profile.
- Test is skipped cleanly without Camunda.

Dependencies: Tasks 6, 8, 12

Estimated scope: Medium

#### Task 14: Add operator-visible incident and retry state

Description: Surface Camunda job failures, incidents, and retry exhaustion in Agentwerke run detail.

Acceptance criteria:

- Run detail API exposes retry count, failed task, incident reason, and recommended action.
- UI shows blocked/incident state clearly.
- Operator can distinguish agent failure, policy block, and engine incident.

Verification:

- Tests cover failed job and incident mapping.
- Manual scenario includes a retry failure variant.

Dependencies: Task 7

Estimated scope: Medium

## Checkpoints

### Checkpoint A: Engine Foundation

After Tasks 1-5:

- Camunda starts locally.
- Agentwerke can deploy and start a process instance.
- Camunda identifiers are stored in Agentwerke records.

### Checkpoint B: Execution Loop

After Tasks 6-9:

- Camunda service tasks execute through Agentwerke workers.
- User tasks produce Agentwerke approvals.
- Evidence and artifacts are visible.

### Checkpoint C: Factory Slice

After Tasks 10-13:

- A user can run the first Camunda-backed SDLC flow end-to-end.
- The in-process runtime is no longer the production path.

## Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Camunda-specific details leak into UI | Users face engine complexity | Keep an SDLC process builder and hide engine fields behind presets |
| Rich `agentwerke:*` XML is rejected by Camunda | Publish fails | Project to Zeebe task definitions and store rich metadata in Agentwerke DB |
| Worker failures leave confusing run state | Operators lose trust | Map failures, retries, and incidents into Agentwerke run detail |
| C# SDK maturity changes | Integration instability | Use Camunda REST API first behind an Agentwerke client abstraction |
| Scope expands into general workflow product | MVP slips | Ship one issue-to-PR factory line before broad templates |

## Definition of Done

- Camunda 8 is the default production runtime.
- A published workflow deploys to Camunda.
- Agent tasks execute through Agentwerke workers.
- User tasks create and resolve Agentwerke approval requests.
- Evidence and artifacts are persisted outside Camunda and referenced from run variables.
- A manual test scenario proves the first dark software factory flow.
