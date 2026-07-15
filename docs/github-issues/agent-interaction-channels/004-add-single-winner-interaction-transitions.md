# Add single-winner interaction transitions to the repository

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 003.
**Blocks:** 005, 006, 007.
**Layer:** backend / persistence.

## Context

This is the correctness core of the epic. Today `AnswerInteractionAsync`
(`WorkflowRunOrchestrationService.cs:199`) reads the interaction, checks `Status == Pending`, writes,
and enqueues an outbox `Resume` — with **no concurrency control**. Two responders (UI + Slack) can both
observe `Pending` and both enqueue a `Resume`, resuming the run twice. Once interactions fan out to
multiple channels (006), that race stops being theoretical.

`IAgentInteractionRepository` lives in `src/Agentwerke.Application/Agents/AgentInteractionContracts.cs`
and is implemented twice: `AgentInteractionRepository` (EF) and `InMemoryInteractionRepository` (tests).
**Both must be updated** or the agent test suite will not compile.

## Objective

Make "first valid terminal response wins" a property of the persistence layer, not of caller
discipline.

## Implementation steps

1. **Add a transition result type** in `AgentInteractionContracts.cs`:
   ```csharp
   public enum InteractionTransitionOutcome { Won, AlreadyTerminal, NotFound }

   public sealed record InteractionTransitionResult(
       InteractionTransitionOutcome Outcome,
       AgentInteraction? Interaction);
   ```

2. **Add `TryTransitionAsync` to `IAgentInteractionRepository`:**
   ```csharp
   /// <summary>
   /// Atomically moves a pending interaction to a terminal status. Returns Won for exactly one
   /// caller; every other concurrent or late caller gets AlreadyTerminal. The caller must enqueue
   /// the outbox Resume only on Won.
   /// </summary>
   Task<InteractionTransitionResult> TryTransitionAsync(
       string interactionId,
       string toStatus,
       string? response,
       string? respondedBy,
       string? respondedChannel,
       CancellationToken cancellationToken);
   ```

3. **Implement it in `AgentInteractionRepository`:**
   - Load the tracked entity; `null` → `NotFound`.
   - `AgentInteractionStatuses.IsTerminal(current)` → `AlreadyTerminal` (covers duplicate and late).
   - Set status, response, responder, channel, `RespondedAt`; **increment `Version`** so the token
     changes even when EF sees no other modification.
   - `SaveChangesAsync` inside `try`; catch `DbUpdateConcurrencyException` → reload and return
     `AlreadyTerminal`. This is the branch that makes the loser lose.
   - Return `Won`.

4. **Add the query methods:**
   ```csharp
   Task<IReadOnlyList<AgentInteraction>> GetPendingAsync(
       string? runId, string? addresseeType, CancellationToken cancellationToken);

   /// <summary>Pending rows whose TimeoutAt is non-null and at or before nowIso. For the sweeper.</summary>
   Task<IReadOnlyList<AgentInteraction>> GetDueForExpiryAsync(
       string nowIso, CancellationToken cancellationToken);
   ```
   `GetDueForExpiryAsync` compares ISO-8601 strings with `string.CompareOrdinal`-equivalent SQL
   (`i.TimeoutAt <= nowIso`). This is **only correct because every timestamp is written UTC with `"o"`**
   — same length, same offset, so ordinal ordering matches chronological ordering. Add a code comment
   saying so; a local-time or non-`"o"` value would silently break the sweeper.

5. **Add delivery persistence** — either extend this repository or add
   `IInteractionDeliveryRepository`; prefer a separate interface, since the router (006) needs
   deliveries but not interactions:
   `UpsertAsync(delivery)`, `GetByInteractionAsync(interactionId)`,
   `GetByChannelMessageAsync(channel, channelMessageId)` (inbound correlation for 011).
   Upsert keys on the unique `(InteractionId, Channel)` index from 003.

6. **Mirror everything in `InMemoryInteractionRepository`.** Model the race honestly: guard with a
   `lock` and check `IsTerminal` inside it, so a concurrent test exercises real
   winner/loser behavior rather than always passing.

## Files

- `src/Agentwerke.Application/Agents/AgentInteractionContracts.cs`
- `src/Agentwerke.Infrastructure/Persistence/AgentInteractionRepository.cs`
- `src/Agentwerke.Infrastructure/Persistence/InteractionDeliveryRepository.cs` (new)
- `tests/Agentwerke.Agents.Tests/InMemoryInteractionRepository.cs`

## Acceptance criteria

- **AC 10 (partial):** N concurrent `TryTransitionAsync` calls on one pending interaction yield exactly
  one `Won` and N-1 `AlreadyTerminal`.
- **AC 9 (partial):** transitioning an already-answered interaction returns `AlreadyTerminal` and does
  not overwrite the original response, responder, or channel.
- `GetDueForExpiryAsync` returns only pending rows with a non-null `TimeoutAt` at or before now, and
  never a row with `TimeoutAt = null`.
- Existing agent tests compile and pass unmodified.

## Verification

```bash
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Application.Tests --no-build
dotnet test tests/Agentwerke.Agents.Tests --no-build
```

## Out of scope

Callers. Audit. Outbox. All of that is 005.
