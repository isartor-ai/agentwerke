# Start Camunda process instances from the run API

## Summary
Update the run-start path so `POST /api/runs` starts a Camunda process instance and links it to an Agentwerke run record.

## Why
Agentwerke should remain the product API while Camunda owns process execution.

## Scope
- Start Camunda process instance for a published workflow.
- Create or update Agentwerke run record with Camunda process instance key.
- Pass initiator, correlation id, and run input as process variables.
- Return run id and initial status through the existing API contract.

## Acceptance Criteria
- Starting a published workflow starts a Camunda process.
- Agentwerke run stores Camunda process instance metadata.
- Run detail can retrieve the Camunda-backed run.
- Invalid workflow/start input returns structured errors.

## Verification
- Integration test starts a published Camunda workflow.
- Run detail API shows linkage between Agentwerke run and Camunda process instance.

## Suggested Files
- `src/Agentwerke.Application/Workflows`
- `src/Agentwerke.Infrastructure/Workflows`
- `src/Agentwerke.Api/Controllers/RunsController.cs`
- `tests/Agentwerke.Application.Tests`
