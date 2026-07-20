# Add human.confirm and fix the re-run answer lookup

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 002, 006.
**Blocks:** 016.
**Layer:** backend / agent tools.

## Context

`HumanInteractionTools.cs` holds `HumanAskTool` (blocking) and `HumanNotifyTool` (non-blocking). The
blocking mechanism works and must be preserved: persist the interaction → throw
`AgentInteractionRequiredException` → `ToolGateway` re-throws → `AgentOrchestrator:351` returns
`WaitingUser` → the step re-runs on resume → the tool finds the answer and returns it. Nothing polls
and no thread is held.

**There is a latent bug this ticket fixes.** On re-run, `HumanAskTool` finds its answer by matching
`i.Prompt == question` with `StringComparison.Ordinal` (`HumanInteractionTools.cs:47-51`). The comment
there says StepId is not a key because it changes per re-run — true — but prompt text is not a key
either. An LLM that rephrases its question on re-run ("Which environment?" → "Which environment should
I deploy to?") will not match, will create a **second** interaction, and will park the run again. The
human answers into the void. This gets more likely, not less, as models get chattier.

## Objective

Add `human.confirm`, add channel/timeout arguments, and key re-run lookup on an identity rather than
on model-generated prose.

## Implementation steps

1. **Thread the BPMN node id into the tool context first.** This is a prerequisite, not an option —
   see the key analysis below. `AgentToolExecutionContext`
   (`src/Agentwerke.Agents/Tools/ToolContracts.cs:35`) currently carries
   `RunId, StepId, AgentName, Action, Environment, PurposeType, PolicyTag, Attempt` — **no node id**.
   Add `string? NodeId = null` (optional, so existing construction sites keep compiling) and populate
   it in `AgentOrchestrator` at `:143` and `:278`, which already have the `node` parameter in scope
   (`ExecuteAsync(runId, stepId, node, attempt, …)` → `node.Id`).

2. **Replace prompt matching with a run-context key on `NodeId`.** `RunContextEntry`
   (`src/Agentwerke.Domain/Persistence/RunContextEntry.cs`) is a per-run key/value bag with unique
   keys — exactly what this needs.
   - On first call, after persisting the interaction, write
     `key = $"interaction.pending.{context.NodeId}"`, `value = interaction.Id`, `kind = "interaction"`.
   - On re-run, look up that key first. Found → load by id and act on its status. Not found → this is a
     genuinely new question; create one.
   - Delete the key once the interaction is terminal and its result has been returned, so a later
     question from the same node is not confused for the old one.

   **Do not key on `StepId` — verified 2026-07-15.** `WorkflowInstanceEngine:567` calls
   `store.CreateStepAsync(...)` on every attempt, and `WorkflowRuntimeStore.CreateStepAsync:107` sets
   `Id = $"step_{Guid.NewGuid():N}"`. A re-run therefore produces a **brand-new `StepId`**, so a
   StepId-keyed lookup would miss every time, create a second interaction, and park the run again —
   i.e. it would reproduce exactly the bug this step exists to fix. The existing comment at
   `HumanInteractionTools.cs:47` ("StepId is not a key — it changes per re-run") is correct.
   `node.Id` is the BPMN node id and is stable across attempts, which is why step 1 is a prerequisite.

3. **Handle every terminal status on re-run**, not just `Answered`:
   - `Answered` → `$"Human answered via {RespondedChannel}: {Response}"`;
   - `Rejected` → throw `ConfirmationRejectedException`;
   - `Expired` → honour `ExpiresAction`: `Fail` → tool failure; `DefaultAnswer` → return the default;
     `Continue` → return "No answer was received; proceed without it.";
   - `Cancelled` → tool failure, "The request was cancelled.";
   - `Pending` → re-throw `AgentInteractionRequiredException` (existing behavior at `:64`).

4. **Add `channels` and `timeout_seconds` parameters** to `human.ask` and `human.notify`. Both optional
   — the tools' current signatures must keep working unchanged. `channels` is comma-separated (matching
   how `options` is already parsed at `ParseOptions`, `:90`). Compute `TimeoutAt` from
   `timeout_seconds` or `InteractionOptions.DefaultTimeoutSeconds`; null → no timeout.

5. **Add `HumanConfirmTool`** in the same file, `Name => "human.confirm"`,
   `Category => AgentToolCategories.Coordination`:
   - params: `question` (required), `channels`, `timeout_seconds`;
   - `Kind = Confirm`, `Blocking = true`, `Options = ["approve", "reject"]`;
   - same persist → route → throw flow;
   - on re-run: `Answered` + response `approve` → `$"Confirmed by {RespondedBy} via {RespondedChannel}."`;
     `Rejected` (or response `reject`) → throw `ConfirmationRejectedException`.

6. **Add `ConfirmationRejectedException`** next to `AgentInteractionRequiredException`, and handle it in
   `AgentOrchestrator` **exactly like `ToolAccessStepFailedException`** (`:363` and `:474`): return a
   failed `AgentTaskOutcome` with the rejection reason.
   **A rejection must not become a tool result.** Feeding "the human said no" back into the model loop
   invites the agent to rationalize and retry — which is precisely the safety boundary this epic
   exists to enforce. Failing the step is the point.

7. **Call `IInteractionRouter.RouteAsync`** after `SaveChangesAsync`, before throwing (006 step 5).
   Wrap in try/catch — a delivery failure must never turn into a tool failure.

8. **Treat responses as untrusted data.** When returning a human's free text to the model, keep the
   attributed, delimited form (`Human answered via slack: …`). Never interpolate a response into a
   system prompt. Cap length at the column's 8192.

9. **Register `HumanConfirmTool`** wherever `HumanAskTool` is registered; `AgentsDependencyInjectionTests`
   will need the new tool added to its expected set.

## Files

- `src/Agentwerke.Agents/Tools/ToolContracts.cs` (`:35` — add `NodeId`)
- `src/Agentwerke.Agents/Tools/HumanInteractionTools.cs`
- `src/Agentwerke.Agents/Tools/AgentInteractionRequiredException.cs` (or a sibling for the new exception)
- `src/Agentwerke.Agents/AgentOrchestrator.cs` (`:143`, `:278` — populate `NodeId`; `:351`, `:363` — exception handling)
- `src/Agentwerke.Agents/DependencyInjection.cs`
- `tests/Agentwerke.Agents.Tests/HumanInteractionToolsTests.cs`

## Acceptance criteria

- **AC 1:** blocking ask → suspend → answer → re-run returns the answer with its channel.
- **AC 2:** `human.confirm` rejected → step fails with the reason; the model loop is not re-entered.
- **AC 6:** `human.notify` → `posted`, no suspend, step completes.
- **Regression (the bug above):** a re-run whose model rephrases the question still finds the original
  interaction and does **not** create a second one. Write this test explicitly — it is the reason the
  lookup changed.
- Existing `HumanInteractionToolsTests` pass with only additive changes.
- A router failure does not fail the step.

## Verification

```bash
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Agents.Tests --no-build
```

## Out of scope

`agent.request` (009). Channel implementations (010–012).
