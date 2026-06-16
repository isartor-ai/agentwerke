# Deploy workflows to Camunda on publish

## Summary
Change workflow publish so the production path deploys projected BPMN to Camunda and records Camunda deployment metadata.

## Why
Publishing should make a workflow executable in the real BPMN engine, not just persist XML inside Autofac.

## Scope
- Add Camunda deployment through `IWorkflowEngineAdapter` or infrastructure adapter.
- Store deployment id, process id/key, version, and deployment time on the workflow definition or related metadata.
- Return deployment errors as structured API responses.
- Do not mark workflow active when Camunda deployment fails.

## Acceptance Criteria
- Publishing a valid workflow deploys to Camunda.
- Workflow detail shows Camunda deployment/process metadata for diagnostics.
- Deployment failure is visible in the UI/API and leaves workflow unpublished.
- Tests cover success and failure paths.

## Verification
- Integration test deploys a minimal workflow to Camunda.
- API publish endpoint returns structured errors on invalid Camunda BPMN.

## Suggested Files
- `src/Autofac.Application/Workflows`
- `src/Autofac.Infrastructure/Workflows`
- `src/Autofac.Api/Controllers/WorkflowsController.cs`
- `tests/Autofac.Application.Tests`
