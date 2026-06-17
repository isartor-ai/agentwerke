# Adapt Workflow Designer to advanced BPMN mode

## Context

The existing BPMN designer is powerful, but exposing it as the first workflow creation experience makes the product feel like a BPMN modeling tool rather than an SDLC AI factory.

## Scope

- Refactor the designer so it can be launched from a workflow draft as advanced editing mode.
- Make validation feedback distinguish default-runtime errors from optional-adapter compatibility warnings.
- Keep Autofac custom tasks and approval gates visible in BPMN mode.
- Hide Camunda-specific projection, deployment, and runtime fields unless Camunda mode is active.
- Preserve round-trip BPMN XML editing and existing validation overlays.

## Acceptance Criteria

- Template-first authoring can open the BPMN designer for advanced edits.
- Default-runtime validation messages are clear and actionable.
- Camunda-specific UI is absent unless the runtime mode is explicitly Camunda.
- Existing bpmn-js tests continue to pass.
