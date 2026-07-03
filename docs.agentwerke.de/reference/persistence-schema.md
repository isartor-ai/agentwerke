# Persistence Schema

Agentwerke uses EF Core migrations in `src/Agentwerke.Infrastructure` with the default PostgreSQL schema `agentwerke`.

The model stores timestamps as ISO-8601 strings in text columns and uses `jsonb` for list-valued fields.

## Core tables

### `workflows`

Stores workflow definitions, status, metadata, tags, and BPMN XML.

Important fields:

- `Id`
- `Name`
- `Version`
- `Status`
- `Owner`
- `ValidationState`
- `Tags`
- `BpmnXml`

### `workflow_runs`

Stores one workflow execution.

Important fields:

- `Id`
- `WorkflowId`
- `WorkflowName`
- `WorkflowVersion`
- `Status`
- `RiskLevel`
- `CurrentStep`
- `RequestedBy`
- `StartedAt`
- `CompletedAt`
- `PendingApprovals`
- `Tags`

### `workflow_run_steps`

Stores step-level execution state, output, and policy decision details.

Important fields:

- `RunId`
- `Name`
- `Type`
- `Status`
- `AgentName`
- `Output`
- `PolicyDecision_*`

### `workflow_events`

Stores run-scoped event messages and timestamps.

### `workflow_run_context`

Stores run-scoped key/value context such as:

- `input.body`
- `input.title`
- `output.WriteRequirements`

Writes upsert by `(RunId, Key)`.

### `approval_requests`

Stores approval task state, risk information, decision comment, decision timestamp, and approver identity.

### `agent_interactions`

Stores run-scoped agent communications:

- coordination-bus posts
- questions
- choices
- notifications
- `agent.request`
- approval-adjacent interaction records

This table backs the run Conversation view.

## Notes

- `PolicyDecision` is an owned type on `workflow_run_steps`.
- `approval_requests`, `agent_interactions`, and `workflow_run_context` are standalone tables keyed by run id in the current model.
- Approvals still live in `approval_requests`; folding them into `agent_interactions` is a possible future consolidation.

## Migration commands

Add a migration:

```bash
dotnet ef migrations add <Name> \
  --project src/Agentwerke.Infrastructure \
  --startup-project src/Agentwerke.Api
```

List migrations:

```bash
dotnet ef migrations list \
  --project src/Agentwerke.Infrastructure \
  --startup-project src/Agentwerke.Api
```

Generate an idempotent SQL script:

```bash
dotnet ef migrations script --idempotent \
  --project src/Agentwerke.Infrastructure \
  --startup-project src/Agentwerke.Api
```
