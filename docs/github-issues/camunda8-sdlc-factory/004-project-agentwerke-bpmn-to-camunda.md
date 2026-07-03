# Project Agentwerke BPMN metadata to Camunda-compatible BPMN

## Summary
Implement a projection step that turns Agentwerke agent metadata into Camunda-deployable BPMN service tasks and task headers.

## Why
Camunda should execute valid Camunda BPMN. Rich Agentwerke metadata can exist in the UI and database, but deployed BPMN must use Camunda-supported service task configuration.

## Scope
- Convert Agentwerke agent tasks to `bpmn:serviceTask` with `zeebe:taskDefinition type="agentwerke.agent"`.
- Store small static task configuration in Zeebe task headers or Agentwerke metadata keyed by BPMN element id.
- Preserve approval tasks as BPMN user tasks.
- Add validation errors for unsupported or missing metadata.

## Acceptance Criteria
- Valid Agentwerke workflow projects to Camunda-deployable BPMN.
- Agent, action, policy tag, and evidence metadata can be resolved by the worker.
- Unsupported task metadata blocks publish with actionable errors.
- Projection is covered by unit tests.

## Verification
- Unit tests cover agent task, approval task, missing metadata, and unsupported elements.
- Projected BPMN deploys to local Camunda.

## Suggested Files
- `src/Agentwerke.Workflows/Bpmn`
- `src/Agentwerke.Infrastructure/Workflows`
- `tests/Agentwerke.Workflows.Tests`
