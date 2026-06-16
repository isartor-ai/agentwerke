# Add Camunda 8 local runtime profile

## Summary
Add a local Docker Compose profile that runs Camunda 8 with the Autofac stack for development and manual testing.

## Why
Autofac needs Camunda available locally before adapters, workers, publish flow, and manual tests can be implemented reliably.

## Scope
- Add Camunda services to the manual or e2e compose setup behind a `camunda` profile.
- Document startup and health verification.
- Keep the existing non-Camunda manual stack working.

## Acceptance Criteria
- Developers can start Autofac with Camunda using one documented command.
- Camunda health or topology check succeeds.
- Autofac API health endpoint still succeeds.
- Existing manual compose path remains usable.

## Verification
- `docker compose -f docker/docker-compose.manual.yml --profile camunda up --build`
- API health endpoint returns healthy.
- Camunda topology or health endpoint responds.

## Suggested Files
- `docker/docker-compose.manual.yml`
- `docker/docker-compose.e2e.yml`
- `docs/manual-test-camunda8-sdlc-factory.md`
