# Extend the interaction domain vocabulary for channels, confirmation, and delivery

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** nothing — this is the first task.
**Blocks:** 003, 004, 005, 006, 007, 008, 009.
**Layer:** backend / domain.

## Context

`AgentInteraction` (`src/Agentwerke.Domain/Persistence/AgentInteraction.cs`) is already the unified
primitive and covers `post`, `question`, `choice`, `notify`, `agent_request`, `approval`, and
`tool_access`. It is missing the vocabulary for confirmation, rejection, cancellation, timeout, channel
routing, and delivery tracking.

Note `AgentInteractionStatuses.Expired` is declared today but **never assigned anywhere in `src/`** —
it is vocabulary without a writer. Ticket 007 adds the writer; this ticket adds the rest of the words.

## Objective

Add the domain types and constants every later task depends on. Pure domain — no EF, no behavior.

## Implementation steps

1. **Add the `Confirm` kind** to `AgentInteractionKinds`:
   ```csharp
   /// <summary>
   /// A confirmation boundary: the agent must not proceed without an explicit approve/reject.
   /// Unlike question/choice, a rejection fails the step rather than feeding text back to the model.
   /// </summary>
   public const string Confirm = "confirm";
   ```
   Leave `Approval` declared and unused — approvals stay in their own table (see 001 Out of Scope).

2. **Add terminal statuses** to `AgentInteractionStatuses`:
   ```csharp
   /// <summary>A confirmation the responder explicitly declined. The step fails.</summary>
   public const string Rejected = "rejected";

   /// <summary>Withdrawn by the originating agent, an operator, or a cancelled run.</summary>
   public const string Cancelled = "cancelled";
   ```
   Add an XML-doc note on `Expired` naming `InteractionTimeoutSweeper` as its writer (007).

3. **Add `InteractionChannels`** in the same file:
   ```csharp
   public static class InteractionChannels
   {
       public const string Ui = "ui";
       public const string Slack = "slack";
       public const string Teams = "teams";
       public const string Webhook = "webhook";
       public const string Agent = "agent";
   }
   ```

4. **Add `InteractionExpiryActions`**: `Fail`, `Continue`, `DefaultAnswer`.

5. **Add fields to `AgentInteraction`.** Every timestamp is `string` (ISO-8601 `"o"`) — match the
   existing `CreatedAt`/`RespondedAt` convention; do **not** introduce `DateTimeOffset` on this entity
   alone.
   ```csharp
   public List<string> RequestedChannels { get; set; } = new();
   public string? RespondedChannel { get; set; }
   public string? TimeoutAt { get; set; }               // null = never expires
   public string? ExpiresAction { get; set; }
   public string? DefaultAnswer { get; set; }
   public string? CancelledAt { get; set; }
   public string? CancelledBy { get; set; }
   public string? ResumedAt { get; set; }
   public int DelegationDepth { get; set; }
   public int Version { get; set; }                     // optimistic concurrency token (003)
   ```

6. **Add `InteractionDelivery`** in a new file
   `src/Agentwerke.Domain/Persistence/InteractionDelivery.cs`: `Id`, `InteractionId`, `Channel`,
   `Status`, `ChannelMessageId`, `Attempts`, `LastAttemptAt`, `LastError`, `CreatedAt` — plus an
   `InteractionDeliveryStatuses` static class (`Pending`, `Delivered`, `Failed`, `NotSupported`).
   Document `NotSupported` as "the channel cannot carry a response for this interaction kind (e.g.
   Teams outbound-only in v1)".

7. **Add a status helper** so later tickets do not re-derive terminality:
   ```csharp
   public static bool IsTerminal(string status) =>
       status is Answered or Rejected or Expired or Cancelled or Posted;
   ```

## Files

- `src/Agentwerke.Domain/Persistence/AgentInteraction.cs` (extend)
- `src/Agentwerke.Domain/Persistence/InteractionDelivery.cs` (new)
- `tests/Agentwerke.Domain.Tests/` (new test file)

## Acceptance criteria

- `dotnet build Agentwerke.sln` succeeds; no existing call site changes (all new fields are optional
  or defaulted).
- `IsTerminal` returns `true` for answered/rejected/expired/cancelled/posted and `false` for pending.
- `RequestedChannels` defaults to an empty list, so rows created before this change stay valid.

## Verification

```bash
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Domain.Tests --no-build
```

## Out of scope

EF mapping and migration (003). Any behavior that reads these fields.
