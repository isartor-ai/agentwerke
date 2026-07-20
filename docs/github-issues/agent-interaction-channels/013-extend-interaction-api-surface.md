# Add the interactions API surface

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 005.
**Blocks:** 014, 015.
**Layer:** backend / API.

## Context

Today: `GET /runs/{runId}/interactions` (`RunsController.cs:366`) queries the `DbContext` directly and
maps to `InteractionSummary`; `POST /runs/{runId}/interactions/{id}/answer` (`:379`) is
`[Authorize(Policy = AgentwerkePolicies.Approver)]` and maps exceptions to 404/409/422, returning
`Accepted(result)`.

Missing: a cross-run pending list (the decision inbox needs one query, not one per run), reject, cancel,
and the new fields on the summary.

## Objective

Expose the 005 verbs and the new state over HTTP, following the conventions already in these
controllers.

## Implementation steps

1. **Extend `InteractionSummary`** (`src/Agentwerke.Api/Contracts/Runs/InteractionSummary.cs`) with
   optional trailing params — it is a positional record, so appending with defaults keeps every existing
   construction valid: `RequestedChannels`, `RespondedChannel`, `TimeoutAt`, `CancelledAt`, `ResumedAt`,
   `DelegationDepth`, and `IReadOnlyList<InteractionDeliverySummary> Deliveries`.

2. **Add `InteractionDeliverySummary`**: `Channel`, `Status`, `ChannelMessageId`, `Attempts`,
   `LastAttemptAt`, `LastError`. `LastError` is operator-facing — make sure it carries the provider's
   message, not a swallowed generic.

3. **Extend `AnswerInteractionRequest`** with an optional `Decision` (`approve`/`reject`), keeping
   `Answer` — `AnswerInteractionRequest(string Answer, string? Decision = null)` is source-compatible.

4. **Add `POST /runs/{runId}/interactions/{id}/reject`**, `[Authorize(Policy = Approver)]`, body
   `{ reason }` → `RejectInteractionAsync`. Or fold into `answer` when `Decision == "reject"`. **Pick
   one and be consistent** — prefer a distinct verb: rejection has different semantics (it fails the
   step) and deserves a distinct audit action and a distinct permission story later.

5. **Add `InteractionsController`** (`[Route("api/interactions")]`) for the cross-run surface:
   - `GET /api/interactions?status=pending&addresseeType=human&runId=` → `[Authorize(Policy = Viewer)]`,
     backed by `GetPendingAsync` (004);
   - `POST /api/interactions/{id}/cancel` → `[Authorize(Policy = Approver)]`, body `{ reason }`;
   - `POST /api/interactions/{id}/deliveries/{channel}/retry` → `[Authorize(Policy = Operator)]`, calls
     `IInteractionRouter.RetryAsync`. **This is AC 14's "retryable"** — without it, a failed delivery is
     observable but inert.

6. **Map exceptions to 001's status table.** `InteractionNotFoundException` → `404` (empty, matching
   the existing `NotFound()`); `InteractionNotPendingException` → `409` with `{ message }` — this
   single mapping already covers duplicate, already-answered, expired, and cancelled, since all four
   are "not pending"; make the exception message name the actual status so the client can tell them
   apart. `InvalidInteractionAnswerException` → `400` listing valid options.
   `WorkflowRunNotFoundException` → `422` (existing behavior — preserve).

7. **Include `AcceptedChannel`** in the answer response so the UI can show which channel won.

8. **Keep `{ message }`** for controller errors — that is the `RunsController` convention.
   `WebhooksController` uses `{ error }`; do not mix them.

9. **Prefer the repository over the raw `DbContext`.** `ListInteractions` queries `_dbContext` directly
   (`:369`); the new endpoints should use `IAgentInteractionRepository`. Refactoring the existing action
   is optional and can be skipped if it churns tests.

## Files

- `src/Agentwerke.Api/Controllers/InteractionsController.cs` (new)
- `src/Agentwerke.Api/Controllers/RunsController.cs` (`:366`, `:379`, `:414`)
- `src/Agentwerke.Api/Contracts/Runs/InteractionSummary.cs`, `AnswerInteractionRequest.cs`
- `tests/Agentwerke.Api.Tests/RunsControllerTests.cs`

## Acceptance criteria

- Every row of 001's status table is covered by a controller test.
- **AC 11:** non-`Approver` answering → `403`; unauthenticated → `401`; answer outside `options` → `400`.
- `GET /api/interactions?status=pending` returns pending interactions across runs with deliveries
  inlined.
- Retry endpoint re-attempts delivery and updates the delivery row.
- Existing `RunsControllerTests` pass with only additive changes.

## Verification

```bash
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Api.Tests --no-build
```

## Out of scope

UI (014, 015). A public interaction-creation endpoint — agents create interactions through tools, not
HTTP (001).
