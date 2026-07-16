# Add reject, cancel, and expire orchestration verbs and guard answer

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 004.
**Blocks:** 007, 013.
**Layer:** backend / application.

## Context

`WorkflowRunOrchestrationService.AnswerInteractionAsync` (`:199`) is the only verb today. It:

- scopes the interaction to its run (`:211`) — **keep this guard**, it prevents answering another run's
  question;
- throws `InteractionNotFoundException` / `InteractionNotPendingException` / `WorkflowRunNotFoundException`
  — `RunsController` maps these to 404/409/422, so **preserve the exception types**;
- audits via `WriteAuditAsync(action: "interaction.answer", …)`;
- enqueues `OutboxOperations.Resume` with an `OutboxResumePayload`.

## Objective

Route every terminal transition through 004's single-winner primitive, and add the three missing verbs.

## Implementation steps

1. **Rewrite `AnswerInteractionAsync` to use `TryTransitionAsync`.** Keep the signature, the run-scope
   guard, and the exception types. Extend `AnswerInteractionCommand` with
   `string Channel = InteractionChannels.Ui` and optional `string? Decision`.
   - Validate the answer against `Options` when non-empty → throw a new `InvalidInteractionAnswerException`
     (400 in 013). Validate **before** transitioning.
   - Call `TryTransitionAsync(id, Answered, answer, answeredBy, channel, ct)`.
   - `NotFound` → `InteractionNotFoundException`; `AlreadyTerminal` → `InteractionNotPendingException`
     (already mapped to 409 — no controller change needed for the duplicate case).
   - **Only on `Won`:** audit, then enqueue `Resume`. This ordering is the whole point — the loser must
     reach neither.
   - Return `AnswerInteractionResult` extended with `AcceptedChannel`.

2. **Add `RejectInteractionAsync`.** Transition to `Rejected` with the reason as the response. Audit
   `interaction.reject`. **Enqueue `Resume` on `Won`** — the step must re-run so the tool can throw
   `ConfirmationRejectedException` (008) and fail the step. A rejection is not "do nothing"; it is a
   resume that ends in failure.

3. **Add `CancelInteractionAsync`.** Transition to `Cancelled`, set `CancelledAt`/`CancelledBy`. Audit
   `interaction.cancel`. Enqueue `Resume` on `Won` **only if the interaction was `Blocking`** — a
   cancelled non-blocking notify has no parked step to wake.

4. **Add `ExpireInteractionAsync`** (called by the sweeper, 007). Transition to `Expired`. Audit
   `interaction.expire`. Honour `ExpiresAction` on `Won`:
   - `Fail` → enqueue `Resume`; the tool fails the step on re-run.
   - `DefaultAnswer` → write `DefaultAnswer` into the response before transitioning, then `Resume`.
   - `Continue` → enqueue `Resume`; the tool returns a "no answer" result and the agent proceeds.

5. **Reject responses for terminal runs.** Before transitioning, load the run (the method already does
   at `:221`) and refuse if the run is cancelled or failed → `WorkflowRunNotFoundException` or a new
   `RunNotAcceptingResponsesException` (422). **This is AC 15** — a cancelled run must not be resumed by
   a late channel response.

6. **Add `MarkResumedAsync`** setting `ResumedAt`, called from the resume path so the UI can distinguish
   "response accepted" from "run resumed".

7. **Add a metrics counter** for `AlreadyTerminal` outcomes via the existing `IWorkflowMetrics` —
   race-loss rate is the signal that channel fan-out is behaving.

## Files

- `src/Agentwerke.Application/Workflows/WorkflowRunOrchestrationService.cs` (`:199`)
- `src/Agentwerke.Application/Workflows/WorkflowRunContracts.cs` (commands, results, exceptions)

## Acceptance criteria

- **AC 1:** answer via UI → run leaves `waiting_user`, step re-runs exactly once.
- **AC 2:** reject a `confirm` → status `rejected`, resume enqueued, step fails with the reason.
- **AC 9:** duplicate answer → `InteractionNotPendingException`, exactly one `Resume` in the outbox.
- **AC 10:** concurrent answers → one `Won`, one `Resume`, correct `RespondedChannel`.
- **AC 11 (partial):** answer outside `Options` → `InvalidInteractionAnswerException`.
- **AC 15:** response to a cancelled run's interaction → rejected, no `Resume`.
- Audit rows exist for answer, reject, cancel, expire.

## Verification

```bash
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Application.Tests --no-build
```

## Out of scope

HTTP surface (013). The sweeper's schedule (007). Tool-side handling of the resume (008).
