# Add default-runtime BPMN template conformance tests

## Context

The default runtime should not become a general BPMN engine. It must reliably execute the curated SDLC templates Agentwerke ships and reject unsupported BPMN constructs before publish.

## Scope

- Define the supported BPMN subset for the default runtime in test fixtures.
- Add conformance tests for each built-in SDLC template.
- Cover start/end events, service tasks, user approval tasks, exclusive gateways, parallel gateways, timers, boundary timeout behavior, retries, checkpoints, resume, and recovery where templates use them.
- Add negative tests for unsupported constructs such as compensation, complex event subprocesses, ad hoc subprocesses, and any unimplemented expression language.
- Ensure publish validation fails clearly for unsupported constructs.

## Acceptance Criteria

- Every built-in template has a default-runtime conformance test.
- Unsupported BPMN constructs fail validation before publish.
- Tests assert persisted run events/checkpoints where behavior depends on recovery.
- The conformance suite runs in CI without Camunda.
