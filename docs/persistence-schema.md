# Persistence Schema

## Overview

Agentwerke currently uses EF Core migrations in `src/Agentwerke.Infrastructure` with the default PostgreSQL schema `agentwerke`.

The model stores timestamps as ISO-8601 strings in text columns and uses `jsonb` for list-valued fields.

## Tables

### workflows

- `Id` (text, PK)
- `Name` (varchar(256), required)
- `Description` (varchar(1024), required)
- `Version` (varchar(64), required)
- `Status` (varchar(64), required)
- `Owner` (varchar(128), required)
- `CreatedAt` (text, required)
- `LastEditedAt` (text, required)
- `ValidationState` (varchar(64), required)
- `Tags` (jsonb, required)
- `BpmnXml` (text, required)

### workflow_runs

- `Id` (text, PK)
- `WorkflowId` (varchar(128), required)
- `WorkflowName` (varchar(256), required)
- `WorkflowVersion` (varchar(64), required)
- `Status` (varchar(64), required)
- `RiskLevel` (varchar(64), required)
- `CurrentStep` (varchar(256), required)
- `RequestedBy` (varchar(128), required)
- `StartedAt` (text, required)
- `CompletedAt` (text, nullable)
- `DurationMs` (integer, nullable)
- `PendingApprovals` (integer, required)
- `Tags` (jsonb, required)

### workflow_run_steps

- `Id` (text, PK)
- `RunId` (text, FK -> workflow_runs.Id)
- `Name` (varchar(256), required)
- `Type` (varchar(128), required)
- `Status` (varchar(64), required)
- `StartedAt` (text, nullable)
- `CompletedAt` (text, nullable)
- `AgentName` (varchar(128), nullable)
- `Output` (text, nullable)
- `PolicyDecision_Kind` (varchar(64), nullable)
- `PolicyDecision_PolicyId` (varchar(128), nullable)
- `PolicyDecision_PolicyName` (varchar(256), nullable)
- `PolicyDecision_Rationale` (varchar(1024), nullable)
- `PolicyDecision_RiskScore` (integer, nullable)
- `PolicyDecision_RiskLevel` (varchar(64), nullable)
- `PolicyDecision_RiskFactors` (jsonb, nullable)
- `PolicyDecision_DecidedAt` (text, nullable)
- `PolicyDecision_Constraints` (jsonb, nullable)

### workflow_events

- `Id` (text, PK)
- `RunId` (text, FK -> workflow_runs.Id)
- `Type` (varchar(128), required)
- `Message` (varchar(2048), required)
- `CreatedAt` (text, required)

### workflow_run_context

Run-scoped key/value bag used to flow data between tasks (Phase A). Seeded with
the triggering issue (`input.*`) at run start, and appended with each completed
service task's primary output (`output.<nodeId>`). Exposed to agent prompt
assembly as template variables and a rendered `run_context` section.

- `Id` (text, PK)
- `RunId` (varchar(128), required)
- `Key` (varchar(256), required) — e.g. `input.body`, `output.WriteRequirements`
- `Value` (text, required)
- `Kind` (varchar(64), required) — `input` or `output`
- `CreatedAt` (text, required)
- `UpdatedAt` (text, required)
- Unique index `ix_workflow_run_context_run_key` on (`RunId`, `Key`) — writes upsert by key.

### approval_requests

- `Id` (text, PK)
- `RunId` (varchar(128), required)
- `WorkflowName` (varchar(256), required)
- `ActionRequested` (varchar(1024), required)
- `Requester` (varchar(128), required)
- `AgentName` (varchar(128), required)
- `PolicyRationale` (varchar(2048), required)
- `RiskScore` (integer, required)
- `RiskLevel` (varchar(64), required)
- `RiskFactors` (jsonb, required)
- `AffectedSystems` (jsonb, required)
- `SlaDeadline` (text, required)
- `CreatedAt` (text, required)
- `Status` (varchar(64), required)
- `Priority` (varchar(64), required)
- `DecisionComment` (varchar(2048), nullable)
- `DecidedAt` (text, nullable)
- `DecidedBy` (varchar(128), nullable)

### agent_interactions

