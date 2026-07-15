# Add non-blocking agent.request, depth counting, and cycle detection

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 002.
**Blocks:** 016.
**Layer:** backend / agent tools.

## Context

`AgentRequestTool` runs the callee **inline** as a nested `ModelRunRequest` through
`IAgentModelRunner`, then records request and reply under one `CorrelationId`. Per the 2026-07-15
decision, **the inline model stays** for blocking requests — it already works, it is already
correlated, and replacing it with an async mailbox is a much larger change for little pilot benefit.

Two things are wrong today:

1. **The run conversation misreports blocking delegations.** `RecordAsync` (`:161`) hard-codes
   `Blocking = false` and `Status = Posted` for *both* sides. A blocking delegation — which is the only
   kind that exists — is recorded as if it were fire-and-forget. The UI shows a lie.
2. **The depth guard is a hard-coded deny-list.** `BuildDelegatedRequest` (`:118`) sets
   `DeniedTools = [Name, "human.ask"]`, which enforces depth-1 by making delegation literally
   unavailable to the callee. It works, but it cannot express "depth 3", and it does not detect cycles
   beyond the self-delegation check at `:70`.

## Objective

Add a non-blocking mode, make the persisted rows truthful, and replace the deny-list with a real depth
counter plus cycle detection.

## Implementation steps

1. **Thread delegation state through the context.** Add to `AgentToolExecutionContext`
   (`src/Agentwerke.Agents/Tools/ToolContracts.cs:35`):
   ```csharp
   int DelegationDepth = 0,
   IReadOnlyList<string>? DelegationChain = null
   ```
   Both optional with defaults — every existing construction site keeps compiling. `BuildDelegatedRequest`
   propagates `DelegationDepth + 1` and `DelegationChain + [context.AgentName]`.

2. **Add the `blocking` parameter**, default `"true"` — today's behavior is the default, so no existing
   workflow changes meaning:
   ```csharp
   new("blocking", "string", "Whether to wait for the agent's reply ('true', default) or dispatch and continue ('false').", Required: false),
   ```

3. **Fix the recorded rows.** `RecordAsync` takes `blocking` and `status`:
   - blocking request row: `Blocking = true`, `Status = Pending`, `DelegationDepth` set;
   - reply row: `Status = Answered`, `Response = reply`, `RespondedBy = to`,
     `RespondedChannel = InteractionChannels.Agent`;
   - **also transition the request row to `Answered`** once the reply lands, so the conversation does
     not show a permanently pending request. Since the inline run completed, this is a direct update,
     not a `TryTransitionAsync` race.
   - non-blocking dispatch row: `Blocking = false`, `Status = Posted` (today's behavior, now correct
     for the mode it describes).

4. **Implement non-blocking mode.** `blocking=false` → persist the request row and return
   `$"Dispatched to {to}."` immediately, **without** running the callee. The request is a durable record
   another agent or a human can read; nothing schedules it in v1 (an async dispatcher is explicitly out
   of scope, per 001). Say so in the tool description so a model does not expect a reply:
   `"Dispatches the task and returns immediately; no reply is delivered back to you."`

5. **Add the depth guard.** Before dispatch, if `context.DelegationDepth >= MaxDelegationDepth`
   (config, default 3) → tool failure `$"Delegation depth limit ({max}) reached; '{to}' was not
   invoked."` Never throw — a failed tool result lets the caller adapt.

6. **Add cycle detection.** If `to` is already in `DelegationChain` (case-insensitive, matching the
   self-check at `:70`) → tool failure naming the cycle: `$"Delegation cycle detected: {chain} -> {to}."`
   This subsumes the existing self-delegation check; keep that check as the depth-0 case.

7. **Relax the deny-list now that depth is real.** `BuildDelegatedRequest` currently denies
   `[Name, "human.ask"]`. Replace with:
   - `agent.request` is **allowed** (depth/cycle guards now do the work);
   - `human.ask`/`human.confirm` stay **denied** for delegated callees. A nested inline sub-task cannot
     park the parent run — the parent is mid-tool-call and has no way to suspend correctly. Keep the
     deny and keep the existing comment explaining why.
   - Permissions stay `ReadOnly` — narrowed, never inherited-and-widened.

8. **Return callee failure as a tool failure**, as today (`:96`) — not an exception. Confirm the inline
   runner's exceptions are caught; a callee that throws must surface as `FailureReason`, not unwind the
   parent step.

9. **Cancellation** propagates through the existing `CancellationToken` into the nested run — verify,
   and add a test.

## Files

- `src/Agentwerke.Agents/Tools/AgentRequestTool.cs`
- `src/Agentwerke.Agents/Tools/ToolContracts.cs` (`:35`)
- `tests/Agentwerke.Agents.Tests/AgentRequestToolTests.cs`

## Acceptance criteria

- **AC 7:** blocking request → request and reply share a `CorrelationId`; the request row is
  `Blocking = true` and ends `Answered`; the caller's step completes with the reply.
- **AC 8:** `blocking=false` → the caller does not suspend, the row is `posted`, the callee is not run.
- Depth cap → tool failure, callee not invoked.
- A → B → A → tool failure naming the cycle.
- Callee failure → `Succeeded: false` with the reason; no unhandled exception.
- Cancelling the parent propagates into the nested run.
- Existing `AgentRequestToolTests` pass with only additive changes.

## Verification

```bash
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Agents.Tests --no-build
```

## Out of scope

An async agent mailbox or dispatcher. Delivering non-blocking requests to the target agent's context.
Both are deliberate deferrals — see 001 Out of Scope.
