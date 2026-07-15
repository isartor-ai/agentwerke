# Add the Teams outbound interaction channel (notify only)

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 006.
**Blocks:** 016.
**Layer:** backend / provider.

## Context

`TeamsConnector.SendNotificationAsync` posts `{ text = "**{Title}**\n\n{Message}" }` to a configured
incoming webhook URL. That is the whole Teams integration today — **outbound only, and structurally
so**: a Teams incoming webhook is one-way by design. There is no callback, no signature to verify, no
way for a Teams user's click to reach Agentwerke.

Making Teams answer interactions needs a different integration entirely — an Azure Bot Framework or
Graph app registration, Adaptive Card `Action.Execute`, and bot token validation. That was scoped out
on 2026-07-15 as its own workstream.

So this ticket delivers what is honestly deliverable: Teams **sees** interactions, and the system is
explicit that Teams cannot answer them.

## Objective

A Teams adapter implementing the same contract, which reports truthfully that it cannot carry a
response — so enabling inbound later is one adapter plus config, not a redesign.

## Implementation steps

1. **Add `TeamsInteractionChannel : IInteractionChannel`** in
   `src/Agentwerke.Integrations/Channels/`:
   ```csharp
   public string ChannelId => InteractionChannels.Teams;
   public bool Enabled => _options.Teams.Enabled;
   public bool CanCarryResponse => false;   // incoming webhooks are one-way; see 001 Open Question 1
   ```
   The comment matters — `false` here is a fact about Microsoft's incoming webhooks, not a TODO someone
   should "fix" by flipping the flag.

2. **Render an Adaptive Card** rather than the current bare `text`: requesting agent, workflow, run,
   step, prompt, options (as read-only text, **not** as `Action.Execute` buttons — a button that cannot
   submit is worse than no button), and a **deep link to the Agentwerke run details page** so a reader
   can answer in the UI in one click. That link is what makes an unanswerable notification useful.

3. **Handle the router's `NotSupported` path.** Per 006 step 3, the router records `NotSupported` for a
   blocking interaction on a channel with `CanCarryResponse == false`. Decide what Teams posts in that
   case, and post it: the card, with "Answer in Agentwerke" instead of options. The delivery row stays
   `not_supported` so the UI (015) can explain why Teams shows no controls.

4. **Non-blocking `notify` is fully supported** — `CanCarryResponse` is irrelevant when no response is
   wanted. Record `Delivered`, not `NotSupported`.

5. **Same redaction rule as 010/011:** prompt, options, identifiers, run link. Nothing else.

6. **Reuse `TeamsOptions`** (`Enabled`, `WebhookUrl` via `ISecretStore`) — no new options needed.
   Reuse `ResolveWebhookUrlAsync`'s pattern from the existing connector.

7. **Do not touch `ConnectorApprovalNotifier`.** Teams approvals keep flowing through it unchanged.

## Files

- `src/Agentwerke.Integrations/Channels/TeamsInteractionChannel.cs` (new)
- `src/Agentwerke.Integrations/TeamsConnector.cs` (reuse; extract card rendering if useful)
- `tests/Agentwerke.Integrations.Tests/`

## Acceptance criteria

- **AC 4 (v1 scope):** Teams enabled + blocking interaction routed → an outbound card is delivered, the
  delivery row is `not_supported`, and the interaction stays answerable via UI/Slack. The card links to
  the run.
- `human.notify` to Teams → delivery row `delivered`.
- Teams outage → `failed` delivery row; the agent step is unaffected; UI/Slack still answer.
- No `Action.Execute` buttons are rendered.

## Verification

```bash
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Integrations.Tests --no-build
```

**No real Teams credentials.** Point `TeamsOptions.WebhookUrl` at WireMock and assert on the request
journal.

## Out of scope

**Teams inbound responses** — needs an Azure Bot Framework/Graph app registration, `Action.Execute`,
and token validation. Tracked as 001 Open Question 1 and requires a follow-up ticket before Teams can
be called complete. Do not partially start it here.
