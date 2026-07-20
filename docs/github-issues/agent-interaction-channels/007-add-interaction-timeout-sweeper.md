# Add the interaction timeout sweeper

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 004, 005.
**Blocks:** nothing.
**Layer:** backend / worker.

## Context

`AgentInteractionStatuses.Expired` has been declared since the interaction table was introduced and is
**never assigned anywhere in `src/`**. A blocking `human.ask` with no responder parks its run forever.
This ticket writes the missing writer.

`RunDispatchWorker` (`src/Agentwerke.Infrastructure/Workers/RunDispatchWorker.cs`) is the pattern:
`BackgroundService`, a `PollInterval` constant, `IServiceScopeFactory` for scoped resolution inside the
loop, registered with `services.AddHostedService<>()` in `DependencyInjection.cs:92`.

## Objective

Move due interactions to `expired` and honour `ExpiresAction`, racing safely against a late answer.

## Implementation steps

1. **Create `InteractionTimeoutSweeper : BackgroundService`** in
   `src/Agentwerke.Infrastructure/Workers/`. Copy `RunDispatchWorker`'s shape: `IServiceScopeFactory`,
   a logger, a poll interval (default 30s, from `InteractionOptions`).

2. **Loop body:**
   - create a scope; resolve `IAgentInteractionRepository` and `IWorkflowRunOrchestrationService`;
   - `nowIso = DateTimeOffset.UtcNow.ToString("o")`;
   - `GetDueForExpiryAsync(nowIso)` — indexed by `(Status, TimeoutAt)` from 003;
   - for each, call `ExpireInteractionAsync` (005);
   - drain the full batch each cycle rather than one per poll (same reasoning as `RunDispatchWorker`'s
     comment about backlog).

3. **Rely on the concurrency token for the race.** Do not pre-check the status — call
   `ExpireInteractionAsync` and let `TryTransitionAsync` decide. If a human answered microseconds
   earlier, the sweeper gets `AlreadyTerminal` and does nothing. **Do not treat that as an error**; it
   is the mechanism working. Log at `Debug`.

4. **Wrap each interaction in its own try/catch.** One poisoned row must not kill the sweeper loop for
   every other run. Log and continue.

5. **Register** with `services.AddHostedService<InteractionTimeoutSweeper>()` next to
   `RunDispatchWorker` at `DependencyInjection.cs:92`.

6. **Multi-instance safety.** Two API instances both run a sweeper. This is safe **because** of the
   concurrency token: both may pick up the same row, exactly one transitions, the other logs Debug. No
   leader election needed. State that in a code comment so nobody adds one later.

7. **Default timeout is null.** `InteractionOptions.DefaultTimeoutSeconds` defaults to `null` — the
   sweeper never expires interactions unless a timeout is set explicitly per config or per interaction.
   This is deliberate rollout safety: enabling this feature must not start expiring runs that
   previously waited indefinitely.

## Files

- `src/Agentwerke.Infrastructure/Workers/InteractionTimeoutSweeper.cs` (new)
- `src/Agentwerke.Infrastructure/DependencyInjection.cs` (`:92`)

## Acceptance criteria

- **AC 12:** an interaction whose `TimeoutAt` has passed becomes `expired` on the next sweep, and
  `ExpiresAction` is honoured — `fail` fails the step, `default_answer` resumes with the default,
  `continue` resumes with a no-answer result.
- An interaction with `TimeoutAt = null` is never expired, however old.
- A sweep racing an answer produces exactly one terminal transition and one `Resume`.
- An exception on one row does not stop the batch or the loop.

## Verification

```bash
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Application.Tests --no-build
```

Test the sweep by invoking the loop body directly with an injected clock/`nowIso` rather than sleeping
— no `Task.Delay` in tests.

## Out of scope

Per-interaction timeout arguments (008). UI expiry display (014, 015).
