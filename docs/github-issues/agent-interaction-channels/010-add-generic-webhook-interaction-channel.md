# Add the generic webhook interaction channel (outbound + inbound)

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 006.
**Blocks:** 016.
**Layer:** backend / provider.

## Context

Build this **before** Slack: it is the reference implementation of `IInteractionChannel` with no
provider quirks, and it proves the full outbound→inbound→resume loop end to end. Slack (011) then only
has to translate payload shapes.

`WebhookSignatureValidator` (`src/Agentwerke.Integrations/Webhooks/WebhookSignatureValidator.cs`)
already does HMAC-SHA256 with `CryptographicOperations.FixedTimeEquals` and a `sha256=<hex>` header
convention, plus a ±5-minute timestamp window in `ValidateSlack`. Reuse it; do not write a second
implementation.

**Security issue to fix here.** `WebhookSignatureValidator.cs:22` returns `Ok()` when the secret is
empty — "secret not configured = skip validation". That is defensible for an inbound *trigger*. It is
not defensible for an endpoint that **resumes a parked run**: an unset secret would turn this into an
unauthenticated resume for anyone who can reach the API. The interaction endpoint must **fail closed**.
Do not change the shared validator's behavior (Jira/GitHub triggers depend on it) — enforce the secret
at the interaction endpoint instead.

## Objective

A signed outbound POST carrying the interaction, and a signed inbound endpoint that answers it.

## Implementation steps

1. **Add `WebhookInteractionChannel : IInteractionChannel`** in
   `src/Agentwerke.Integrations/Channels/`. `ChannelId => InteractionChannels.Webhook`;
   `CanCarryResponse => true`; `Enabled => _options.Webhook.Enabled`.

2. **Outbound payload** — POST JSON to the configured endpoint:
   ```json
   {
     "interactionId": "8f2c1a...", "runId": "run-42", "stepId": "review-step",
     "workflowName": "Deploy pipeline", "fromAgent": "reviewer-agent",
     "kind": "confirm", "blocking": true,
     "prompt": "Deploy build 1.4.2 to production?",
     "options": ["approve", "reject"],
     "createdAt": "2026-07-15T09:00:00.0000000+00:00",
     "timeoutAt": "2026-07-15T10:00:00.0000000+00:00",
     "respondUrl": "https://agentwerke.example/webhooks/interactions/response"
   }
   ```
   **Carry only these fields.** No run context, no artifacts, no tool output, no credentials — a
   channel payload leaves the trust boundary. Apply the existing redaction path and add a test that a
   secret-shaped value never appears in a payload.

3. **Sign the outbound request:** `X-Agentwerke-Signature: sha256=<hex>` over the **raw body bytes**,
   plus `X-Agentwerke-Timestamp: <unix>`. Reuse `ComputeHmacSha256Hex`. Resolve the secret through
   `ISecretStore`, as `SlackConnector.ResolveWebhookUrlAsync` does — never from appsettings directly.

4. **Return the right result:** 2xx → `Delivered` with any `id` from the response body; non-2xx or
   exception → `Failed(reason)`. Never throw — the router (006) records it and the run stays answerable
   in the UI.

5. **Add `POST /webhooks/interactions/response`** to `WebhooksController` (`[AllowAnonymous]`, like the
   other webhook actions — authentication *is* the signature). Body per 001's API Contracts:
   `interactionId`, `response`, `responder`, `nonce`, `timestamp`.

6. **Fail closed on the missing secret.** Before validating: if the configured secret is empty →
   `401` and log at `Error`. Do **not** call through to the shared validator's skip path. Better: refuse
   at startup — if `Interactions:Webhook:Enabled` is true and no secret resolves, throw during options
   validation so the misconfiguration is a boot failure, not a silent hole.

7. **Verify signature and timestamp:** HMAC over the raw body (read it with `ReadBodyAsync`, as the
   controller already does — **do not** re-serialize the parsed model, the bytes must be the ones that
   were signed), then reject timestamps outside ±5 minutes.

8. **Replay protection:** persist the nonce with a TTL and reject a repeat with `409`. A small
   dedicated table with a sweep is the default (see 001 Open Question 6). Note the distinction: a
   *replayed nonce* is `409` from the nonce store; a *duplicate response* with a fresh nonce is `409`
   from `TryTransitionAsync` (004). Both are correct; both must be tested.

9. **Route to the orchestration verb:** call `AnswerInteractionAsync` (or `RejectInteractionAsync` for
   a `reject` decision on a `confirm`) with `Channel = InteractionChannels.Webhook` and
   `AnsweredBy = $"webhook:{responder.id}"`. Map exceptions per 001's status table: not found → `404`,
   not pending → `409`, invalid choice → `400`.

10. **Add `WebhookOptions`** under `InteractionOptions`: `Enabled`, `Endpoint`, `Secret` (via
    `ISecretStore`), `TimeoutSeconds`.

## Files

- `src/Agentwerke.Integrations/Channels/WebhookInteractionChannel.cs` (new)
- `src/Agentwerke.Integrations/IntegrationOptions.cs`
- `src/Agentwerke.Api/Controllers/WebhooksController.cs`
- `tests/Agentwerke.Integrations.Tests/`, `tests/Agentwerke.Api.Tests/WebhooksControllerTests.cs`

## Acceptance criteria

- **AC 5:** blocking interaction → outbound POST → signed inbound response → accepted, resumed once,
  `RespondedChannel = webhook`.
- **AC 11:** invalid signature → `401`, no state change. Answer outside `options` → `400`.
- **AC 9:** replayed nonce → `409`, no second resume.
- **AC 14:** endpoint returns 500 → delivery row `failed` with attempts/error; interaction stays
  `pending`; retry succeeds once the endpoint recovers.
- **Fail-closed:** enabled with no secret → startup failure, or `401` on every inbound. Never `Ok()`.
- Outbound payload contains no redacted or context values.

## Verification

```bash
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Integrations.Tests --no-build
dotnet test tests/Agentwerke.Api.Tests --no-build
```

## Out of scope

Slack (011), Teams (012). E2E wiring (016).
