# Accept Camunda 8 as the production BPMN runtime and update architecture docs

## Summary
Record the Camunda 8 production runtime decision and update roadmap documents that still describe the in-process runtime as the MVP execution path.

## Why
The architecture direction has changed: Autofac should use a real BPMN engine now and avoid expanding the custom local runtime. Future agents need a clear written decision so they do not continue building the wrong runtime.

## Scope
- Add or review the ADR for Camunda 8 production runtime.
- Update MVP and architecture docs that still say "local runtime for MVP execution."
- Mark the in-process runtime as test-only or temporary compatibility.
- Link the new implementation, UI, and manual test docs.

## Acceptance Criteria
- Documentation clearly states Camunda 8 is the production execution runtime.
- Documentation clearly states Autofac owns SDLC semantics, agent execution, evidence, policy, and UI.
- The old local-runtime-first plan is either updated or explicitly superseded.
- References point to the implementation plan and manual test scenario.

## Verification
- Review `docs/decisions/ADR-001-use-camunda8-for-production-bpmn-runtime.md`.
- Review `docs/camunda8-sdlc-factory-implementation-plan.md`.
- `rg "Local Autofac runtime for MVP execution|Do not start by replacing|Add Camunda adapter seam and spike" docs` returns no matches.

## Suggested Files
- `docs/decisions/ADR-001-use-camunda8-for-production-bpmn-runtime.md`
- `docs/mvp-implementation-plan.md`
- `docs/architecture-design.md`
- `README.md`
