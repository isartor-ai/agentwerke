# Surface Camunda retry and incident state in Run Detail

## Summary
Expose Camunda job failures, retries, and incidents through Agentwerke APIs and Run Detail UI.

## Why
Operators need to distinguish agent failure, policy block, exhausted retries, and engine incident without opening Camunda tooling.

## Scope
- Add retry count, incident reason, failed element id, and recommended action to run detail.
- Render incident/blocker state in Run Board and Run Detail.
- Add failure-path tests.

## Acceptance Criteria
- Failed job retry state appears in Agentwerke run detail.
- Incident state is visually distinct and textually clear.
- Operator can identify the affected workflow step.
- Tests cover incident serialization and UI rendering.

## Verification
- Backend tests for incident mapping.
- Frontend tests for run detail failure state.
- Manual failure scenario.

## Suggested Files
- `src/Agentwerke.Api/Contracts/Runs`
- `src/Agentwerke.Application/Workflows`
- `web/src/views/RunBoard.tsx`
- `web/src/views/RunDetail.tsx`
