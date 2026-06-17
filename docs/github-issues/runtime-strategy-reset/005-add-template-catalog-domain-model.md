# Add runtime-neutral SDLC template catalog domain model

## Context

The product should feel like a straightforward SDLC AI tool, not a raw BPMN engine. Users should begin from governed templates and customize their process within safe boundaries.

## Scope

- Add a template catalog model independent of Camunda.
- Seed initial templates such as issue-to-PR, bugfix, hotfix, security review, release approval, and deployment approval.
- Store template metadata: name, description, intended trigger, required inputs, agent roles, approval roles, evidence expectations, policy level, and BPMN XML.
- Expose catalog APIs for listing, previewing, cloning, and publishing templates.
- Keep BPMN XML as the workflow artifact while making template metadata the primary user-facing entry point.

## Acceptance Criteria

- API can list available SDLC templates.
- API can clone a template into an editable workflow draft.
- Template metadata is runtime-neutral and contains no Camunda-specific fields.
- Seeded templates validate against the default-runtime BPMN subset.
