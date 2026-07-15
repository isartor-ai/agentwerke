# Route agent clarification, confirmation, and collaboration across UI, chat channels, and agents

## Summary

When an Agentwerke agent cannot safely continue — because information, intent, permission, or a
confirmation boundary is unclear — it can raise a single interaction that reaches a human in the
Agentwerke UI **and** in a configured external channel (Slack, Microsoft Teams, generic webhook), or
reaches another agent. The interaction may be non-blocking (notify and continue), blocking (suspend
until answered), or confirmation-required (suspend until approved, rejected, or redirected). Exactly
one valid response wins, the step resumes exactly once, and the whole exchange survives process
restart and stays visible in run history and the audit trail.

## Problem

The interaction primitive already exists and the UI path already works end to end. What is missing is
everything around it:

1. **The interaction never leaves the UI.** `HumanAskTool` persists an `AgentInteraction` and the run
   parks in `waiting_user`, but nothing delivers it anywhere. `ConnectorApprovalNotifier` fans out to
   Slack/Teams only for **approvals** (`IApprovalNotifier`), a separate table and a separate path.
   An agent that asks a question is invisible unless a human happens to be looking at the run.
2. **Slack inbound is hard-wired to approvals.** `WebhooksController.SlackInteractions` verifies an
   HMAC signature correctly, then parses `approve`/`reject` action ids and calls `ResumeRunAsync` with
   an `ApprovalId`. It cannot answer an `AgentInteraction`. Teams has no inbound path at all, and there
   is no generic inbound response endpoint.
3. **No single-winner mechanism.** `AnswerInteractionAsync` does a read → check `Pending` → write with
   no concurrency token and no `RowVersion` anywhere in the model. Two responders (UI + Slack) racing
   can both observe `Pending` and both enqueue an outbox `Resume`, resuming the run twice.
4. **No timeout.** `AgentInteractionStatuses.Expired` is declared and **never assigned anywhere in
   `src/`**. A blocking question parks the run forever.
5. **Missing terminal states.** There is no `rejected` and no `cancelled`. A confirmation can only be
   "answered" with free text; rejection is not modelled, and neither the initiating agent nor an
   operator can withdraw a pending request.
6. **No delivery tracking.** No channel, no delivery id, no attempt count, no failure reason. A
   Slack post that 500s is silently lost — `ConnectorApprovalNotifier` swallows failures by design.
7. **Agent-to-agent has only one shape.** `AgentRequestTool` always runs the callee inline and always
   records both sides as `Status = Posted`, `Blocking = false`. There is no non-blocking dispatch, no
   depth counter (the guard is a hard-coded depth-1 deny-list), and no cycle detection beyond a
   self-delegation check.

## User Stories

- **As a human responder**, I want an agent's question to reach me in Slack with enough context to
  decide, so I do not have to be watching the run board.
- **As a human responder**, I want to answer in the UI or in Slack and have either work, with the
  other surface immediately showing the question is settled.
- **As an operator**, I want to see which interactions are pending, how old they are, when they
  expire, and whether delivery to a channel failed — and retry that delivery.
- **As an operator**, I want a rejected confirmation to fail the step with the rejection reason
  rather than feeding "no" back into a model loop that argues with me.
- **As a workflow author**, I want to configure which channel a workflow's questions go to without
  editing agent prompts or C# code.
- **As a requesting agent**, I want to ask another agent for information and either wait for the
  answer or fire and continue, without knowing how that agent is scheduled.
- **As a responding agent**, I want the request, my reply, and their correlation persisted so the
  delegation is auditable.
- **As a compliance reviewer**, I want the request, the accepted response, the responder identity, the
  winning channel, and every state transition in the audit trail.

## Repository Findings

