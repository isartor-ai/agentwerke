# Add template-first SDLC factory authoring UI

## Context

Easy use is the key product constraint. Most users should configure their SDLC process by starting from a template, assigning agents and approval owners, and editing policy-relevant choices without needing to understand raw BPMN.

## Scope

- Add a template catalog view as the primary entry point for workflow creation.
- Add a guided template configuration flow for inputs, agent assignments, approval roles, connectors, policy level, and evidence requirements.
- Generate or update the underlying BPMN XML from structured template settings.
- Keep direct BPMN editing available as an advanced mode.
- Use compact, operations-oriented UI patterns consistent with the existing app.
- Avoid Camunda terminology in default UI copy.

## Acceptance Criteria

- A user can create an issue-to-PR style workflow without opening the raw BPMN canvas.
- Agent assignments and approval owners are visible and editable from template settings.
- The generated workflow validates on the default runtime.
- Advanced BPMN editing remains available but is not the default path.
