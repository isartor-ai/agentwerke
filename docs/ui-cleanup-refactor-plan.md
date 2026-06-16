# UI Cleanup and Refactor Plan

Version: Draft v0.1
Status: Ready for issue breakdown
Date: 2026-06-16

## Objective
Refocus the UI from a BPMN technical editor into a straightforward SDLC factory builder and operator console.

The product should feel like a dense, practical control room for software delivery:

- template-first SDLC process authoring
- simple agent assignment
- explicit approval and policy configuration
- live run visibility
- evidence, artifacts, incidents, and approval decisions in one place

BPMN remains available underneath, but users should not need to understand Camunda job types, Zeebe headers, XML namespaces, or process instance keys.

## Current UI Problems to Fix

- Workflow Designer is doing too much in one screen.
- Some frontend flows still rely on mocked data or contract drift.
- BPMN concepts are more visible than SDLC concepts.
- Run monitoring, approvals, and workflow authoring are separate enough to create context switching.
- Visual density and component structure are inconsistent.
- The product direction needs to align with the Kinetic Industrialism design system in `docs/design/DESIGN.md`.

## UX Direction

### Primary Navigation

- Factory: template gallery, process drafts, published workflows
- Runs: active, waiting, failed, completed
- Approvals: human decision queue
- Agents: agent catalog and capability status
- Integrations: Jira, GitHub, CI, secrets, webhooks
- Settings: runtime, policy, users, roles

### Core Authoring Flow

1. Choose SDLC template.
2. Configure trigger.
3. Assign agents to tasks.
4. Configure approval gates.
5. Configure evidence requirements.
6. Validate and preview policy impact.
7. Publish to Camunda.
8. Start a test run.

### Core Operator Flow

1. Open Runs.
2. Filter by waiting, failed, or active.
3. Inspect current stage, agent, evidence, and blocker.
4. Approve, reject, retry, or open artifact.
5. See what happened without reading raw engine logs.

## Task List

### Task UI-1: Split Workflow Designer into focused containers

Description: Separate workflow loading, BPMN modeler ownership, metadata editing, validation, and publish actions into focused modules.

Acceptance criteria:

- `WorkflowDesigner` is reduced to page composition and orchestration.
- BPMN modeler lifecycle is isolated.
- Metadata panel and publish panel are independently testable.
- Existing tests are updated without losing coverage.

Verification:

- `npm test -- --run workflowDesigner`
- `npm run build`

Dependencies: None

Estimated scope: Medium

### Task UI-2: Replace BPMN-first labels with SDLC-first language

Description: Update visible UI language so users configure SDLC steps, agents, approvals, and evidence instead of raw BPMN internals.

Acceptance criteria:

- Agent task forms use "Agent", "Goal", "Allowed action", "Evidence required", and "Policy" labels.
- Approval forms use "Approver", "Decision needed", "Risk", and "Escalation" labels.
- Raw Camunda terms are hidden from normal authoring flows.

Verification:

- UI tests cover key labels.
- Manual review confirms no engine jargon in primary flow.

Dependencies: UI-1

Estimated scope: Small

### Task UI-3: Add SDLC template-first landing state

Description: Make the first workflow-authoring screen a practical template picker, not a blank canvas.

Acceptance criteria:

- User sees issue-to-PR, deployment approval, hotfix, and security review templates.
- Each template shows trigger, major stages, required approvals, and expected outputs.
- Selecting a template opens the designer with valid metadata defaults.

Verification:

- Template selection test.
- Manual check at 320px, 768px, 1024px, and 1440px.

Dependencies: UI-1

Estimated scope: Medium

### Task UI-4: Add Camunda publish status and validation panel

Description: Show validation and publish state clearly when a workflow is deployed to Camunda.

Acceptance criteria:

- Validation panel shows Autofac validation and Camunda deployment result separately.
- Publish button is disabled when blocking validation errors exist.
- Deployment failure shows actionable reason and affected element when available.

Verification:

- Tests cover success, validation error, and deployment error states.
- Manual publish failure scenario is visible and readable.

Dependencies: Camunda implementation Tasks 3-4

Estimated scope: Medium

### Task UI-5: Refactor Run Board for operations-first scanning

Description: Make the run list optimized for active work: waiting approvals, failed jobs, active agents, and recent completions.

Acceptance criteria:

- Run Board has compact filters for active, waiting, failed, and completed.
- Rows show workflow, current stage, agent, duration, blocker, and next action.
- Failed and waiting states are visually distinct without relying only on color.

Verification:

- Run board tests cover filters and empty/error/loading states.
- Keyboard navigation reaches filters and row actions.

Dependencies: Camunda implementation Tasks 5, 14

Estimated scope: Medium

### Task UI-6: Refactor Run Detail into evidence-centered layout

Description: Rework Run Detail so operators can understand a run from workflow path, step timeline, evidence, logs, and decisions.

Acceptance criteria:

- Run Detail has stable panels for path, timeline, selected step, evidence/artifacts, and event log.
- Agent output, policy decision, retry state, and approval state are shown together for the selected step.
- Long logs and artifact names do not break layout.

Verification:

- Run detail integration tests.
- Responsive visual check at target breakpoints.

Dependencies: UI-5, Camunda implementation Tasks 9, 14

Estimated scope: Medium

### Task UI-7: Refactor Approvals Dashboard into decision inbox

Description: Make approvals feel like a production decision queue with risk, evidence, and context.

Acceptance criteria:

- Approval list supports pending, approved, rejected, and escalated filters.
- Approval detail shows requested decision, risk factors, evidence, agent output, and related run.
- Approve/reject/request-changes actions capture rationale.

Verification:

- Approval integration tests cover approve and reject.
- Keyboard and focus behavior works for decision dialog.

Dependencies: Camunda implementation Task 8

Estimated scope: Medium

### Task UI-8: Create reusable compact controls and status primitives

Description: Normalize common UI primitives used across factory pages.

Acceptance criteria:

- Shared status badge, risk badge, evidence checklist, step state, and compact toolbar primitives are used consistently.
- Components follow the design system radius, spacing, and typography.
- Components include loading, empty, and error states where relevant.

Verification:

- Component tests cover variants.
- `npm run build`

Dependencies: None

Estimated scope: Medium

### Task UI-9: Remove remaining mocked API behavior from core workflow screens

Description: Ensure Factory, Runs, Run Detail, and Approvals use live API contracts or explicit test fixtures only.

Acceptance criteria:

- Production client does not return mock workflow/run/approval data.
- Tests use isolated fixtures or mocked transport.
- UI errors are visible when backend endpoints fail.

Verification:

- `rg "mock" web/src/api web/src/views web/src/components`
- Frontend integration tests pass.

Dependencies: Backend Camunda adapter and API contract readiness

Estimated scope: Medium

## Design Constraints

- Keep pages dense but readable.
- Avoid hero-style marketing sections inside the app.
- Use compact panels and tables for operational views.
- Use icons in buttons where the command is familiar.
- Do not nest cards inside cards.
- Ensure all text fits at mobile and desktop widths.
- Preserve accessibility: keyboard navigation, focus management, and contrast.

## Checkpoint

The UI cleanup is successful when a new user can configure a template-driven SDLC workflow, publish it, start a test run, approve a gate, and understand the run outcome without needing BPMN or Camunda expertise.
