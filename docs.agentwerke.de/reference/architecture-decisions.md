# Architecture Decisions

Architecture decision records live under `docs/decisions` in the repository. This page summarizes the current reader-facing decisions.

## ADR-002: BPMN-centric Agentwerke runtime by default

Agentwerke's default workflow runtime is the in-process, Postgres-backed Agentwerke runtime. It avoids requiring Camunda for the open-source path and keeps the default deployment simpler.

Operational impact:

- `WorkflowRuntime:Mode=Agentwerke` is the default.
- Camunda options should not be read unless `WorkflowRuntime:Mode=Camunda`.
- Health endpoints expose the active runtime mode.

## ADR-001: Camunda 8 for production BPMN runtime

This decision is superseded by ADR-002 for the default runtime. Camunda remains an opt-in enterprise adapter.

Operational impact:

- Existing Camunda documentation should be read as adapter-specific.
- New quickstart and open-core docs should assume the Agentwerke runtime unless explicitly stated otherwise.

## ADR-003: OpenSandbox control plane with Kata runtime

OpenSandbox/Kata is the preferred path for stronger sandbox isolation where plain Docker boundaries are not enough.

Operational impact:

- Docker is useful for local development and simple installs.
- Production environments with stricter egress or isolation requirements should evaluate OpenSandbox or Kubernetes-backed sandboxing.
- Workflow authors should still express intent through `sandboxProfile`.

## When to add an ADR

Add or update an ADR when a change:

- changes a public runtime mode
- changes a security boundary
- changes the deployment model
- changes data residency expectations
- deprecates or supersedes an integration/runtime
- introduces a lasting architectural tradeoff
