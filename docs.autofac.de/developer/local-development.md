# Local Development

Autofac is a .NET solution with a React/Vite web UI and Docker-based local stacks.

## Prerequisites

- .NET SDK from `global.json`
- Node.js 20 for the web UI and docs site
- Docker and Docker Compose for local stacks
- PostgreSQL when running without Compose

## Build the solution

From the repository root:

```bash
dotnet restore Autofac.sln
dotnet build Autofac.sln
dotnet test Autofac.sln --no-build
```

The main CI path excludes the E2E suite unless the full docker stack is running.

## Run the API

```bash
dotnet run --project src/Autofac.Api/Autofac.Api.csproj
```

The OpenAPI document is served at:

```text
/openapi/v1.json
```

## Run the web UI

```bash
cd web
npm ci
npm run dev
```

The Vite dev server proxies `/api` to the local API when `VITE_API_BASE_URL` is not set.

## Run the docs site

```bash
cd docs.autofac.de
npm ci
npm run dev
```

Build the static docs artifact:

```bash
npm run build
```

Preview the built artifact:

```bash
npm run preview
```

## Common local stacks

| Stack | Command |
| --- | --- |
| Quickstart | `docker compose -f docker/docker-compose.quickstart.yml up --build` |
| Development | `docker compose -f docker/docker-compose.yml up --build` |
| Manual testing | `docker compose -f docker/docker-compose.manual.yml up --build` |
| E2E | `docker compose -f docker/docker-compose.e2e.yml up --build` |

## Repository map

| Path | Purpose |
| --- | --- |
| `src/Autofac.Api` | ASP.NET Core host. |
| `src/Autofac.Application` | Use cases and orchestration contracts. |
| `src/Autofac.Domain` | Domain model. |
| `src/Autofac.Infrastructure` | EF Core, Postgres, outbox, dispatch worker. |
| `src/Autofac.Workflows` | BPMN validation and workflow engine. |
| `src/Autofac.Agents` | Agent orchestration, model clients, gateways, prompt assembly. |
| `src/Autofac.AgentSecOps` | Policy evaluation and governance. |
| `src/Autofac.Sandboxes` | Sandbox lifecycle providers. |
| `src/Autofac.Integrations` | GitHub, CI/CD, and external connectors. |
| `src/Autofac.Storage` | Artifact and blob abstractions. |
| `src/Autofac.Observability` | Logs, metrics, and tracing. |
| `web` | React/Vite web UI. |
| `docs.autofac.de` | Static documentation site. |
| `docs` | Raw design, operation, and reference docs used as source material. |

## Development safety

- Do not commit local settings or secret files.
- Keep agent/tool permissions narrow when adding workflows.
- Use the mock provider for deterministic tests before using a real model.
- Verify evidence-pack behavior when adding new agent interactions, tools, or policy decisions.
- Run relevant tests before opening a PR.
