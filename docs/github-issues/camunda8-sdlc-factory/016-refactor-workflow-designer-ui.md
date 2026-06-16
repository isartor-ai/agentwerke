# Refactor Workflow Designer into SDLC factory builder modules

## Summary
Split the current Workflow Designer into focused modules and shift the visible authoring language from BPMN-first to SDLC-first.

## Why
The easy-to-use product promise depends on users configuring SDLC processes, not engine mechanics.

## Scope
- Split page composition, BPMN modeler lifecycle, metadata editing, validation, and publishing into focused components/hooks.
- Rename visible labels toward agents, goals, approvals, evidence, and policy.
- Hide Camunda-specific terms from normal authoring flows.
- Preserve advanced BPMN editing for power users.

## Acceptance Criteria
- `WorkflowDesigner` becomes mostly page composition.
- Metadata editing is independently testable.
- Primary labels use SDLC/product language.
- Existing designer tests still pass after updates.

## Verification
- `npm test -- --run workflowDesigner`
- `npm run build`
- Manual authoring review confirms reduced engine jargon.

## Suggested Files
- `web/src/views/WorkflowDesigner.tsx`
- `web/src/components/BpmnModeler.tsx`
- `web/src/bpmn/properties`
- `web/src/test/workflowDesigner.integration.test.tsx`
