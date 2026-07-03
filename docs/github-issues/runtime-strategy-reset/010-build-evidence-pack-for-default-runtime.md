# Build default-runtime evidence pack and audit export

## Context

Agentwerke should make SDLC automation auditable. Evidence must be first-class in the default runtime, not a Camunda-only variable mapping concern.

## Scope

- Define an evidence pack model for workflow runs.
- Include workflow version, BPMN XML hash, runtime mode, agent snapshots, approvals, policy decisions, tool calls, connector calls, artifacts, logs, and relevant run events.
- Add API endpoints to generate and download evidence packs.
- Add UI affordances in Run Detail for evidence status and export.
- Ensure evidence generation works without Camunda.
- Add optional adapter hooks so Camunda metadata can be included when Camunda mode is active.

## Acceptance Criteria

- A completed default-runtime run can export an evidence pack.
- The evidence pack contains enough data to reconstruct who approved what, which agents ran, which tools were called, and which artifacts were produced.
- Evidence export tests run without Camunda.
- Run Detail exposes evidence export in an operations-friendly way.