| Area | Existing implementation | Reusable | Gap | Files / symbols |
|---|---|---|---|---|
| Interaction entity | `AgentInteraction` with `Kind`, `AddresseeType`, `Addressee`, `Blocking`, `Options`, `CorrelationId`, `Status`, `Response`, `RespondedBy`, `RespondedAt` | **Yes — extend, do not replace** | No channel, delivery id, attempts, timeout, cancel/reject, concurrency token; `RespondedAt`/`CreatedAt` are `string` (ISO-8601) | `src/Agentwerke.Domain/Persistence/AgentInteraction.cs` |
| Interaction kinds | `post`, `question`, `choice`, `notify`, `agent_request`, `approval`, `tool_access` | **Yes — covers every kind the spec asks for** | `approval` is declared but approvals still live in their own table; `confirm` maps onto `choice`/`approval` and needs a decision | `AgentInteractionKinds` |
| Statuses | `pending`, `answered`, `posted`, `expired` | Yes | `expired` never assigned; no `rejected`, no `cancelled`, no `failed_delivery` | `AgentInteractionStatuses` |
| Blocking suspend | `HumanAskTool` throws `AgentInteractionRequiredException`; `ToolGateway` re-throws; `AgentOrchestrator` returns `StepStatus = WaitingUser` | **Yes — the whole suspend path works** | Only `human.ask` and tool-access escalation use it; no confirm, no agent request | `src/Agentwerke.Agents/Tools/AgentInteractionRequiredException.cs`, `AgentOrchestrator.cs:351`, `AgentOrchestrator.cs:463` |
| Resume | `AnswerInteractionAsync` marks answered, audits, enqueues outbox `Resume`; step re-runs and `HumanAskTool` finds the answer by run + kind + prompt match | **Yes — re-run strategy works and avoids busy-waiting** | No concurrency token; no channel attribution; prompt-text matching is fragile (see Scope) | `src/Agentwerke.Application/Workflows/WorkflowRunOrchestrationService.cs:199` |
| Non-blocking notify | `HumanNotifyTool` persists `notify` / `Posted`, returns immediately | **Yes — already correct** | Never delivered to any channel | `src/Agentwerke.Agents/Tools/HumanInteractionTools.cs:107` |
| Agent-to-agent | `AgentRequestTool` runs callee inline as nested `ModelRunRequest`, records request+reply under one `CorrelationId` | **Yes for blocking** | Always inline, always `Posted`/non-blocking rows; depth-1 via hard-coded `DeniedTools = [Name, "human.ask"]`; no depth counter, no cycle detection | `src/Agentwerke.Agents/Tools/AgentRequestTool.cs` |
| Coordination bus | `PersistentAgentCoordinationChannel` posts `post` interactions | Yes — unchanged | — | `src/Agentwerke.Agents/Coordination/AgentCoordinationChannel.cs` |
| Slack outbound | `SlackConnector.SendNotificationAsync`, Block Kit approve/reject buttons when `Notifications.Interactive` | **Yes — extend** | Approval-shaped only; `ISlackConnector` takes `SendNotificationCommand(Title, Message, ApprovalId, RunId)` | `src/Agentwerke.Integrations/SlackConnector.cs` |
| Slack inbound | `POST webhooks/slack/interactions`, HMAC verified, parses `approve`/`reject`, calls `ResumeRunAsync` | **Yes — signature verification and parsing** | Routes to approvals only; cannot answer an interaction; no free-text (`view_submission`) support | `src/Agentwerke.Api/Controllers/WebhooksController.cs:132` |
| Teams | `TeamsConnector.SendNotificationAsync` posts `{ text }` to an incoming webhook | Outbound shell only | **No inbound path, no interactive controls** — incoming webhooks are one-way | `src/Agentwerke.Integrations/TeamsConnector.cs` |
| Generic webhook | `WebhookSignatureValidator` (`ValidateGitHub`, `ValidateJira`, `ValidateSlack`), HMAC-SHA256, `FixedTimeEquals` | **Yes — reuse the validator** | No generic outbound interaction post; no generic inbound response endpoint; empty secret **skips validation** (`WebhookSignatureValidator.cs:22`) | `src/Agentwerke.Integrations/Webhooks/WebhookSignatureValidator.cs` |
| Fan-out | `ConnectorApprovalNotifier` fans approvals to enabled connectors, swallows failures | **Yes — the pattern** | Approval-only; failures invisible and unretryable | `src/Agentwerke.Integrations/ConnectorApprovalNotifier.cs` |
| Correlation store | `WaitingExternalCorrelation` — one row per waiting run, upserted on `waiting_external`, matched by inbound webhooks | **Yes — the pattern to copy for channel delivery ids** | Keyed to `waiting_external` BPMN gates, not interactions | `src/Agentwerke.Domain/Persistence/WaitingExternalCorrelation.cs` |
| API | `GET /runs/{runId}/interactions`; `POST /runs/{runId}/interactions/{id}/answer` (`[Authorize(Policy = Approver)]`) | **Yes — extend** | No cross-run pending list, no cancel, no reject verb, no channel in the summary | `src/Agentwerke.Api/Controllers/RunsController.cs:366`, `:379` |
| Auth | `AgentwerkePolicies.Viewer/Operator/Approver/Admin`; `AuthenticatedPrincipal.ResolveSubject(User)` | **Yes** | External responder identity is unmapped (`slack:username` string today) | `src/Agentwerke.Api/Auth/AgentwerkeRoles.cs` |
| Persistence | `agent_interactions` table, `HasIndex(RunId)`, `Options` as `jsonb` | **Yes** | No index on `Status`; no concurrency token; two prior migrations to follow | `AgentwerkeDbContext.cs:160`, `Migrations/20260702132231_AddAgentInteractions.cs`, `20260713070316_AddToolAccessContext.cs` |
| UI | `ConversationTab.tsx` renders the thread with an inline answer box; `ApprovalsDashboard.tsx` is the decision inbox; `ToolAccessRequests.tsx` | **Yes — extend both** | No pending interactions in the inbox; no channel/timeout/age display; no live update when another channel answers | `web/src/components/ConversationTab.tsx`, `web/src/views/ApprovalsDashboard.tsx`, `web/src/views/RunDetail.tsx` |
| Tests | `HumanInteractionToolsTests`, `AgentRequestToolTests`, `ToolAccessEscalationTests`, `InMemoryInteractionRepository`, `SlackInteractionTests`, `WebhooksControllerTests`, `runDetail.integration.test.tsx` | **Yes — extend all** | No channel, race, timeout, or restart coverage | `tests/` |

## Scope

Decisions confirmed with the product owner on 2026-07-15 are folded in below and are **not** open.

- Extend `AgentInteraction` with channel routing, delivery tracking, timeout, and a concurrency token.
- Add `confirm`, `rejected`, `cancelled`, and `failed_delivery` to the kind/status vocabulary; assign
  `expired` from a real timeout sweeper.
- Add an `IInteractionChannel` provider-neutral abstraction in `Agentwerke.Application` with an
  `InteractionRouter` that fans out to selected channels.
