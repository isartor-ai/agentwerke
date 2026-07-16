# Interaction Channels: Configuration, API, and Operations

Version: Draft v0.1
Status: Active
Date: 2026-07-16

This guide is the operator reference for routing agent questions and notifications to the Agentwerke
UI, a generic webhook, Slack, and Microsoft Teams. For a command-by-command exercise, see
[manual-test-interactions.md](manual-test-interactions.md).

## Delivery and response model

The UI is always enabled and cannot be configured away. When external delivery is enabled, the
resolver chooses the first non-empty layer in this order:

1. `channels` requested by the interaction;
2. `ChannelsByWorkflow[workflow name]`;
3. `ChannelsByAgent[requesting agent]`;
4. `DefaultChannels`.

It then adds the UI, removes duplicates, and drops unknown or disabled adapters with a warning. The
router fans out to every resulting external channel. One `interaction_deliveries` row per channel
records `pending`, `delivered`, `failed`, or `not_supported`, the attempt count, provider message id,
last attempt, and provider error.

For blocking interactions, the first valid response wins. A simultaneous or later API/webhook
response receives a conflict and cannot enqueue another run resume. Notifications and posts do not
park or resume a run.

## Common configuration

Configuration binds from `Integrations:Interactions`; environment variables use .NET's double-
underscore form.

```json
{
  "Integrations": {
    "Interactions": {
      "Enabled": true,
      "DefaultChannels": ["ui", "webhook"],
      "ChannelsByWorkflow": {
        "Production deployment": ["ui", "slack"]
      },
      "ChannelsByAgent": {
        "security-reviewer": ["ui", "slack", "webhook"]
      },
      "DefaultTimeoutSeconds": null,
      "SweepIntervalSeconds": 30,
      "MaxDeliveryAttempts": 3,
      "RetryBaseDelayMs": 200,
      "RespondUrlBase": "https://agentwerke.example"
    }
  }
}
```

Equivalent minimal environment configuration:

```bash
Integrations__Interactions__Enabled=true
Integrations__Interactions__DefaultChannels__0=ui
Integrations__Interactions__DefaultChannels__1=webhook
Integrations__Interactions__RespondUrlBase=https://agentwerke.example
```

`DefaultTimeoutSeconds` deliberately defaults to null: upgrading or enabling channel fan-out does not
start expiring runs that previously waited indefinitely. Set a finite default only after choosing the
appropriate expiry policy for each interaction (`fail`, `continue`, or `default_answer`).

Secrets are resolved through `ISecretStore`. In production, store provider secrets in the configured
secret backend and put only its reference in configuration. Do not commit raw signing secrets,
webhook URLs, or bot tokens.

## Provider setup and limitations

### Generic webhook

```bash
Integrations__InteractionWebhook__Enabled=true
Integrations__InteractionWebhook__Endpoint=https://automation.example/agentwerke/interactions
Integrations__InteractionWebhook__Secret=secret-store-reference
Integrations__InteractionWebhook__TimeoutSeconds=10
Integrations__InteractionWebhook__ToleranceSeconds=300
```

The adapter is response-capable. It refuses to send an unsigned request, and the inbound response
endpoint fails closed when its secret cannot be resolved.

### Slack

Create a Slack app, enable incoming webhooks, and configure its interactivity request URL as:

```text
https://agentwerke.example/webhooks/slack/interactions
```

Then configure:

```bash
Integrations__Slack__Enabled=true
Integrations__Slack__WebhookUrl=secret-store-reference
Integrations__Slack__SigningSecret=secret-store-reference
# Optional: required only for free-text answer modals
Integrations__Slack__BotToken=secret-store-reference
Integrations__Slack__ToleranceSeconds=300
```

Structured `choice` and `confirm` interactions use buttons. Free-text questions require a Slack bot
token and `views.open`; an incoming webhook alone cannot open the answer modal. Slack interaction
actions use namespaced action ids so the existing approval buttons keep their behavior.

Operational warning: do not put `slack` in interaction channel selection on a deployed revision that
does not contain `SlackInteractionChannel`; the resolver will log and drop it. The adapter, callback
dispatch, and their provider/API tests are committed on `feat/agent-interactions-224`.

### Microsoft Teams

```bash
Integrations__Teams__Enabled=true
Integrations__Teams__WebhookUrl=secret-store-reference
```

