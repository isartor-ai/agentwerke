# Add Camunda-compatible issue-to-PR SDLC factory template

## Summary
Add the first Camunda-compatible SDLC workflow template for an issue-to-PR delivery flow.

## Why
The product needs one real factory line before broad workflow features.

## Scope
- Add a template with requirement analysis, spec approval, implementation, test, PR creation, and final review.
- Use supported Agentwerke agent task metadata.
- Ensure generated BPMN deploys to Camunda.
- Include realistic default agents and evidence requirements.

## Acceptance Criteria
- Template validates through Agentwerke validation.
- Template deploys to Camunda.
- Template can start and reach the first `agentwerke.agent` job.
- Template is selectable from the UI.

## Verification
- Template fixture test.
- Manual publish/start run check.

## Suggested Files
- `docker/seed-manual.sql`
- `docs/manual-test-camunda8-sdlc-factory.md`
- `web/src/bpmn`
- `web/src/views/WorkflowDesigner.tsx`