- **v1 providers: UI (always), generic webhook, Slack (in + out).** Teams is **outbound notify only**
  in v1; Teams inbound is explicitly deferred (needs a Bot Framework/Graph app registration, which is
  its own workstream). The Teams adapter must implement the same `IInteractionChannel` contract and
  return a "not supported" delivery result for inbound-requiring interactions, so enabling it later is
  configuration plus one adapter, not a redesign.
- Channel selection is layered: `IntegrationOptions` default → per-workflow/per-agent config → optional
  per-interaction `channels` argument. Fan-out to all selected channels; **first valid terminal
  response wins** via an optimistic-concurrency token; losers receive `409`.
- Blocking `agent.request` keeps today's inline nested run. Add a **non-blocking** mode that persists
  the request and returns immediately. Add an explicit depth counter and cycle detection to replace
  the hard-coded deny-list.
- Add `POST /interactions/{id}/cancel` and a cross-run `GET /interactions?status=pending`.
- Add a timeout sweeper as a hosted service that moves due `pending` rows to `expired` and resumes the
  step with an expiry result.
- Surface pending interactions in `ApprovalsDashboard` and enrich `ConversationTab`.
- **Replace prompt-text matching for re-run answer lookup.** `HumanAskTool` currently matches on
  `Prompt` string equality (`HumanInteractionTools.cs:47-51`); a model that rephrases its question on
  re-run asks twice. Carry the interaction id in run context instead.

## Out of Scope

- Migrating the existing `approvals` table into `agent_interactions`. `AgentInteractionKinds.Approval`
  stays declared-but-unused; the approval path (`ApprovalsController`, `ConnectorApprovalNotifier`,
  Slack approve/reject) is untouched. Consolidation is a follow-up.
- Teams **inbound** responses and Adaptive Card `Action.Execute`.
- A tenant model. The repo has none; `tenant isolation` in this ticket means run/workflow-scoped
  authorization via existing policies, nothing more.
- Threaded multi-turn conversations with a human. One interaction, one response.
- Email/SMS/PagerDuty channels.
- Changing the outbox or the step re-run resume strategy.
- Replacing the inline nested-run execution model for blocking `agent.request`.

## Proposed Behavior

### Lifecycle

```
                      ┌──────────► posted        (non-blocking: notify, dispatch)
                      │
created ──► pending ──┼──────────► answered      (first valid response wins)
   │                  ├──────────► rejected      (confirm declined)
   │                  ├──────────► expired       (timeout sweeper)
   │                  └──────────► cancelled     (agent, operator, or run cancelled)
   │
   └── delivery ─────────────────► failed_delivery (all channels failed; still pending, retryable)
```

`posted`, `answered`, `rejected`, `expired`, `cancelled` are **terminal**. `failed_delivery` is a
delivery annotation on a still-`pending` row, not a terminal state — it exists so the UI can show and
retry it.

### Flow

1. Agent detects uncertainty or a confirmation boundary and calls `human.ask` / `human.confirm` /
   `human.notify` / `agent.request`.
2. The tool persists the `AgentInteraction` **before any delivery attempt** (already true today) and
   resolves the channel set from the layered config.
3. Blocking: the tool throws `AgentInteractionRequiredException`; `AgentOrchestrator` returns
   `WaitingUser`; the run parks. **No thread is held and nothing polls** — this is the existing,
   working mechanism.
