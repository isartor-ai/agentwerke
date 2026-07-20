# Enrich the run conversation with channels, confirmation, and delivery state

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 013, 014.
**Blocks:** 016.
**Layer:** frontend.

## Context

`web/src/components/ConversationTab.tsx` renders the run's interaction thread with an inline answer box
and a `kindLabel` switch (`:19`) already covering `post`/`question`/`choice`/`agent_request`/`notify`/
`approval`/`tool_access`. It is the audit surface a reviewer actually reads, so it has to be truthful:
right now it renders a free-text box for every pending human interaction and shows nothing about
channels, delivery, or timeouts.

Note the two data corrections landing underneath it: 009 makes blocking `agent.request` rows report
`Blocking = true` (they currently claim `posted`/non-blocking), and 013 adds `respondedChannel`. The
thread will start telling the truth about delegations — make sure the rendering reflects that.

## Objective

The conversation shows the full lifecycle: what was asked, where it was sent, whether it arrived, who
answered, via which channel, and what happened.

## Implementation steps

1. **Extend `kindLabel`** (`:19`) with `confirm` → "needs confirmation". Keep the existing labels.

2. **Render controls by kind**, replacing the one-size free-text box:
   - `choice` → a button per option;
   - `confirm` → Approve / Reject (reject prompts for a reason — the reason becomes the step's failure
     message, so it is worth capturing well);
   - `question` → free text (today's behavior);
   - `notify`, `post` → no controls.

3. **Show channel state per interaction:** "Sent to: ui, slack" from `requestedChannels`, and once
   terminal, "Answered via Slack by dana at …" from `respondedChannel`/`respondedBy`/`respondedAt`.
   **AC 16 asks the run history to show the winning channel** — this is where a reviewer sees it.

4. **Show delivery failures with a retry action.** A `failed` delivery row renders the channel, attempt
   count, and error, with a Retry button calling the 013 endpoint. **AC 14 is only satisfied when this
   is visible and actionable** — a failed Slack post that is merely logged is invisible to the person
   who needs it.
   Render `not_supported` distinctly and explain it: "Teams cannot accept replies — answer here or in
   Slack." Otherwise a Teams user will assume it is broken (012).

5. **Show timeout state:** for pending rows with `timeoutAt`, a countdown or "expires in 12m". For
   `expired` rows, "Expired at …" and what followed from `ExpiresAction`.

6. **Disable controls on terminal states** — `answered`, `rejected`, `expired`, `cancelled`. Do not
   merely hide them: showing a spent question with its outcome is the audit value.

7. **Distinguish the states visually:** answered, rejected, expired, cancelled, and failed-delivery must
   not all look alike. Reuse existing status styling from `StepTimeline.tsx`/`index.css` rather than
   adding a palette.

8. **Render agent-to-agent pairs together.** Rows sharing a `correlationId` are a request and its
   reply — group or visually link them so a delegation reads as one exchange. Show `delegationDepth`
   when > 0.

9. **Poll consistently** with `RunDetail.tsx`'s existing cadence; no new mechanism.

10. **Treat responses as untrusted text.** A response can contain anything a human or a channel typed —
    render as text, never as HTML/markdown-with-HTML. React escapes by default; do not reach for
    `dangerouslySetInnerHTML`.

## Files

- `web/src/components/ConversationTab.tsx`
- `web/src/views/RunDetail.tsx`
- `web/src/test/runDetail.integration.test.tsx`

## Acceptance criteria

- Choice, confirm, and free-text each render their correct controls.
- **AC 16:** the thread shows request, channels sent to, accepted response, responder, winning channel,
  and timestamps.
- **AC 14:** a failed delivery is visible with attempts and error, and Retry re-attempts it.
- Terminal rows are visible but not answerable; expired/cancelled/rejected are distinguishable.
- **AC 7:** a blocking `agent.request` and its reply render as one correlated exchange.
- Teams `not_supported` is explained, not shown as an error.
- Existing `runDetail.integration.test.tsx` passes with only additive changes.

## Verification

```bash
cd web && npm test && npm run lint && npm run build
```

## Out of scope

The inbox (014). Editing or retracting an answer once accepted.
