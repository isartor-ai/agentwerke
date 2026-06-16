# Add Camunda configuration and REST client abstraction

## Summary
Add typed Camunda options and a small HTTP client abstraction for Camunda REST API calls.

## Why
Autofac should integrate with Camunda through infrastructure adapters, not scattered HTTP calls or SDK-specific code in application services.

## Scope
- Add Camunda options for base URL, auth mode, timeout, and runtime enablement.
- Add a Camunda client abstraction in infrastructure.
- Implement health/topology or equivalent probe.
- Add tests for option binding and request construction.

## Acceptance Criteria
- Camunda configuration is read from appsettings/environment variables.
- Infrastructure exposes a single client abstraction for Camunda calls.
- API can report whether the Camunda runtime is configured and reachable.
- Unit tests cover configuration binding.

## Verification
- `dotnet test Autofac.sln --filter Camunda`
- Manual runtime health check reaches Camunda.

## Suggested Files
- `src/Autofac.Infrastructure`
- `src/Autofac.Api/appsettings*.json`
- `tests/Autofac.Infrastructure.Tests` or existing infrastructure test project