Run-scoped record of every agent communication (#192): coordination-bus posts, agent-to-agent
delegations, and questions/notifications to a human. Backs the run **Conversation** view.

- `Id` (text, PK)
- `RunId` (varchar(128), required, indexed)
- `StepId` (varchar(128), nullable) — the step that produced the interaction, for anchoring in the UI
- `FromAgent` (varchar(128), required)
- `Kind` (varchar(64), required) — `post` | `notify` | `question` | `choice` | `confirm` |
  `agent_request` | `approval` | `tool_access`
- `AddresseeType` (varchar(32), required) — `human` | `agent`
- `Addressee` (varchar(128), nullable) — agent id or human role/user; null = broadcast to the run
- `Blocking` (boolean, required) — whether the sender suspends until answered
- `Prompt` (varchar(8192), required) — the message / question / task text
- `Options` (jsonb, required) — choices offered to the responder
- `RequestedChannels` (jsonb, required, default `[]`) — requested delivery channels
- `CorrelationId` (varchar(128), nullable) — links a request to its reply (e.g. `agent.request` ↔ result)
- `Status` (varchar(64), required) — `pending` | `answered` | `posted` | `expired` | `rejected` | `cancelled`
- `Response` (varchar(8192), nullable)
- `RespondedBy` (varchar(128), nullable)
- `RespondedAt` (text, nullable)
- `RespondedChannel` (varchar(32), nullable) — channel whose accepted response won
- `TimeoutAt` (varchar(64), nullable) — expiry instant; null means never
- `ExpiresAction` (varchar(32), nullable) — `fail` | `continue` | `default_answer`
- `DefaultAnswer` (varchar(8192), nullable)
- `CancelledAt` (varchar(64), nullable)
- `CancelledBy` (varchar(128), nullable)
- `ResumedAt` (varchar(64), nullable)
- `DelegationDepth` (integer, required, default `0`)
- `Version` (integer, required, default `0`, optimistic concurrency token)
- `CreatedAt` (text, required)

Indexes cover `RunId`, `Status`, (`Status`, `TimeoutAt`), and `CorrelationId`.

`Version` deliberately uses an application-visible `int`, not PostgreSQL's `xmin`. Interaction
repositories have hand-written in-memory fakes and concurrency tests that must exercise the same token
without Docker or an EF shadow property. Replacing it with `xmin` would make the first-response-wins
invariant testable only against PostgreSQL and would weaken the fast unit-test proof of the race.

### interaction_deliveries

One row per interaction/channel, updated in place across retries:

- `Id` (text, PK)
- `InteractionId` (varchar(64), required, indexed)
- `Channel` (varchar(32), required)
- `Status` (varchar(32), required) — `pending` | `delivered` | `failed` | `not_supported`
- `ChannelMessageId` (varchar(256), nullable) — provider correlation id
- `Attempts` (integer, required)
- `LastAttemptAt` (varchar(64), nullable)
- `LastError` (varchar(1024), nullable) — provider-facing diagnostic retained for operators
- `CreatedAt` (varchar(64), required)

The unique (`InteractionId`, `Channel`) index makes retry an update rather than a duplicate delivery
row. (`Channel`, `ChannelMessageId`) supports inbound provider correlation.

All interaction and delivery timestamps are produced with the .NET round-trip (`"o"`) ISO-8601
format. This is a storage contract: the expiry repository compares `TimeoutAt` and the current
timestamp with ordinal string comparison, which is correct only while every value uses the same
UTC-normalized round-trip representation. Do not introduce locale-formatted or mixed-offset strings.

## Notes

- `PolicyDecision` is an owned type on `workflow_run_steps`, so it does not have its own table.
- `approval_requests` is intentionally modeled as a standalone table without a foreign key back to `workflow_runs` in the current EF model.
- `agent_interactions` is likewise standalone (no FK), keyed and indexed by `RunId`. Approvals still live in their own `approval_requests` table (conceptually `Kind=approval`); folding them into `agent_interactions` is a possible future consolidation.
- `workflow_run_context` is likewise standalone (no FK), keyed by `RunId`; entries are upserted by (`RunId`, `Key`).
- To add a migration:
  - `dotnet ef migrations add <Name> --project src/Agentwerke.Infrastructure --startup-project src/Agentwerke.Api`
- To list migrations:
  - `dotnet ef migrations list --project src/Agentwerke.Infrastructure --startup-project src/Agentwerke.Api`
- To generate an idempotent SQL script:
  - `dotnet ef migrations script --idempotent --project src/Agentwerke.Infrastructure --startup-project src/Agentwerke.Api`
