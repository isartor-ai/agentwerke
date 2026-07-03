# Refactor Approvals into a production decision inbox

## Summary
Make the approvals UI a decision queue with risk, evidence, agent output, run context, and rationale capture.

## Why
Human gates should be rare, meaningful, and easy to decide correctly.

## Scope
- Add filters for pending, approved, rejected, escalated, and expired.
- Show decision context: requested action, risk factors, evidence, agent output, and related run.
- Capture rationale for approve, reject, and request-changes.
- Improve focus management for decision dialogs.

## Acceptance Criteria
- Approver can decide without leaving approval detail.
- Approval detail shows evidence and related run context.
- Rationale is required for reject and request-changes.
- Keyboard navigation and focus behavior are correct.

## Verification
- Approval integration tests for approve/reject/request-changes.
- Manual keyboard test.
- `npm run build`

## Suggested Files
- `web/src/views/ApprovalsDashboard.tsx`
- `web/src/components/ConfirmDialog.tsx`
- `web/src/api/client.ts`
- `src/Agentwerke.Api/Controllers/ApprovalsController.cs`
