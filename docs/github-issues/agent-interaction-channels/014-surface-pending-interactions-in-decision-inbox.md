# Surface pending interactions in the decision inbox

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 013.
**Blocks:** 016.
**Layer:** frontend.

## Context

`web/src/views/ApprovalsDashboard.tsx` is the decision inbox and today lists approvals only. An agent's
question is invisible unless someone opens the exact run and the Conversation tab — which is the whole
problem this epic exists to solve. `ToolAccessRequests.tsx` is the closest existing precedent for
rendering an actionable agent request.

`api/client.ts` has `getRunInteractions(runId)` and `answerInteraction(runId, interactionId, answer)`
(`:391`, `:395`). `RunInteraction` in `types/index.ts:195` carries the old, narrower unions.

## Objective

Pending interactions appear in the inbox alongside approvals, answerable without navigating to the run.

## Implementation steps

1. **Update `RunInteraction`** (`types/index.ts:195`) — these unions are now wrong and TypeScript will
   point at every site that needs attention:
   ```ts
   kind: 'post' | 'question' | 'choice' | 'confirm' | 'notify' | 'agent_request' | 'approval' | 'tool_access';
   status: 'pending' | 'answered' | 'posted' | 'expired' | 'rejected' | 'cancelled';
   requestedChannels?: string[];
   respondedChannel?: string | null;
   timeoutAt?: string | null;
   cancelledAt?: string | null;
   resumedAt?: string | null;
   deliveries?: InteractionDelivery[];
   ```
   Add an `InteractionDelivery` interface mirroring 013's summary.

2. **Add client functions** in `api/client.ts` next to the existing pair: `getPendingInteractions()`,
   `rejectInteraction()`, `cancelInteraction()`, `retryInteractionDelivery()`. Follow the existing
   `requestJson` + `encodeURIComponent` style.

3. **Add a pending-interactions section** to `ApprovalsDashboard`. Per interaction show: requesting
   agent (reuse `AgentIdentityBadge`, as `ConversationTab` does), workflow, run (linked to run details),
   step, the question, the choices, a blocking indicator, **age**, and **time until timeout**. Blocking
   and age are the two fields that tell an operator what is actually stuck — do not bury them.

4. **Answer inline.** Choices → buttons. `confirm` → approve/reject. Free-text → an input. Reuse
   `ToolAccessRequests.tsx`'s interaction shape rather than inventing a new one.

5. **Poll for updates.** Match whatever interval the dashboard already uses for approvals; do not
   introduce a second cadence. **No SSE/WebSocket** — the repo has neither and adding one is out of
   scope (001). Polling is what makes AC 10's "all open clients update" true: when Slack wins, the next
   poll flips the row to answered.

6. **Handle the 409 correctly — this is the whole point of the epic in one interaction.** When a user
   clicks Approve and the API returns `409` because Slack just won, do **not** show a red error. Show a
   neutral message naming the winner ("Already answered via Slack by dana") and refresh the row. Losing
   a race is normal and expected, not a failure.

7. **Empty and terminal states:** show pending only; disable controls the instant a row goes terminal;
   surface `expired`/`cancelled` distinctly from `answered`.

8. **Accessibility:** follow the existing `role="list"` / `aria-label` conventions from
   `ConversationTab.tsx:58`.

## Files

- `web/src/views/ApprovalsDashboard.tsx`
- `web/src/api/client.ts` (`:391`)
- `web/src/types/index.ts` (`:195`)
- `web/src/test/approvals.integration.test.tsx`

## Acceptance criteria

- Pending human interactions from all runs render with agent, workflow, run link, step, question,
  choices, blocking, age, and timeout.
- Answering from the inbox resumes the run (AC 1 from the UI's side).
- A row answered elsewhere disappears/updates on the next poll without a page reload (AC 10).
- A `409` shows a neutral "already answered via X", not an error.
- Terminal rows are not answerable.
- Approvals rendering is unchanged; existing tests pass with only additive changes.

## Verification

```bash
cd web && npm test && npm run lint && npm run build
```

## Out of scope

Run-details conversation rendering (015).