Teams incoming webhooks are outbound-only in v1. A card can display the agent, workflow, run, step,
prompt, options, and a link back to Agentwerke, but it cannot authenticate an answer. Blocking Teams
deliveries are recorded as `not_supported`; answer in the Agentwerke UI, Slack, or the generic
webhook. Supporting replies requires an Azure Bot/Graph application and token validation, not a
configuration switch.

## Generic webhook contract

### Outbound request

Agentwerke sends `POST application/json` with this bounded payload; no run context, artifacts, tool
output, or credentials leave the service:

```json
{
  "interactionId": "int-42",
  "runId": "run-42",
  "stepId": "review-step",
  "workflowName": "Production deployment",
  "fromAgent": "reviewer-agent",
  "kind": "confirm",
  "blocking": true,
  "prompt": "Deploy build 1.4.2 to production?",
  "options": ["approve", "reject"],
  "createdAt": "2026-07-16T09:00:00.0000000+00:00",
  "timeoutAt": "2026-07-16T10:00:00.0000000+00:00",
  "respondUrl": "https://agentwerke.example/runs/run-42"
}
```

Headers:

```text
X-Agentwerke-Timestamp: <Unix seconds>
X-Agentwerke-Signature: sha256=<lowercase hex HMAC-SHA256>
```

The signed bytes are UTF-8 `timestamp + "." + raw_request_body`. Verify the exact bytes received;
do not parse and re-serialize JSON before verification.

### Inbound response

Send the same signature headers to `POST /webhooks/interactions/response` with:

```json
{
  "interactionId": "int-42",
  "response": "Approved after change review",
  "decision": "approve",
  "responder": { "id": "ops-7", "displayName": "Dana" },
  "nonce": "d0cf2075-ef6a-49bd-8584-ce98e6f03d5d",
  "timestamp": "2026-07-16T09:04:00.0000000+00:00"
}
```

`decision: "reject"` calls the rejection verb; the response becomes the step failure reason. The HMAC
timestamp must be within the configured tolerance (300 seconds by default). A persistent nonce store
must reject reuse with `409`; a fresh nonce submitted after another channel won also receives `409`
from the single-winner transition. The nonce property is present in the current payload, but persistent
nonce replay storage from #224 is not yet implemented on this branch—do not expose the endpoint to an
untrusted network until that acceptance criterion lands.

The interaction response endpoint deliberately fails closed without a secret and returns `401`. Jira
and GitHub trigger validation deliberately skips signature validation when their trigger secret is
empty for development compatibility. That asymmetry is intentional: a forged trigger may start an
unwanted run; a forged interaction response would decide and resume an already parked run.

### Worked signing example

This example is deterministic and uses no trailing newline in the body:

```bash
SECRET='correct horse battery staple'
TIMESTAMP='1784188800'
BODY='{"interactionId":"int-42","response":"approve","responder":{"id":"ops-7"},"nonce":"nonce-42"}'
SIGNATURE="sha256=$(printf '%s.%s' "$TIMESTAMP" "$BODY" \
  | openssl dgst -sha256 -hmac "$SECRET" -binary \
  | xxd -p -c 256)"
test "$SIGNATURE" = 'sha256=b90beec99da410084a8c7f1be8220333197b21c12b961dd585323156130a516a'
```

For a live response, replace `TIMESTAMP` with `$(date +%s)`, keep `BODY` byte-for-byte identical, and
send:

```bash
curl -i -X POST "$API/webhooks/interactions/response" \
  -H 'Content-Type: application/json' \
  -H "X-Agentwerke-Timestamp: $TIMESTAMP" \
  -H "X-Agentwerke-Signature: $SIGNATURE" \
  --data-binary "$BODY"
```

## API reference

Bearer-authenticated endpoints use `{ "message": "..." }` errors. Webhook endpoints use
`{ "error": "..." }` because the signature is their authentication boundary.

