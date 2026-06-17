# Triage Camunda-first PRs and issue chain after ADR-002

## Context

The recent Camunda-first issues and PRs were created before ADR-002. Some work is still useful as optional adapter groundwork, but anything that makes Camunda the default runtime should stop.

## Scope

- Review open PRs related to issues 93-96.
- Keep BPMN projection work only if it remains runtime-boundary compatible and does not force Camunda as the default.
- Close or convert default Camunda start/run/worker PRs to draft spike status.
- Comment on relevant GitHub issues explaining the ADR-002 direction.
- Park local or remote branches that implement Camunda-only execution until a customer requires the adapter.
- Update issue labels or descriptions if available.

## Acceptance Criteria

- PRs that force Camunda-backed execution are not mergeable into the default path.
- Optional adapter work is clearly titled and described as optional.
- The private issue chain points to ADR-002 and the replacement runtime-strategy issue set.
- No active issue instructs a code agent to make Camunda the default runtime.
