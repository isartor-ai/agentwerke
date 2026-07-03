# Implement Agentwerke agent job worker for Camunda service tasks

## Summary
Add a background worker that activates Camunda jobs of type `autofac.agent` and executes them through the Agentwerke Agent Orchestrator.

## Why
Camunda service tasks are the correct extension point for custom Agentwerke agent work.

## Scope
- Activate jobs for `autofac.agent`.
- Resolve BPMN element metadata, task headers, and process variables.
- Call the Agent Orchestrator.
- Record job start, output, failure, and completion events in Agentwerke.

## Acceptance Criteria
- Worker receives a Camunda service task job.
- Worker invokes the Agent Orchestrator with task context.
- Successful job completion advances the Camunda process.
- Run events show the worker lifecycle.

## Verification
- Integration test with a no-op agent executor completes an `autofac.agent` job.
- Worker logs include job key, run id, BPMN element id, and agent id.

## Suggested Files
- `src/Autofac.Infrastructure/Workers`
- `src/Autofac.Agents`
- `src/Autofac.Application/Workflows`
- `tests/Autofac.E2ETests`
