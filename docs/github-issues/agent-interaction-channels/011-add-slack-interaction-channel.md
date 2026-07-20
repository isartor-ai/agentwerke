# Add the Slack interaction channel without breaking Slack approvals

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 006, 010.
**Blocks:** 016.
**Layer:** backend / provider.

## Context

Slack already works — for approvals, and only for approvals:

- **Outbound:** `SlackConnector.SendNotificationAsync` takes
  `SendNotificationCommand(Title, Message, ApprovalId, RunId)` and renders Block Kit approve/reject
  buttons when `Notifications.Interactive` is on.
- **Inbound:** `WebhooksController.SlackInteractions` (`:132`) verifies the HMAC via `ValidateSlack`,
  extracts the URL-encoded `payload` form field, parses `actions[0].action_id` into `approve`/`reject`,
  splits `value` on `:` into `approvalId:runId`, and calls `ResumeRunAsync` with an `ApprovalId`.

That inbound endpoint is the single Slack callback URL. Interactions must share it. **The existing
approval flow must not change behavior** — `SlackInteractionTests` should pass unmodified when this
ticket is done. That is the acceptance bar.

## Objective

Deliver interactions to Slack and accept Slack responses, by dispatching on payload shape at the
existing endpoint.

## Implementation steps

1. **Add `SlackInteractionChannel : IInteractionChannel`** in
   `src/Agentwerke.Integrations/Channels/`. `ChannelId => InteractionChannels.Slack`;
   `CanCarryResponse => true`; `Enabled => _options.Slack.Enabled`.

2. **Render Block Kit** for the interaction. Context section: requesting agent, workflow, run, step,
   and the prompt. Then:
   - `choice`/`confirm` with options → one button per option;
   - `question` free-text → an "Answer" button opening a modal;
   - `notify` → text only, no controls.
   Same redaction rule as 010: prompt, options, and identifiers only.

3. **Namespace the action ids so approvals and interactions cannot collide.** The current parser keys
   on bare `approve`/`reject` (`:222`). Use `interaction_choice` / `interaction_answer` for the new
   buttons, and carry `value = $"{interactionId}:{optionIndex}"`. **Do not reuse `approve`/`reject`** —
   that is the collision that would break the approval path.

4. **Free-text via modal:** the "Answer" button opens a `views.open` modal carrying
   `private_metadata = interactionId`; the reply arrives as a `view_submission` payload rather than
   `block_actions`. Note this needs `views.open`, i.e. a bot token — not just the incoming webhook URL
   the connector uses today. **If only an incoming webhook is configured, free-text is unavailable**;
   the channel must degrade to structured choices and say so, rather than rendering a button that
   errors. (See 001 Open Question 4.)

5. **Dispatch by shape in `SlackInteractions`.** Keep the signature check first — it already runs
   before parsing, which is correct. Then branch on `payload.type`:
   - `block_actions` + `action_id` in `approve`/`reject` → **existing approval path, untouched**;
   - `block_actions` + `action_id` starting `interaction_` → the interaction path;
   - `view_submission` → the interaction path via `private_metadata`;
   - otherwise → today's `Ok(new { text = $"Ignored action '{action.ActionId}'." })`.
   Extract the interaction branch into a private method; leave `ParseSlackAction` alone.

6. **Call `AnswerInteractionAsync`/`RejectInteractionAsync`** with `Channel = InteractionChannels.Slack`
   and `AnsweredBy = $"slack:{user}"`, mirroring the approval path's `DecidedBy` convention (`:176`).

7. **Keep Slack's response convention.** Slack renders the response body, so the endpoint returns `200`
   with a message even on failure — see `:180`. **Do not** return 4xx for "already answered" here; that
   would show a Slack user a raw error. Return `200` with
   `{ replace_original: true, text: ":information_source: Already answered via ui." }`. The 409 in
   001's status table is for the API surface (013), not for Slack's callback.

8. **Update the message after a terminal transition** (`replace_original: true`) so the buttons are
   visibly spent — this is what prevents a second click, and it is AC 10's Slack-side behavior. Store
   the `ts` from the outbound post as `ChannelMessageId` so the update can target it.

9. **Reuse `SlackOptions`** — `Enabled`, `WebhookUrl`, `SigningSecret` already exist. Add `BotToken`
   (via `ISecretStore`) only if the modal path is in scope.

## Files

- `src/Agentwerke.Integrations/Channels/SlackInteractionChannel.cs` (new)
- `src/Agentwerke.Integrations/SlackConnector.cs` (extract Block Kit rendering if it helps; do not
  change `SendNotificationCommand`'s existing shape)
- `src/Agentwerke.Api/Controllers/WebhooksController.cs` (`:132`)
- `tests/Agentwerke.Integrations.Tests/SlackInteractionTests.cs`

## Acceptance criteria

- **AC 3:** interaction delivered to Slack → signed `block_actions` naming the interaction id →
  accepted, `RespondedChannel = slack`, resumed once.
- **Regression bar:** `SlackInteractionTests` passes **unmodified**; approval approve/reject is
  byte-for-byte unchanged.
- Approval and interaction payloads route to different paths and cannot collide on `action_id`.
- **AC 9:** replayed Slack payload → no second resume; the user sees "already answered", not an error.
- **AC 11:** bad signature → `401`, no state change, no parsing attempted.
- Incoming-webhook-only config → structured choices work, free-text degrades cleanly.

## Verification

```bash
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Integrations.Tests --no-build
dotnet test tests/Agentwerke.Api.Tests --no-build
```

**No real Slack credentials.** Point `SlackOptions.WebhookUrl` at WireMock and assert on its request
journal. A credentialed smoke test is documented separately (017).

## Out of scope

Teams (012). Mapping a Slack identity to an Agentwerke principal — **this is 001 Open Question 3 and a
real authorization gap**: today any workspace member who can see the message can answer a blocking
confirmation without holding `Approver`. v1 inherits the approval path's behavior. Resolve before
enabling Slack confirmations in production.
