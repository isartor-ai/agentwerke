# Add runtime-mode configuration with Autofac default and Camunda opt-in

## Context

Autofac needs a clear runtime boundary so the product can run on the default Postgres-backed runtime while preserving space for optional Camunda support.

## Scope

- Add configuration such as `WorkflowRuntime:Mode = Autofac | Camunda`.
- Default to `Autofac` when configuration is absent.
- Ensure Camunda-specific services, publish/start paths, health checks, and UI/status fields are only active in `Camunda` mode.
- Add startup logging and health diagnostics showing the active runtime mode.
- Add validation so unsupported runtime values fail fast with an actionable error.
- Update developer documentation and sample configuration.

## Acceptance Criteria

- A clean local deployment starts in `Autofac` runtime mode with no Camunda dependency.
- Setting `WorkflowRuntime:Mode=Camunda` is explicit and required before Camunda paths are used.
- Tests prove default mode does not call Camunda configuration, clients, or worker code.
- Runtime mode is visible in diagnostics or health output.