4. `InteractionRouter` delivers to the UI (implicit — the row is the UI's source of truth) and to each
   selected external channel, recording a delivery row per channel with the provider's message id.
5. A responder answers via the UI API or an authenticated inbound channel callback.
6. The application layer accepts the **first** valid terminal response under an optimistic-concurrency
   update. The winner's channel is recorded on the row.
7. Duplicate or late responses observe a non-`pending` status and are rejected with `409`; the outbox
   `Resume` is enqueued exactly once, by the winning transaction only.
8. The step re-runs; the tool finds the terminal interaction by id and returns structured context to
   the model.
9. Every transition is audited.

Non-blocking interactions skip steps 3 and 5–8: they persist as `posted`, deliver best-effort, and the
agent continues.

## Architecture and Component Changes

### Domain / persistence — `src/Agentwerke.Domain/Persistence/`

- Extend `AgentInteraction` (fields in the next section).
- New `InteractionDelivery` entity: one row per interaction per channel.
- Extend `AgentInteractionKinds` with `Confirm`; extend `AgentInteractionStatuses` with `Rejected`,
  `Cancelled`.
- New `InteractionChannels` constants: `ui`, `slack`, `teams`, `webhook`, `agent`.

### Application / orchestration — `src/Agentwerke.Application/`

- New `Agents/IInteractionChannel.cs` — the provider-neutral contract. **No provider type may be
  referenced from `Agentwerke.Domain`, `Agentwerke.Agents`, or `Agentwerke.Application`;** adapters
  live in `Agentwerke.Integrations` and are bound by DI, mirroring how `ConnectorApprovalNotifier`
  depends on `IApprovalNotifier` from `Application/Notifications`.
- New `Agents/InteractionRouter.cs` — resolves channels, fans out, records deliveries, never throws
  into the agent path.
- New `Agents/InteractionChannelResolver.cs` — the layered config precedence.
- Extend `WorkflowRunOrchestrationService`: guard `AnswerInteractionAsync` with the concurrency token;
  add `RejectInteractionAsync`, `CancelInteractionAsync`, `ExpireInteractionAsync`.
- Extend `IAgentInteractionRepository`: `GetPendingAsync(filter)`, `GetDueForExpiryAsync(now)`,
  `TryTransitionAsync(id, expectedVersion, …)`.
- New `Agentwerke.Infrastructure/Workers/InteractionTimeoutSweeper.cs` — `BackgroundService`, polls on
  a configurable interval, alongside the existing `WorkflowRunExecutor`.

### Agent tools — `src/Agentwerke.Agents/Tools/`

- `HumanInteractionTools.cs`: add `HumanConfirmTool`; add optional `channels` and `timeout_seconds`
  parameters to `human.ask`/`human.notify`; **stop matching on prompt text** — persist the interaction
  id into run context (`RunContextEntry`) keyed by run+step and look it up on re-run.
- `AgentRequestTool.cs`: add a `blocking` parameter (default `true` — preserves today's behavior); add
  a depth counter threaded through `AgentToolExecutionContext`; add cycle detection over the delegation
  chain; record the request row as `Blocking = true`/`Pending` for blocking mode so the run
  conversation is truthful (it currently lies: both rows are `Posted`/non-blocking).
- Tool registration is unchanged — `AgentToolCategories.Coordination` / `SubAgent` already exist.

### Integrations — `src/Agentwerke.Integrations/`

- `Channels/SlackInteractionChannel.cs` — implements `IInteractionChannel`; renders Block Kit with
  option buttons; free-text via a modal (`view_submission`) with the interaction id in `private_metadata`.
- `Channels/TeamsInteractionChannel.cs` — outbound notify only in v1; returns
  `DeliveryResult.NotSupported` for interactions requiring a response, so the router can fall back and
  the UI can show why.
- `Channels/WebhookInteractionChannel.cs` — signed outbound POST; HMAC-SHA256 over the raw body with
  `X-Agentwerke-Signature: sha256=<hex>` and `X-Agentwerke-Timestamp`, matching the existing scheme.
- Extend `IntegrationOptions` with `InteractionOptions` (default channels, timeout, retry, webhook
  endpoint + secret). Secrets resolve through the existing `ISecretStore` — no secret in appsettings.

### API — `src/Agentwerke.Api/`

- `Controllers/InteractionsController.cs` (new): `GET /interactions`, `POST /interactions/{id}/cancel`.
- Extend `RunsController`: add `reject` verb; add channel/timeout/delivery fields to `InteractionSummary`.
- `Controllers/WebhooksController.cs`: **refactor `SlackInteractions` to dispatch on payload shape** —
  approval actions keep calling `ResumeRunAsync` (unchanged), interaction actions route to the
  interaction path. Add `POST webhooks/interactions/response` for the generic channel.

### UI — `web/src/`

- `views/ApprovalsDashboard.tsx`: add a pending-interactions section (requesting agent, workflow, run,
  step, question, choices, blocking, age, expiry).
- `components/ConversationTab.tsx`: render choice buttons, confirm approve/reject/redirect, winning
  channel, terminal-state disabling, delivery failures with a retry action.
- `api/client.ts`, `types/index.ts`: new fields and endpoints.
- Live update: **poll**, consistent with the existing views. No SSE/WebSocket — the repo has neither,
  and adding one is out of scope.

### Observability

- Audit actions: `interaction.create`, `interaction.deliver`, `interaction.answer` (exists),
  `interaction.reject`, `interaction.cancel`, `interaction.expire`, `interaction.delivery_failed`.
- Metrics via the existing `IWorkflowMetrics`: pending gauge, age histogram, delivery
  success/failure by channel, race-loss counter.

## Data Model Changes

New columns on `agent_interactions` (all nullable or defaulted — the table has live rows):

| Column | Type | Notes |
|---|---|---|
| `RequestedChannels` | `jsonb` | Channels the router was asked to deliver to. Use the existing `SerializeStringList` converter. |
| `RespondedChannel` | `varchar(32)` | Which channel supplied the accepted response. Null until terminal. |
| `TimeoutAt` | `varchar(64)` | ISO-8601. Null = never expires. **Matches the existing `string` timestamp convention** (`CreatedAt`, `RespondedAt`) — do not introduce `DateTimeOffset` columns here alone. |
| `ExpiresAction` | `varchar(32)` | `fail` \| `continue` \| `default_answer`. |
| `DefaultAnswer` | `varchar(8192)` | Used when `ExpiresAction = default_answer`. |
| `CancelledAt` / `CancelledBy` | `varchar(64)` / `varchar(128)` | |
| `ResumedAt` | `varchar(64)` | Distinguishes "response accepted" from "run resumed". |
| `DelegationDepth` | `int` default `0` | Cycle/depth guard for `agent_request`. |
| `Version` | `int` default `0` | **Concurrency token** — `IsConcurrencyToken()`. See below. |

New table `interaction_deliveries`:

| Column | Type | Notes |
|---|---|---|
| `Id` | `varchar(64)` PK | |
| `InteractionId` | `varchar(64)` | FK → `agent_interactions.Id`, indexed |
| `Channel` | `varchar(32)` | |
| `Status` | `varchar(32)` | `pending` \| `delivered` \| `failed` \| `not_supported` |
| `ChannelMessageId` | `varchar(256)` | Provider id (Slack `ts`, webhook response id) |
| `Attempts` | `int` | |
| `LastAttemptAt` / `LastError` | `varchar(64)` / `varchar(1024)` | |
| `CreatedAt` | `varchar(64)` | |

Indexes and constraints:

- `agent_interactions`: add `HasIndex(Status)` and `HasIndex(Status, TimeoutAt)` (sweeper query);
  keep the existing `HasIndex(RunId)`; add `HasIndex(CorrelationId)`.
- `interaction_deliveries`: unique `(InteractionId, Channel)`; index `(Channel, ChannelMessageId)` for
  inbound correlation.

**Concurrency.** Use an EF `int` concurrency token incremented on every transition, not Postgres
`xmin`. Rationale: every unit-test fake in this repo is **hand-rolled** against the repository
interfaces (`tests/Agentwerke.Agents.Tests/InMemoryInteractionRepository.cs`,
`InMemoryAgentInteractionRepository` inside `WorkflowRunOrchestrationServiceTests`) — there is no EF
InMemory provider, no SQLite, and no Testcontainers anywhere in `tests/`. An `xmin` token is an EF
shadow property that a hand-rolled fake cannot emulate, so the single-winner logic would only be
exercisable in the Docker E2E stack. An `int Version` is an ordinary entity property the fakes can
implement, which keeps the race testable in fast unit tests.

**Migration.** Add one migration after `20260713070316_AddToolAccessContext`. Additive only: new
nullable columns, defaults for `Version`/`DelegationDepth`, new table. Existing rows are valid
untouched (no channels requested, no timeout, version 0). Reversible via `Down()`. No backfill.

## API Contracts

> Proposals. Align with existing conventions before implementing: the repo returns `Accepted` with a
> result body from `AnswerInteraction`, uses `{ message }` for controller errors and `{ error }` in
> `WebhooksController`, and serializes camelCase records.

**Create** — agents create interactions through tools, not HTTP. No public create endpoint in v1.

**List pending** — `GET /interactions?status=pending&runId=&addresseeType=human`

```json
{
  "items": [
    {
      "id": "8f2c1a...", "runId": "run-42", "stepId": "review-step",
      "from": "reviewer-agent", "kind": "confirm",
      "addresseeType": "human", "addressee": null,
      "blocking": true,
      "prompt": "Deploy build 1.4.2 to production?",
      "options": ["approve", "reject"],
      "status": "pending",
      "requestedChannels": ["ui", "slack"],
      "respondedChannel": null,
      "createdAt": "2026-07-15T09:00:00.0000000+00:00",
      "timeoutAt": "2026-07-15T10:00:00.0000000+00:00",
      "deliveries": [
        { "channel": "slack", "status": "delivered", "channelMessageId": "1721034000.1234", "attempts": 1 }
      ]
    }
  ]
}
```

**Answer** — `POST /runs/{runId}/interactions/{id}/answer` (exists; add `channel` to the response)

```json
{ "answer": "approve", "decision": "approve" }
```
```json
{ "runId": "run-42", "interactionId": "8f2c1a...", "status": "pending", "acceptedChannel": "ui" }
```

**Cancel** — `POST /interactions/{id}/cancel`

```json
{ "reason": "Superseded by a newer request." }
```

**Inbound generic channel** — `POST /webhooks/interactions/response`

```json
{
  "interactionId": "8f2c1a...",
  "response": "approve",
  "responder": { "id": "u-77", "displayName": "Dana Ito" },
  "nonce": "b41c9e02",
  "timestamp": "2026-07-15T09:14:00Z"
}
```

Headers: `X-Agentwerke-Signature: sha256=<hex>`, `X-Agentwerke-Timestamp: <unix>`.

Status semantics:

| Case | Status | Body |
|---|---|---|
| Accepted (winner) | `202 Accepted` | result with `acceptedChannel` |
| Duplicate — same responder, same value, already terminal | `409 Conflict` | `{ "message": "Interaction already answered." }` — idempotent, no second resume |
| Already answered by another channel | `409 Conflict` | `{ "message": "Interaction already answered via slack." }` |
| Expired | `409 Conflict` | `{ "message": "Interaction expired at ..." }` |
| Cancelled | `409 Conflict` | `{ "message": "Interaction was cancelled." }` |
| Unauthorized responder | `401` / `403` | `403` when authenticated but lacking `Approver` |
| Invalid choice not in `options` | `400 Bad Request` | `{ "message": "Answer must be one of: approve, reject." }` |
| Correlation / interaction not found | `404 Not Found` | empty (matches existing `NotFound()`) |
| Bad signature (webhook) | `401 Unauthorized` | `{ "error": "Signature mismatch." }` (matches `WebhooksController`) |
| Run not found | `422` | existing behavior — preserve |

Slack inbound must keep returning `200` with a body even on failure (Slack renders the body); do not
change that convention — see `WebhooksController.cs:180`.

## Agent Tool Contracts

Extend existing tools; add exactly one new tool (`human.confirm`). Do not duplicate `human.ask`.

**`human.ask`** — existing; add parameters. Blocking.

| Param | Type | Req | Notes |
|---|---|---|---|
| `question` | string | yes | existing |
| `options` | string | no | existing, comma-separated |
| `channels` | string | no | **new** — comma-separated override; defaults to config |
| `timeout_seconds` | string | no | **new** — defaults to config |

Returns on re-run: `Human answered via slack: <response>` (channel added to today's string).

**`human.confirm`** — new. Blocking. `question`, optional `channels`/`timeout_seconds`. Options are
fixed (`approve`, `reject`). On approve, returns `Confirmed by <responder> via <channel>.`; on reject,
throws `ConfirmationRejectedException` → step fails with the reason, mirroring the existing
`ToolAccessStepFailedException` handling at `AgentOrchestrator.cs:474`. **Rejection must not be fed
back into the model loop as a tool result.**

**`human.notify`** — existing; add `channels`. Non-blocking, unchanged semantics.

**`agent.request`** — existing; add `blocking` (string `"true"`/`"false"`, default `"true"`).
`blocking=true` keeps the inline nested run. `blocking=false` persists the request as `posted` and
returns `Dispatched to <to>.` immediately. Depth and cycle guards replace the hard-coded deny-list.

**`interaction.cancel`** — new, non-blocking. `interaction_id`, `reason`. Only the originating agent
in the same run may cancel.

`interaction.status` is **not** proposed: a blocking agent is suspended and cannot poll, and a
non-blocking one has no need. Adding it would invite busy-waiting.

## Security and Reliability

- **Authorization.** Answer/reject/cancel require `AgentwerkePolicies.Approver` (as
  `AnswerInteraction` does today). Listing requires `Viewer`.
- **Isolation.** Every lookup is scoped by `runId` — preserve the existing guard at
  `WorkflowRunOrchestrationService.cs:211` that rejects a mismatched run id. No tenant model exists;
  do not invent one.
- **Signature verification.** Reuse `WebhookSignatureValidator`'s HMAC-SHA256 + `FixedTimeEquals`.
  **Fix the empty-secret bypass for interaction endpoints:** `WebhookSignatureValidator.cs:22` returns
  `Ok()` when the secret is unset. That is tolerable for a trigger; for an endpoint that resumes a run
  it is an unauthenticated resume. The interaction endpoint must **fail closed** — refuse to start if
  enabled without a secret.
- **Replay.** Reject timestamps outside a ±5-minute window (Slack's rule, already implemented in
  `ValidateSlack`) and persist a seen-nonce set for the generic channel.
- **Secrets.** Via `ISecretStore` only, as `SlackConnector.ResolveWebhookUrlAsync` does.
- **Redaction.** Outbound payloads carry prompt, options, run/step ids, and agent name — never run
  context, artifacts, credentials, or tool output. Apply the existing redaction path; add tests that a
  secret-shaped value never reaches a channel payload.
- **Idempotency.** The concurrency token is the single-winner mechanism. Only the transaction that
  successfully transitions `pending → terminal` enqueues the outbox `Resume`.
- **Retries.** Delivery retries with exponential backoff (3 attempts, jitter) per channel. Delivery
  failure never fails the agent step — the interaction stays `pending` and answerable in the UI.
- **Timeout.** Sweeper transitions due rows using the same concurrency token, so it races safely
  against a late answer.
- **Recovery.** State is entirely in Postgres; `waiting_user` runs are recovered by the existing
  executor. A restart mid-delivery leaves a `pending` delivery row the router retries.
- **Delegation loops.** `DelegationDepth` capped (default 3, configurable) and the chain of agent ids
  is checked for repeats before dispatch.
- **Prompt injection.** A response is untrusted input. Insert it into the model context as clearly
  delimited, attributed data (`Human (dana@…, via slack) answered: "<text>"`), never as instructions,
  and never interpolate it into a system prompt. Free-text length capped at the column's 8192.

## Acceptance Criteria

1. **UI question and resume.** *Given* a blocking `human.ask`, *when* an `Approver` answers via
   `POST /runs/{id}/interactions/{id}/answer`, *then* the run leaves `waiting_user`, the step re-runs
   exactly once, and the agent receives the answer.
2. **UI confirmation rejected.** *Given* a pending `human.confirm`, *when* rejected, *then* status is
   `rejected`, the step fails with the rejection reason, and the model loop is not re-entered.
3. **Slack answer and resume.** *Given* an interaction delivered to the Slack fake, *when* a signed
   `block_actions` payload naming the interaction id arrives, *then* it is accepted, `respondedChannel`
   is `slack`, and the run resumes once.
4. **Teams (v1 scope).** *Given* Teams is enabled, *when* a blocking interaction is routed, *then* an
   outbound card is delivered, the delivery row is `not_supported` for response, and the UI shows the
   question is answerable in the UI/Slack only. *(Teams inbound answer-and-resume is deferred — see
   Open Questions.)*
5. **Generic webhook answer and resume.** *Given* a blocking interaction delivered to the webhook fake,
   *when* a correctly signed response posts to `/webhooks/interactions/response`, *then* it is
   accepted and the run resumes once.
6. **Non-blocking notification.** *Given* `human.notify`, *then* the interaction is `posted`, the run
   never enters `waiting_user`, and the step completes.
7. **Blocking agent-to-agent.** *Given* Agent A calls `agent.request` with `blocking=true`, *then* the
   request and reply share a `CorrelationId`, the request row is `Blocking = true`, and A's step
   completes with B's response.
8. **Non-blocking agent-to-agent.** *Given* `blocking=false`, *then* A's step completes without
   waiting and the request persists as `posted`.
9. **Duplicate responses.** *Given* an answered interaction, *when* the identical response is replayed,
   *then* `409`, and the outbox contains exactly one `Resume` for that interaction.
10. **Simultaneous UI and channel.** *Given* concurrent UI and Slack responses, *then* exactly one
    reaches a terminal state, the loser gets `409`, exactly one `Resume` is enqueued, and
    `respondedChannel` names the winner.
11. **Invalid / unauthorized.** *Given* an interaction with `options: [approve, reject]`, *when*
    `maybe` is submitted, *then* `400`. *When* a non-`Approver` answers, *then* `403`. *When* an
    invalid signature arrives, *then* `401` and no state change.
12. **Timeout.** *Given* an interaction whose `TimeoutAt` has passed, *when* the sweeper runs, *then*
    status is `expired` and `ExpiresAction` is honoured (`fail` fails the step; `default_answer`
    resumes with the default).
13. **Restart.** *Given* a run in `waiting_user`, *when* the host restarts and the question is then
    answered, *then* the run resumes normally.
14. **Delivery failure.** *Given* the Slack fake returns `500`, *then* the delivery row is `failed`
    with the attempt count and error, the interaction stays `pending`, the API exposes it, and a retry
    succeeds once the fake recovers.
15. **Cancelled run.** *Given* a cancelled run, *when* a response arrives for its interaction, *then*
    it is rejected and no `Resume` is enqueued.
16. **Audit.** *Given* a completed interaction, *then* run history and audit contain create, deliver
    (per channel), the accepted response with responder and channel, and resume — each with timestamps.

## Implementation Tasks

Each task below is a standalone sub-ticket in this directory with its own step-by-step plan, files,
acceptance criteria, and verification command. Dependency order runs top to bottom; tasks on the same
row of the dependency graph can run in parallel.

| # | Sub-ticket | Layer | Depends on |
|---|---|---|---|
| 002 | [Extend the interaction domain vocabulary](002-extend-interaction-domain-vocabulary.md) | domain | — |
| 003 | [Map the new fields and add the migration](003-add-interaction-persistence-migration.md) | migration | 002 |
| 004 | [Add single-winner interaction transitions](004-add-single-winner-interaction-transitions.md) | persistence | 003 |
| 005 | [Add reject, cancel, expire; guard answer](005-add-interaction-orchestration-verbs.md) | application | 004 |
| 006 | [Add the channel abstraction and router](006-add-interaction-channel-abstraction-and-router.md) | application | 004 |
| 007 | [Add the interaction timeout sweeper](007-add-interaction-timeout-sweeper.md) | worker | 004, 005 |
| 008 | [Add human.confirm; fix the re-run lookup](008-extend-human-interaction-tools.md) | agent tools | 002, 006 |
| 009 | [Add agent.request modes, depth, cycles](009-add-agent-request-blocking-modes.md) | agent tools | 002 |
| 010 | [Add the generic webhook channel](010-add-generic-webhook-interaction-channel.md) | provider | 006 |
| 011 | [Add the Slack channel](011-add-slack-interaction-channel.md) | provider | 006, 010 |
| 012 | [Add the Teams outbound channel](012-add-teams-outbound-interaction-channel.md) | provider | 006 |
| 013 | [Add the interactions API surface](013-extend-interaction-api-surface.md) | API | 005 |
| 014 | [Surface pending interactions in the inbox](014-surface-pending-interactions-in-decision-inbox.md) | frontend | 013 |
| 015 | [Enrich the run conversation](015-enrich-conversation-tab-interactions.md) | frontend | 013, 014 |
| 016 | [Add E2E tests](016-add-interaction-e2e-tests.md) | E2E | 008–012, 014, 015 |
| 017 | [Document channels and operations](017-document-interaction-channels.md) | docs | 013, 016 |

Critical path: **002 → 003 → 004 → 005/006 → providers/UI → 016**. Task 004 is the correctness core —
everything about duplicate, late, and racing responses depends on it, so do not defer it or stub it.

Suggested phasing, each independently shippable behind `Integrations:Interactions:Enabled`:

- **Phase 1 (002–008, 013–015):** UI + generic webhook, full lifecycle. Delivers most of the value.
- **Phase 2 (011):** Slack in/out.
- **Phase 3 (012):** Teams outbound.
- **Phase 4:** Teams inbound — separate ticket, see Open Questions.
- **009** is independent of the channel work and can run in parallel with any phase.


## Test Workflow

### Automated validation (exact repository commands, per `CONTRIBUTING.md`)

```bash
dotnet restore Agentwerke.sln
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.Domain.Tests --no-build
dotnet test tests/Agentwerke.Agents.Tests --no-build
dotnet test tests/Agentwerke.Application.Tests --no-build
dotnet test tests/Agentwerke.Integrations.Tests --no-build
dotnet test tests/Agentwerke.Api.Tests --no-build
dotnet test Agentwerke.sln --no-build          # full suite
cd web && npm ci && npm test && npm run lint && npm run build
```

### Local E2E: human via UI

```bash
docker compose -f docker/docker-compose.e2e.yml up -d
dotnet test tests/Agentwerke.E2ETests --no-build
```

1. Bring up the E2E stack (Postgres + WireMock + API; see `docs/deployment.md`).
2. Upload a fixture workflow whose agent calls `human.ask` (WireMock stubs the Anthropic Messages API
   into emitting the tool call — follow `tests/Agentwerke.E2ETests/Fixtures/wiremock-anthropic-stub.json`).
3. Assert the run reaches `waiting_user`.
4. Assert `GET /runs/{id}/interactions` returns exactly one `pending` row.
5. `POST /runs/{id}/interactions/{id}/answer`.
6. Assert the step re-runs once (step attempt count) and receives the answer.
7. Assert the run completes.
8. Assert audit contains create, answer (with responder + channel), and resume.

### External-channel scenarios (Slack, Teams, generic webhook)

**No real Slack or Teams credentials in CI.** Point each connector's webhook URL at WireMock, which the
stack already runs. For each provider:

1. Route a blocking interaction to the fake endpoint.
2. Capture the outbound payload from WireMock's request journal; assert shape and that no redacted
   value appears.
3. Post a correctly signed inbound response (Teams: skipped in v1 — assert `not_supported` instead).
4. Assert correlation and responder identity.
5. Assert exactly one resume.
6. Replay the response → `409`, still one resume.
7. Post an invalid signature → `401`, no state change.
8. Stub a `500` → delivery row `failed` with attempts; assert retry.

A credentialed smoke test against a real Slack workspace is documented in
`docs/manual-test-interactions.md` and is **not** part of CI.

### Agent-to-agent

1. Agent A → blocking `agent.request` to Agent B; assert correlation, both persisted rows, and A's
   completion with B's response.
2. Repeat with `blocking=false`; assert A does not suspend.
3. B fails → assert the failure is returned to A as a tool failure, not an unhandled exception.
4. Cancel A's run mid-delegation → assert propagation.
5. Exceed `DelegationDepth` → assert rejection.
6. A → B → A cycle → assert detection.

### Concurrency and recovery

1. Park a run on a blocking `human.ask`.
2. `docker compose -f docker/docker-compose.e2e.yml restart api`; answer after restart; assert resume.
3. Fire UI + Slack + webhook responses concurrently; assert one terminal transition, one `Resume`, two
   `409`s.
4. Answer after timeout/cancellation; assert no resume.

## Rollout and Compatibility

- **Migration safety.** Additive and reversible. Existing rows valid with no channels, no timeout,
  `Version = 0`. No backfill, no downtime.
- **Feature flags.** `Integrations:Interactions:Enabled` (default `false`) gates all external routing.
  Off → today's UI-only behavior byte-for-byte. Per-provider `Enabled` flags already exist on
  `SlackOptions`/`TeamsOptions` — reuse them.
- **Backward compatibility.** `human.ask`/`human.notify`/`agent.request` keep their current signatures
  and defaults; new params are optional; `agent.request` defaults `blocking=true` (today's behavior).
  The approvals path is untouched. Existing Slack approve/reject buttons keep working — AC for task 10
  is that `SlackInteractionTests` passes unmodified.
- **Default timeout** ships as **null (never expires)** so enabling the feature cannot start expiring
  runs that previously waited indefinitely. Operators opt in per config or per interaction.
- **Phased providers.** Phase 1: UI + generic webhook. Phase 2: Slack in/out. Phase 3: Teams outbound.
  Phase 4 (separate ticket): Teams inbound.
- **Observability.** Ship metrics and audit actions with phase 1 so a dark launch is measurable.

## Open Questions

Non-blocking for implementation unless noted; each has a stated default so work can start.

1. **Teams inbound.** Deferred by decision. Needs an Azure Bot Framework registration and a token
   validation story. *Blocks AC 4's full "answer and resume" form* — AC 4 is written to v1 scope
   instead. Follow-up ticket required before Teams can be called complete.
2. **Approval/interaction consolidation.** `AgentInteractionKinds.Approval` is declared but approvals
   live in their own table. This ticket deliberately leaves both. *Default:* leave; revisit once
   channel routing is proven on interactions.
3. **External responder identity.** Slack currently records `slack:<username>` as a free string. Should
   a channel identity map to an Agentwerke principal before it is allowed to answer a blocking
   confirmation? *Default (v1):* record the channel identity as-is and rely on the channel's own
   workspace membership, matching today's approval behavior. *This is a real authorization gap* — a
   Slack workspace member effectively answers without an Agentwerke role. Confirm before enabling
   Slack for confirmations in production.
4. **Free-text via Slack.** Modal (`view_submission`) is proposed. Confirm the modal round-trip is
   acceptable versus restricting Slack to structured choices only. *Default:* modal.
5. **Redirect.** The goal names "approved, rejected, or redirected" but no redirect semantics exist in
   the repo. *Default:* treat redirect as answering with guidance text (today's tool-access behavior at
   `AgentInteraction.Intent`) rather than reassigning the interaction. Confirm.
6. **Nonce store.** New table versus reusing `RunContextEntry`. *Default:* small dedicated table with a
   TTL sweep.

## Definition of Done

- [ ] All 16 acceptance criteria have automated tests.
- [ ] `dotnet build Agentwerke.sln` and `dotnet test Agentwerke.sln` pass.
- [ ] `cd web && npm test && npm run lint && npm run build` pass.
- [ ] Migration applies and reverses against a populated database; existing runs unaffected.
- [ ] Feature flag off reproduces current behavior exactly; existing approval tests pass unmodified.
- [ ] API documented; example payloads match the implementation.
- [ ] Operational runbook covers channel configuration, secrets, timeout tuning, and delivery retry.
- [ ] Metrics and audit actions emitted and verified.
- [ ] E2E evidence attached: UI, webhook, Slack, race, restart, timeout.
- [ ] Open questions 1 and 3 resolved or explicitly accepted as risks by the product owner.
