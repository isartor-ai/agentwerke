# Refactor Run Board and Run Detail for operations-first monitoring

## Summary
Update run monitoring so operators can scan active, waiting, failed, and completed runs and inspect evidence or blockers quickly.

## Why
The dark software factory must be mostly autonomous but fully inspectable.

## Scope
- Add compact filters for active, waiting, failed, completed, and incident states.
- Show current stage, agent, duration, blocker, and next action in run rows.
- Rework Run Detail around path, timeline, selected step, evidence/artifacts, and event log.
- Ensure long logs and artifact names do not break layout.

## Acceptance Criteria
- Run Board supports operational filters.
- Run Detail shows agent output, policy decision, retry state, approval state, evidence, and events for selected step.
- Failed/waiting states are clear without relying only on color.
- Loading, empty, and error states are present.

## Verification
- Run board and run detail integration tests.
- Keyboard navigation through filters and row actions.
- Responsive visual check.

## Suggested Files
- `web/src/views/RunBoard.tsx`
- `web/src/views/RunDetail.tsx`
- `web/src/components/StepTimeline.tsx`
- `web/src/components/BpmnRunGraph.tsx`
