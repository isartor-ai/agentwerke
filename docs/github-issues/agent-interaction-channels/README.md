# Agent Interaction Channels GitHub Issue Drafts

This directory contains the implementation plan for routing agent clarification, confirmation, and
collaboration beyond the Agentwerke UI.

**[001](001-agent-clarification-confirmation-collaboration.md) is the parent ticket** — read it first
for the behavioral model, repository findings, data model, API contracts, and the 16 acceptance
criteria. **002–017 are sub-tickets**, each a focused, independently implementable step with its own
plan, files, acceptance criteria, and verification command.

## What already exists

The interaction primitive (`AgentInteraction`, #192) and the blocking suspend/resume loop are built and
working: `HumanAskTool` → `AgentInteractionRequiredException` → `AgentOrchestrator` parks the run in
`waiting_user` → answer via the API → outbox `Resume` → the step re-runs and the tool returns the
answer. Nothing polls, no thread is held. This plan **extends** that; it does not replace it.

## What is missing

- Delivery of an interaction to Slack, Microsoft Teams, or a generic webhook — nothing leaves the UI
  today; `ConnectorApprovalNotifier` fans out approvals only.
- A single-winner mechanism. There is no concurrency token anywhere in the model, so two channels
  answering can resume a run twice.
- Timeout. `AgentInteractionStatuses.Expired` is declared and **never assigned anywhere in `src/`** —
  a blocking question parks its run forever.
- Rejection and cancellation — neither state exists.
- Delivery tracking, so a failed channel post is visible and retryable.
- Non-blocking agent-to-agent dispatch alongside today's inline blocking delegation.

## Product decisions (2026-07-15)

- Provider-neutral `IInteractionChannel` abstraction plus **Slack and generic webhook** in v1.
  **Teams is outbound-only**; Teams inbound is deferred (it needs an Azure Bot Framework registration —
  its own workstream).
- Channel selection is layered: config default → per-workflow/agent → optional per-interaction
  override. Fan-out to all selected channels; **first valid response wins**; losers get `409`.
- Blocking `agent.request` keeps today's inline nested run; a non-blocking dispatch mode is added.

## Tickets

| # | Issue | Title | Layer | Depends on |
|---|---|---|---|---|
| **001** | **[#215](https://github.com/isartor-ai/agentwerke-private/issues/215)** | **[Route agent clarification, confirmation, and collaboration](001-agent-clarification-confirmation-collaboration.md)** | **parent** | — |
| 002 | [#216](https://github.com/isartor-ai/agentwerke-private/issues/216) | [Extend the interaction domain vocabulary](002-extend-interaction-domain-vocabulary.md) | domain | — |
| 003 | [#217](https://github.com/isartor-ai/agentwerke-private/issues/217) | [Map the new fields and add the migration](003-add-interaction-persistence-migration.md) | migration | 002 |
| 004 | [#218](https://github.com/isartor-ai/agentwerke-private/issues/218) | [Add single-winner interaction transitions](004-add-single-winner-interaction-transitions.md) | persistence | 003 |
| 005 | [#219](https://github.com/isartor-ai/agentwerke-private/issues/219) | [Add reject, cancel, expire; guard answer](005-add-interaction-orchestration-verbs.md) | application | 004 |
| 006 | [#220](https://github.com/isartor-ai/agentwerke-private/issues/220) | [Add the channel abstraction and router](006-add-interaction-channel-abstraction-and-router.md) | application | 004 |
| 007 | [#221](https://github.com/isartor-ai/agentwerke-private/issues/221) | [Add the interaction timeout sweeper](007-add-interaction-timeout-sweeper.md) | worker | 004, 005 |
| 008 | [#222](https://github.com/isartor-ai/agentwerke-private/issues/222) | [Add human.confirm; fix the re-run lookup](008-extend-human-interaction-tools.md) | agent tools | 002, 006 |
| 009 | [#223](https://github.com/isartor-ai/agentwerke-private/issues/223) | [Add agent.request modes, depth, cycles](009-add-agent-request-blocking-modes.md) | agent tools | 002 |
| 010 | [#224](https://github.com/isartor-ai/agentwerke-private/issues/224) | [Add the generic webhook channel](010-add-generic-webhook-interaction-channel.md) | provider | 006 |
| 011 | [#225](https://github.com/isartor-ai/agentwerke-private/issues/225) | [Add the Slack channel](011-add-slack-interaction-channel.md) | provider | 006, 010 |
| 012 | [#226](https://github.com/isartor-ai/agentwerke-private/issues/226) | [Add the Teams outbound channel](012-add-teams-outbound-interaction-channel.md) | provider | 006 |
| 013 | [#227](https://github.com/isartor-ai/agentwerke-private/issues/227) | [Add the interactions API surface](013-extend-interaction-api-surface.md) | API | 005 |
| 014 | [#228](https://github.com/isartor-ai/agentwerke-private/issues/228) | [Surface pending interactions in the inbox](014-surface-pending-interactions-in-decision-inbox.md) | frontend | 013 |
| 015 | [#229](https://github.com/isartor-ai/agentwerke-private/issues/229) | [Enrich the run conversation](015-enrich-conversation-tab-interactions.md) | frontend | 013, 014 |
| 016 | [#230](https://github.com/isartor-ai/agentwerke-private/issues/230) | [Add E2E tests](016-add-interaction-e2e-tests.md) | E2E | 008–012, 014, 015 |
| 017 | [#231](https://github.com/isartor-ai/agentwerke-private/issues/231) | [Document channels and operations](017-document-interaction-channels.md) | docs | 013, 016 |

Critical path: **002 → 003 → 004 → 005/006 → providers/UI → 016**. **004 is the correctness core** —
every duplicate, late, and racing response depends on it. Do not stub it.

## Two bug fixes ride along

Both are pre-existing and were found during investigation, not introduced by this work:

- **008** — `HumanAskTool` matches a re-run's answer by prompt **string equality**
  (`HumanInteractionTools.cs:47-51`). A model that rephrases its question on re-run asks twice and the
  human answers into the void. Replaced with an interaction id in run context.
- **009** — `AgentRequestTool` records both sides of a blocking delegation as `posted`/non-blocking
  (`:161`), so the run conversation misreports every delegation.

**010** additionally closes a security gap: `WebhookSignatureValidator.cs:22` skips validation when the
secret is unset. Tolerable for a trigger; on an endpoint that resumes a run it is an unauthenticated
resume, so the interaction endpoint fails closed.

## Status

**Created in `isartor-ai/agentwerke-private` on 2026-07-15** as issues [#215](https://github.com/isartor-ai/agentwerke-private/issues/215)–[#231](https://github.com/isartor-ai/agentwerke-private/issues/231).
#215 is the tracking epic (label `epic`); #216–#231 are its sub-tickets and each backlinks to it.

### Implementation status (2026-07-16)

| Issues | Repository status |
| --- | --- |
| #216–#223 | Implemented and committed on `feat/agent-interactions-221`; #220's dedicated provider-boundary architecture test is still missing |
| #224 | Generic webhook implementation and API tests are committed on `feat/agent-interactions-224`; persistent nonce replay protection remains open |
| #225 | Slack adapter, shared callback dispatch, and provider/API tests are committed on `feat/agent-interactions-224` |
| #226–#229 | Implemented and committed on `feat/agent-interactions-221` |
| #230 | Not implemented; E2E fixtures/race/restart evidence gate remains open |
| #231 | Documentation added; manual test remains gated by #224's nonce protection and #230 as stated in `docs/manual-test-interactions.md` |

This status is intentionally implementation-based rather than inferred from GitHub issue state (the
issues are still open). The epic is not complete until #224's nonce protection and #230 land
and the manual scenario passes from a clean checkout.

The drafts in this directory remain the source of truth for edits — update the markdown, then sync the
issue body.

Created GitHub issues:

1. [#215 Route agent clarification, confirmation, and collaboration across UI, chat channels, and agents](https://github.com/isartor-ai/agentwerke-private/issues/215)
2. [#216 Extend the interaction domain vocabulary for channels, confirmation, and delivery](https://github.com/isartor-ai/agentwerke-private/issues/216)
3. [#217 Map the new interaction fields and add the migration](https://github.com/isartor-ai/agentwerke-private/issues/217)
4. [#218 Add single-winner interaction transitions to the repository](https://github.com/isartor-ai/agentwerke-private/issues/218)
5. [#219 Add reject, cancel, and expire orchestration verbs and guard answer](https://github.com/isartor-ai/agentwerke-private/issues/219)
6. [#220 Add the provider-neutral channel abstraction and interaction router](https://github.com/isartor-ai/agentwerke-private/issues/220)
7. [#221 Add the interaction timeout sweeper](https://github.com/isartor-ai/agentwerke-private/issues/221)
8. [#222 Add human.confirm and fix the re-run answer lookup](https://github.com/isartor-ai/agentwerke-private/issues/222)
9. [#223 Add non-blocking agent.request, depth counting, and cycle detection](https://github.com/isartor-ai/agentwerke-private/issues/223)
10. [#224 Add the generic webhook interaction channel (outbound + inbound)](https://github.com/isartor-ai/agentwerke-private/issues/224)
11. [#225 Add the Slack interaction channel without breaking Slack approvals](https://github.com/isartor-ai/agentwerke-private/issues/225)
12. [#226 Add the Teams outbound interaction channel (notify only)](https://github.com/isartor-ai/agentwerke-private/issues/226)
13. [#227 Add the interactions API surface](https://github.com/isartor-ai/agentwerke-private/issues/227)
14. [#228 Surface pending interactions in the decision inbox](https://github.com/isartor-ai/agentwerke-private/issues/228)
15. [#229 Enrich the run conversation with channels, confirmation, and delivery state](https://github.com/isartor-ai/agentwerke-private/issues/229)
16. [#230 Add end-to-end tests for interaction channels, races, and recovery](https://github.com/isartor-ai/agentwerke-private/issues/230)
17. [#231 Document interaction channels, configuration, and operations](https://github.com/isartor-ai/agentwerke-private/issues/231)

Two open questions in #215 need an answer before the epic can be called done, though neither blocks
starting:

1. **Teams inbound** (Q1) — deferred by decision; needs a follow-up ticket before Teams is complete.
2. **External responder identity** (Q3) — a Slack workspace member can currently answer a blocking
   confirmation without holding an Agentwerke `Approver` role. v1 inherits this from the existing
   approval path rather than introducing it, but resolve it before enabling Slack confirmations in
   production.
