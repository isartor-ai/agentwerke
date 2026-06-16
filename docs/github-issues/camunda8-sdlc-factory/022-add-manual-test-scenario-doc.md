# Add manual test scenario for Camunda-backed SDLC factory flow

## Summary
Create and maintain a manual test scenario that proves the Camunda-backed issue-to-PR factory flow from UI authoring through workflow completion.

## Why
Users need a concrete test path to verify that the product works as a dark software factory, not just isolated APIs.

## Scope
- Document stack startup with the Camunda profile.
- Document workflow template selection, validation, publish, start, approval, and completion.
- Include success, rejection, retry/incident, and validation failure paths.
- Update scenario as implementation tasks land.

## Acceptance Criteria
- Manual scenario can be followed by a user without reading source code.
- Scenario proves Camunda is the active runtime.
- Scenario covers agent service tasks and approval user tasks.
- Scenario includes pass/fail criteria.

## Verification
- Follow `docs/manual-test-camunda8-sdlc-factory.md` end-to-end after implementation.
- Confirm screenshots or notes can be added later without changing the flow.

## Suggested Files
- `docs/manual-test-camunda8-sdlc-factory.md`
- `docs/manual-test-scenario.md`
- `README.md`