| Method and route | Policy | Purpose |
| --- | --- | --- |
| `GET /api/runs/{runId}/interactions` | Viewer | Full run conversation with delivery rows |
| `POST /api/runs/{runId}/interactions/{id}/answer` | Approver | Answer a question/choice/confirmation |
| `POST /api/runs/{runId}/interactions/{id}/reject` | Approver | Reject a confirmation with a reason |
| `GET /api/interactions?status=pending&addresseeType=human&runId=` | Viewer | Cross-run pending inbox |
| `POST /api/interactions/{id}/cancel` | Approver | Cancel a pending interaction with a reason |
| `POST /api/interactions/{id}/deliveries/{channel}/retry` | Operator | Retry one delivery |
| `POST /webhooks/interactions/response` | Signed webhook | Generic webhook answer/rejection |
| `POST /webhooks/slack/interactions` | Slack signature | Slack approval and interaction callback |

| Condition | API status | Meaning |
| --- | --- | --- |
| Accepted answer/reject/cancel | `202` | Transition won; blocking run resume was enqueued |
| Retry completed | `200` | Provider result returned and delivery row updated |
| Missing interaction | `404` | Id is unknown or does not belong to the route's run |
| Already answered/rejected/expired/cancelled | `409` | Another terminal transition already won |
| Missing/invalid answer or option | `400` | Correct the request; valid options are named |
| Run no longer accepts responses | `422` | The run is terminal or missing |
| Missing/invalid/stale webhook signature | `401` | Request did not cross the signature boundary |

Slack deliberately returns `200` with a human-readable message when a click loses the race. Slack
renders the response body to the user; returning the API's `409` would expose a raw provider error.

## Operational runbook

### Find and retry a failed delivery

1. Open **Approvals → Pending interactions** or the run's **Conversation** tab.
2. Inspect channel, attempts, last attempt, and `LastError`.
3. Correct the provider endpoint/secret or recover the provider.
4. As an Operator, click **Retry**, or call the retry endpoint above.
5. Confirm the row becomes `delivered` (or `not_supported` for a blocking Teams card).

Retrying a terminal interaction is refused so operators do not post a message that can no longer be
acted upon.

### Metrics, logs, and audit

Scrape `/metrics` from the `Agentwerke.Workflows` meter. The implemented counter is:

```text
workflow.interactions.transitions{to.status,channel,won}
```

`won=false` counts duplicate, replayed, and racing terminal transitions. Some race loss is normal
when one interaction is fanned out to multiple response-capable channels. A spike without fan-out
usually means a client is retrying callbacks, users are double-submitting, or a captured payload is
being replayed; correlate the structured `Interaction transition lost` log by interaction/run id.

Audit actions currently emitted for terminal decisions are `interaction.answer`,
`interaction.reject`, `interaction.cancel`, and `interaction.expire`, with actor, channel, outcome,
correlation id, and timestamp. Delivery attempts are diagnosed from `interaction_deliveries` and
router logs. The broader `interaction.create`, `interaction.deliver`, and
`interaction.delivery_failed` audit set proposed by #215 remains part of the #230 evidence gate and
must not be assumed present until its tests land.

### Timeout tuning

- Keep `DefaultTimeoutSeconds=null` for upgrade-safe, never-expire behavior.
- Prefer a per-interaction timeout when the business boundary has a real deadline.
- Keep `SweepIntervalSeconds` materially below the shortest configured timeout.
- Use `fail` for safety boundaries, `continue` only where omission is harmless, and
  `default_answer` only when the default is explicitly approved and valid for the declared options.
- A large pending age with null timeout is not a sweeper failure; it is an interaction configured to
  wait indefinitely.

## Credentialed Slack smoke test (manual, never CI)

1. Create a private Slack app and enable incoming webhooks for a non-production channel.
2. Set the interactivity callback URL to
   `https://<public-agentwerke-host>/webhooks/slack/interactions`.
3. Store the incoming webhook URL and app signing secret in `ISecretStore`; configure the references
   shown above. Add a bot token only if testing free-text modal answers.
4. Route a `choice` or `confirm` interaction to Slack. Confirm the message names the agent, workflow,
   run, and step and contains namespaced interaction buttons.
5. Click one option. Confirm the message becomes spent, the run conversation shows
   `RespondedChannel=slack` and `RespondedBy=slack:<user>`, and the step resumes once.
6. Click the spent action again or race it with the UI. Confirm Slack shows an informational
   "already answered" message rather than a raw `409`.
7. Repeat with an invalid signing secret and confirm the callback returns `401` with no state change.

This test is intentionally manual: real Slack credentials and a public callback URL must never be
placed in CI. CI uses WireMock and signed fixture payloads instead.
