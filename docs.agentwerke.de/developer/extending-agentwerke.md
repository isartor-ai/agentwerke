# Extending Agentwerke

Agentwerke is designed around explicit boundaries. Add extensions where the existing layer owns the concern.

## Add a workflow capability

Use `src/Agentwerke.Workflows` when changing BPMN validation, runtime behavior, step advancement, timers, external events, or runtime mode selection.

Checklist:

- Add or update validation for any new BPMN extension fields.
- Persist enough state to resume after process restart.
- Include evidence for new runtime behavior.
- Add tests for happy path, invalid input, and resume behavior.

## Add an agent tool

Use `src/Agentwerke.Agents` and the Tool Gateway boundary.

Checklist:

- Define the tool contract clearly.
- Require explicit agent permission.
- Evaluate policy before execution.
- Record invocation and result metadata.
- Redact sensitive inputs and outputs.
- Add tests that prove blocked, escalated, and allowed paths.

## Add an integration

Use `src/Agentwerke.Integrations` for external systems such as source control, planning tools, chat systems, and CI/CD.

Checklist:

- Keep credentials in the secret store or deployment environment.
- Add readiness checks where operators need them.
- Make operations idempotent where possible.
- Record external identifiers in run evidence.
- Surface retryable versus terminal errors distinctly.

## Add a sandbox provider

Use `src/Agentwerke.Sandboxes`.

Checklist:

- Define provider options and readiness checks.
- Map Agentwerke sandbox profiles to provider capabilities.
- Enforce filesystem and network expectations.
- Pass only required environment variables and secrets.
- Capture execution logs and exit status for evidence.

## Add storage behavior

Use `src/Agentwerke.Storage` for artifact and blob behavior.

Checklist:

- Keep artifact identity tied to run id.
- Avoid leaking local paths in public evidence where not needed.
- Support download and retention requirements.
- Add tests for missing objects, overwrite behavior, and provider errors.

## Add web UI behavior

Use `web/` for product UI changes.

Checklist:

- Match existing React and Vite patterns.
- Keep operational screens dense and scannable.
- Verify keyboard access for interactive controls.
- Run lint, tests, and build.
- Browser-test the flow with real console and layout checks.

## Add docs behavior

Use `docs.agentwerke.de/` for the public manual site and `docs/` for raw design or planning material that is not ready for the manual.

Checklist:

- Keep public docs task-oriented.
- Label enterprise-only capabilities.
- Avoid copying outdated planning statements into the manual.
- Run the docs build and inspect links before publishing.
