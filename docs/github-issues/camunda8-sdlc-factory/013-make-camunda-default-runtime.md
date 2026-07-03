# Make Camunda the default runtime and demote the in-process runtime

## Summary
Change production configuration so Camunda is the default runtime adapter, while the in-process runtime is explicit test/dev compatibility.

## Why
The team should stop adding production features to the local runtime path.

## Scope
- Register Camunda adapter by default when runtime mode is production/Camunda.
- Keep in-process runtime behind explicit config for tests or legacy manual scenario.
- Update tests to target adapter contracts instead of concrete runtime where appropriate.
- Update docs and app startup logs to show active runtime mode.

## Acceptance Criteria
- Production/default local Camunda scenario uses Camunda adapter.
- In-process runtime is opt-in.
- Startup logs identify active workflow runtime.
- Existing tests pass or are intentionally split by runtime.

## Verification
- `dotnet test Agentwerke.sln`
- Manual Camunda scenario confirms no in-process runtime path is used.

## Suggested Files
- `src/Agentwerke.Workflows/DependencyInjection.cs`
- `src/Agentwerke.Infrastructure/DependencyInjection.cs`
- `src/Agentwerke.Api/appsettings*.json`
- `tests/Agentwerke.Workflows.Tests`
