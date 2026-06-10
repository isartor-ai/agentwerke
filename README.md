# Autofac

Secure, BPMN-native, Docker-sandboxed autonomous software factory.

## Repository Layout
- `src/Autofac.Api` - ASP.NET Core API host
- `src/Autofac.Application` - Application use cases and orchestration contracts
- `src/Autofac.Domain` - Core domain model and rules
- `src/Autofac.Infrastructure` - Infrastructure adapters and implementations
- `src/Autofac.Workflows` - BPMN runtime concerns
- `src/Autofac.Agents` - Agent orchestration components
- `src/Autofac.AgentSecOps` - Policy enforcement and action governance
- `src/Autofac.Sandboxes` - Sandbox lifecycle and controls
- `src/Autofac.Integrations` - External platform connectors
- `src/Autofac.Storage` - Artifact and blob abstractions
- `src/Autofac.Observability` - Logging, metrics, and tracing wiring
- `tests/Autofac.Domain.Tests` - Domain-focused tests

## Quick Start
1. `dotnet restore Autofac.sln`
2. `dotnet build Autofac.sln`
3. `dotnet test Autofac.sln --no-build`

See `CONTRIBUTING.md` for contribution standards.
