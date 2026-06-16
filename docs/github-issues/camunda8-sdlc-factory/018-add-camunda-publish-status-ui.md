# Add Camunda validation and publish status UI

## Summary
Show Autofac validation and Camunda deployment status clearly during workflow publish.

## Why
Users need to understand whether a workflow is invalid because of Autofac metadata or because Camunda rejected deployment.

## Scope
- Add publish panel or drawer that separates validation, policy preview, and Camunda deployment result.
- Disable publish when blocking validation errors exist.
- Render deployment errors with affected element when possible.
- Show deployment/process identifiers after success.

## Acceptance Criteria
- Validation errors block publish.
- Camunda deployment errors are visible and actionable.
- Successful publish shows deployment metadata.
- Tests cover success, validation failure, and deployment failure.

## Verification
- Frontend tests for publish states.
- Manual publish failure check.
- `npm run build`

## Suggested Files
- `web/src/views/WorkflowDesigner.tsx`
- `web/src/api/client.ts`
- `web/src/types/index.ts`
- `web/src/test/workflowDesigner.integration.test.tsx`
