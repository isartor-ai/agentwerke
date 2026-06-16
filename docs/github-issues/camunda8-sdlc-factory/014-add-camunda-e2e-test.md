# Add Camunda-backed end-to-end workflow test

## Summary
Add an E2E test that proves publish, start, agent job completion, user task approval, and workflow completion against Camunda.

## Why
The architecture decision is not real until it is covered by an executable end-to-end scenario.

## Scope
- Gate test behind `CAMUNDA_ENABLED=true` or a compose profile.
- Deploy a minimal Autofac agent workflow.
- Start process, complete a no-op agent job, complete approval, and assert completion.
- Include useful failure diagnostics from Camunda responses.

## Acceptance Criteria
- Test passes when Camunda profile is running.
- Test skips cleanly when Camunda is disabled.
- Test proves both service task and user task integration.
- Failure output includes Camunda response body.

## Verification
- `CAMUNDA_ENABLED=true dotnet test tests/Autofac.E2ETests/Autofac.E2ETests.csproj`

## Suggested Files
- `tests/Autofac.E2ETests`
- `docker/docker-compose.e2e.yml`
- `tests/Autofac.E2ETests/Fixtures`
