# Persistence Schema (Phase 1)

## Overview
The initial PostgreSQL schema is managed by EF Core migrations from `Autofac.Infrastructure`.
The default schema name is `autofac`.

## Tables

### workflow_definitions
- `id` (uuid, PK)
- `workflow_key` (varchar(128), required)
- `name` (varchar(256), required)
- `version` (int, required)
- `status` (varchar(64), required)
- `created_at_utc` (timestamptz, required)
- `updated_at_utc` (timestamptz, required)
- Unique index: `(workflow_key, version)`

### workflow_runs
- `id` (uuid, PK)
- `workflow_definition_id` (uuid, FK -> workflow_definitions.id)
- `status` (varchar(64), required)
- `initiator` (varchar(128), nullable)
- `started_at_utc` (timestamptz, required)
- `completed_at_utc` (timestamptz, nullable)

### workflow_events
- `id` (uuid, PK)
- `workflow_run_id` (uuid, FK -> workflow_runs.id)
- `event_type` (varchar(128), required)
- `payload_json` (jsonb, required)
- `created_at_utc` (timestamptz, required)

### approval_requests
- `id` (uuid, PK)
- `workflow_run_id` (uuid, FK -> workflow_runs.id)
- `approval_type` (varchar(128), required)
- `status` (varchar(64), required)
- `requested_by` (varchar(128), required)
- `requested_at_utc` (timestamptz, required)
- `resolved_at_utc` (timestamptz, nullable)

### policy_decisions
- `id` (uuid, PK)
- `workflow_run_id` (uuid, nullable FK -> workflow_runs.id)
- `policy_name` (varchar(128), required)
- `decision` (varchar(64), required)
- `reason` (varchar(2048), required)
- `evidence_json` (jsonb, required)
- `evaluated_at_utc` (timestamptz, required)

### agent_sessions
- `id` (uuid, PK)
- `workflow_run_id` (uuid, FK -> workflow_runs.id)
- `agent_name` (varchar(128), required)
- `status` (varchar(64), required)
- `started_at_utc` (timestamptz, required)
- `ended_at_utc` (timestamptz, nullable)

## Migration Commands
- Add migration:
  - `dotnet ef migrations add <Name> --project src/Autofac.Infrastructure --startup-project src/Autofac.Api`
- List migrations:
  - `dotnet ef migrations list --project src/Autofac.Infrastructure --startup-project src/Autofac.Api`
- Generate SQL script:
  - `dotnet ef migrations script --idempotent --project src/Autofac.Infrastructure --startup-project src/Autofac.Api`
