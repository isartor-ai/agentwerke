# Bridge Camunda user tasks to Agentwerke approvals

## Summary
Create Agentwerke approval requests when Camunda reaches user tasks and complete Camunda user tasks when users decide.

## Why
Human approval is central to the dark software factory model. Camunda should own the waiting state, and Agentwerke should own the approval UX and audit record.

## Scope
- Detect or query active Camunda user tasks for Agentwerke process instances.
- Create pending `ApprovalRequest` records in Agentwerke.
- Complete the Camunda user task on approve.
- Apply rejection behavior and record rationale.

## Acceptance Criteria
- User task creates a pending approval visible in the Approvals dashboard.
- Approving completes the Camunda user task and advances the workflow.
- Rejecting follows documented behavior and records who/why.
- Audit trail includes run id, task id, decision, approver, and comment.

## Verification
- Integration test reaches user task, approves it, and process continues.
- Manual test confirms approval UI can drive Camunda continuation.

## Suggested Files
- `src/Agentwerke.Application/Workflows`
- `src/Agentwerke.Infrastructure/Persistence`
- `src/Agentwerke.Api/Controllers/ApprovalsController.cs`
- `tests/Agentwerke.Application.Tests`
