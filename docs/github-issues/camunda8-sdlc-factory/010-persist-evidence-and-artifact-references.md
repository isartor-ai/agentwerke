# Persist evidence and artifact references for Camunda-backed runs

## Summary
Store agent outputs, evidence, and artifacts in Agentwerke, then pass stable references through Camunda variables.

## Why
Camunda variables should not become the artifact store. Agentwerke needs a durable evidence chain that operators can inspect.

## Scope
- Define evidence produced and evidence required for each agent task.
- Store large outputs in Agentwerke artifact storage.
- Pass artifact ids or URLs as Camunda variables.
- Add run detail API fields for evidence and artifact references.

## Acceptance Criteria
- Agent output creates evidence/artifact records.
- Run detail shows evidence required versus evidence produced.
- Camunda variables contain stable references, not large blobs.
- Tests cover artifact creation and run detail serialization.

## Verification
- Unit/integration tests for artifact reference creation.
- Manual run shows spec/test/PR evidence in Run Detail.

## Suggested Files
- `src/Autofac.Storage`
- `src/Autofac.Application/Workflows`
- `src/Autofac.Api/Contracts/Runs`
- `web/src/views/RunDetail.tsx`
