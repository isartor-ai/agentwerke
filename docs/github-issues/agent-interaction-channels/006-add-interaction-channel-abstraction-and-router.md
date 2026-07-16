# Add the provider-neutral channel abstraction and interaction router

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 004.
**Blocks:** 010, 011, 012.
**Layer:** backend / application.

## Context

`ConnectorApprovalNotifier` (`src/Agentwerke.Integrations/ConnectorApprovalNotifier.cs`) is the model
to copy: it implements `IApprovalNotifier` — an interface owned by
`Agentwerke.Application/Notifications` — and depends on `ISlackConnector`/`ITeamsConnector` from
`Agentwerke.Integrations`. The abstraction points inward; the providers stay outward. That is exactly
the shape this ticket needs, with two differences: delivery must be **tracked** (the notifier swallows
failures at best-effort, which is fine for a notification and not fine for a question a run is parked
on), and channel selection must be **resolved** rather than "every enabled connector".

## Objective

Define `IInteractionChannel` and the router that fans an interaction out to the resolved channel set,
with a delivery row per channel. No provider type may be named in `Domain`, `Agents`, or `Application`.

## Implementation steps

1. **Define the contract** in `src/Agentwerke.Application/Agents/IInteractionChannel.cs`:
   ```csharp
   public sealed record InteractionDeliveryRequest(
       AgentInteraction Interaction,
       string RunId,
       string? WorkflowName,
       string RespondUrl);

   public sealed record InteractionDeliveryResult(
       string Status,                 // InteractionDeliveryStatuses
       string? ChannelMessageId,
       string? Error)
   {
       public static InteractionDeliveryResult Delivered(string? id) => new(Delivered, id, null);
       public static InteractionDeliveryResult Failed(string error) => new(Failed, null, error);
       public static InteractionDeliveryResult NotSupported(string why) => new(NotSupported, null, why);
   }

   public interface IInteractionChannel
   {
       string ChannelId { get; }              // InteractionChannels.*
       bool Enabled { get; }
       bool CanCarryResponse { get; }         // false for Teams v1 (outbound-only)
       Task<InteractionDeliveryResult> DeliverAsync(
           InteractionDeliveryRequest request, CancellationToken cancellationToken);
   }
   ```
   `CanCarryResponse` is what lets Teams (012) be honest instead of silently dropping a blocking
   question into a channel nobody can answer from.

2. **Add `InteractionChannelResolver`** implementing the layered precedence:
   per-interaction `RequestedChannels` (set by the tool's `channels` arg) → per-workflow/per-agent
   config → `Integrations:Interactions:DefaultChannels`. `ui` is **always** in the resolved set and
   cannot be removed — the UI is the fallback that makes every other channel optional.
   Drop channels that are unregistered or disabled, and log at `Warning` when a requested channel is
   dropped (a silently ignored `channels: ["slack"]` is a bad debugging afternoon).

3. **Add `InteractionRouter`** in `src/Agentwerke.Application/Agents/InteractionRouter.cs`:
   ```csharp
   public interface IInteractionRouter
   {
       Task RouteAsync(AgentInteraction interaction, CancellationToken cancellationToken);
       Task<InteractionDeliveryResult> RetryAsync(
           string interactionId, string channel, CancellationToken cancellationToken);
   }
   ```
   `RouteAsync`:
   - resolves the channel set and persists a `pending` delivery row per channel **before** any call, so
     a crash mid-fan-out leaves a retryable record;
   - skips `ui` (the row itself is the UI's source of truth — no delivery needed, but **do** record a
     `delivered` row so the UI can show "available in: ui, slack" uniformly);
   - for each remaining channel: if `!CanCarryResponse && interaction.Blocking` → record
     `NotSupported` and continue; else `DeliverAsync` with retry;
   - **never throws into the caller.** Wrap every channel in try/catch, record `Failed` with the
     message. A Slack outage must not fail an agent step — the interaction stays `pending` and
     answerable in the UI.

4. **Retry with backoff:** 3 attempts, exponential with jitter, incrementing `Attempts` and writing
   `LastAttemptAt`/`LastError` each time. If every channel that `CanCarryResponse` fails and the
   interaction is blocking, log at `Error` and emit a metric — this is the case where a human will
   never see the question outside the UI.

5. **Call the router from the tools.** In `HumanAskTool`/`HumanNotifyTool` (008), call `RouteAsync`
   **after** `SaveChangesAsync` and **before** throwing `AgentInteractionRequiredException` — persist,
   then deliver, per the 001 flow. Ordering matters: delivering first risks a response arriving for a
   row that does not exist yet.

6. **Register in DI**: `IInteractionRouter` and `InteractionChannelResolver` scoped in
   `src/Agentwerke.Infrastructure/DependencyInjection.cs`; channel implementations are registered by
   their own tickets as `IEnumerable<IInteractionChannel>`, the same way `IEnumerable<IAgentTool>` is
   collected today.

7. **Add `InteractionOptions`** to `IntegrationOptions` (`Section = "Integrations"`):
   `Enabled` (default `false` — the epic's feature flag), `DefaultChannels` (default `["ui"]`),
   `DefaultTimeoutSeconds` (default `null` = never expires), `MaxDeliveryAttempts` (3).
   When `Enabled == false`, the resolver returns `["ui"]` only, reproducing today's behavior exactly.

## Files

- `src/Agentwerke.Application/Agents/IInteractionChannel.cs` (new)
- `src/Agentwerke.Application/Agents/InteractionRouter.cs` (new)
- `src/Agentwerke.Application/Agents/InteractionChannelResolver.cs` (new)
- `src/Agentwerke.Integrations/IntegrationOptions.cs` (extend)
- `src/Agentwerke.Infrastructure/DependencyInjection.cs`

## Acceptance criteria

- **AC 14 (partial):** a channel returning failure produces a `failed` delivery row with attempts and
  error; the interaction stays `pending`; the agent step is unaffected.
- A throwing channel never propagates out of `RouteAsync`.
- Resolver precedence: per-interaction beats config; `ui` is always present; disabled channels dropped
  with a warning.
- `Interactions:Enabled = false` → only `ui` is ever resolved.
- **Architecture test:** `Agentwerke.Application` has no reference to `Agentwerke.Integrations`, and no
  provider name appears in `Domain`/`Agents`/`Application` source. Add this as a test, not a review
  convention — it is the constraint most likely to erode.

## Verification

```bash
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Application.Tests --no-build
```

## Out of scope

Any concrete provider (010–012). The UI's rendering of delivery state (015).
