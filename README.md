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

## API Startup
1. Run API locally:
	- `dotnet run --project src/Autofac.Api/Autofac.Api.csproj`
2. OpenAPI document (versioned):
	- `/openapi/v1.json`
3. Baseline phase-1 endpoints:
	- `GET /api/health/live`
	- `GET /api/health/ready`
	- `GET /api/auth/config`
	- `POST /api/auth/token`
	- `GET /api/workflows`
	- `GET /api/workflows/{workflowId}`
	- `GET /api/runs`
	- `GET /api/runs/{runId}`
	- `POST /api/runs`
4. Artifact storage by run ID:
	 - Upload artifact bytes:
		 - `POST /api/runs/{runId}/artifacts/{artifactName}`
	 - Download artifact:
		 - `GET /api/runs/{runId}/artifacts/{artifactName}`

See `CONTRIBUTING.md` for contribution standards.

## Persistence
- Phase-1 schema documentation: `docs/persistence-schema.md`

## Architecture Decisions
- Production BPMN runtime decision: `docs/decisions/ADR-001-use-camunda8-for-production-bpmn-runtime.md`
- Camunda 8 implementation plan: `docs/camunda8-sdlc-factory-implementation-plan.md`
- UI cleanup/refactor plan: `docs/ui-cleanup-refactor-plan.md`
- Camunda-backed manual test scenario: `docs/manual-test-camunda8-sdlc-factory.md`

## Local Artifact Storage
- Config section: `Storage:RootPath`
- Defaults:
	- production-like: `./storage`
	- development: `./storage-dev`
