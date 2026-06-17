# Superseded Camunda 8 SDLC Factory GitHub Issue Drafts

This directory contains the original Camunda-first issue drafts. They are retained for traceability only.

As of 2026-06-17, `docs/decisions/ADR-001-use-camunda8-for-production-bpmn-runtime.md` is superseded by `docs/decisions/ADR-002-use-bpmn-centric-autofac-runtime-by-default.md`.

The current strategy is:

- Keep Autofac BPMN-centric.
- Use the bounded Postgres-backed Autofac runtime as the default runtime for MVP, pilots, and first self-hosted deployments.
- Treat Camunda 8 as an optional enterprise adapter, enabled only through explicit configuration and customer need.
- Prioritize template-first SDLC authoring, real agent execution, authentication/RBAC, evidence, and default-runtime conformance before any Camunda expansion.

Target repository:

```text
isartor-ai/autofac-private
```

Do not create or continue this issue set as the default implementation plan. Use the replacement issue drafts in:

```text
docs/github-issues/runtime-strategy-reset
```

Legacy creation command, kept only for historical reproduction:

```bash
GH_TOKEN=... scripts/create-camunda8-github-issues.sh
```

The script also accepts overrides:

```bash
scripts/create-camunda8-github-issues.sh isartor-ai/autofac-private docs/github-issues/camunda8-sdlc-factory
```

The issue bodies are intentionally small enough for autonomous code agents to implement one issue at a time.

## Triage

| Legacy draft | Action |
| --- | --- |
| `001-docs-accept-camunda8-runtime.md` | Superseded by ADR-002 |
| `002-add-camunda8-local-runtime-profile.md` | Park as optional adapter infrastructure |
| `003-add-camunda-config-and-client.md` | Park as optional adapter infrastructure |
| `004-project-autofac-bpmn-to-camunda.md` | Reframe as optional Camunda compatibility projection |
| `005-deploy-workflows-to-camunda.md` | Park; only valid after explicit Camunda runtime mode |
| `006-start-camunda-process-instances.md` | Park; do not merge into default run path |
| `007-camunda-agent-job-worker.md` | Park as adapter spike/reference |
| `008-map-agent-outcomes-to-camunda-jobs.md` | Park as adapter spike/reference |
| `009-bridge-camunda-user-tasks-to-approvals.md` | Park as adapter spike/reference |
| `010-persist-camunda-evidence-references.md` | Reframe as default-runtime evidence and optional Camunda variable mapping |
| `011-manual-start-input-mapping.md` | Reframe as runtime-neutral workflow start input mapping |
| `012-camunda-issue-to-pr-template.md` | Reframe as default Autofac template with optional Camunda compatibility |
| `013-make-camunda-default-runtime.md` | Superseded; should not be implemented |
| `014-camunda-e2e-workflow-test.md` | Park; replace with default-runtime template conformance tests |
| `015-camunda-incidents-run-detail.md` | Park; replace with runtime-neutral run operations UI |
| `016-refactor-workflow-designer-modules.md` | Keep; runtime-neutral UI refactor |
| `017-template-first-factory-ui.md` | Keep; becomes core product priority |
| `018-camunda-validation-publish-status-ui.md` | Park; replace with runtime-mode diagnostics |
| `019-run-board-detail-operations-ui.md` | Keep; runtime-neutral operations priority |
| `020-approvals-production-inbox.md` | Keep; runtime-neutral approvals priority |
| `021-compact-ui-primitives-remove-mocks.md` | Keep; runtime-neutral UI cleanup |
| `022-manual-test-camunda-flow.md` | Park; replace with default-runtime manual test scenario |
