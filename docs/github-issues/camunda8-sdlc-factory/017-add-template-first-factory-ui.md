# Add template-first Factory authoring UI

## Summary
Make the first workflow-authoring experience a template-driven SDLC factory builder.

## Why
Easy use is the key product constraint. Users should start from proven SDLC flows instead of a blank BPMN canvas.

## Scope
- Add issue-to-PR, deployment approval, hotfix, and security review templates.
- Show trigger, stages, approvals, agents, and expected outputs for each template.
- Open selected template in the designer with valid defaults.
- Ensure responsive behavior at target breakpoints.

## Acceptance Criteria
- User can choose a template before editing BPMN.
- Template preview shows practical SDLC information.
- Selected template opens with valid Agentwerke metadata.
- Mobile and desktop layouts remain usable.

## Verification
- Template selection tests.
- Manual checks at 320px, 768px, 1024px, and 1440px.
- `npm run build`

## Suggested Files
- `web/src/views/WorkflowDesigner.tsx`
- `web/src/bpmn/agentCatalog.ts`
- `web/src/test/workflowDesigner.integration.test.tsx`
- `docs/ui-cleanup-refactor-plan.md`
