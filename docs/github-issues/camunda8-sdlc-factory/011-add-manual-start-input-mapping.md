# Add manual start input mapping for Camunda process variables

## Summary
Normalize manual run input and inbound event payloads into Agentwerke run context and Camunda process variables.

## Why
Agents need clear input context, and runs need traceable trigger metadata.

## Scope
- Extend start run request with title, body, external URL, and typed input fields.
- Seed Agentwerke run context from start input.
- Pass normalized variables into Camunda process start.
- Validate malformed input.

## Acceptance Criteria
- Manual start can provide issue-like input.
- First agent job receives the input in execution context.
- Run detail shows trigger/source metadata.
- Invalid input returns structured validation errors.

## Verification
- API contract tests cover valid and invalid input.
- Manual test starts a run with input visible to the first agent task.

## Suggested Files
- `src/Autofac.Api/Contracts/Runs`
- `src/Autofac.Application/Workflows`
- `src/Autofac.Infrastructure/Persistence`
- `tests/Autofac.Api.Tests`
