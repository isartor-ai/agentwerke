# Supersede Camunda-first runtime decision and align architecture docs

## Context

ADR-001 selected Camunda 8 as the production BPMN runtime. The revised decision keeps Agentwerke BPMN-centric but makes the bounded Postgres-backed Agentwerke runtime the default runtime for MVP, pilots, and first self-hosted deployments.

## Scope

- Mark ADR-001 as superseded.
- Add ADR-002 documenting the default Agentwerke runtime and optional Camunda adapter policy.
- Update `docs/architecture-design.md` so it no longer describes Camunda 8 as the default production runtime.
- Update roadmap language so real agents, auth/RBAC, evidence, templates, and runtime conformance are the primary product path.
- Keep references to Camunda only where it is explicitly described as an optional enterprise adapter.

## Acceptance Criteria

- ADR-001 points to ADR-002.
- ADR-002 is accepted and explains the rationale, runtime scope, adapter policy, re-decision triggers, and consequences.
- Architecture docs consistently describe the Agentwerke/Postgres runtime as the default.
- No roadmap section says Camunda migration is on the default critical path.
- Old Camunda-first issue drafts are marked as superseded or parked.
