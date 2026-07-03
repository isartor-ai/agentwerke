# Runtime Strategy Reset GitHub Issue Drafts

This directory contains the replacement implementation plan for the 2026-06-17 architecture decision:

- Agentwerke remains BPMN-centric.
- The bounded Postgres-backed Agentwerke runtime is the default runtime for MVP, pilots, and first self-hosted deployments.
- Camunda 8 is an optional enterprise adapter, not the default execution engine.
- Product work should prioritize easy-to-use SDLC templates, real agent execution, authentication/RBAC, evidence, and default-runtime conformance.

Target repository:

```text
isartor-ai/autofac-private
```

Create or sync the issues with:

```bash
GH_TOKEN=... scripts/create-runtime-strategy-github-issues.sh
```

The issue bodies are scoped so autonomous code agents can implement one issue at a time.

Created GitHub issues:

1. [#112 Supersede Camunda-first runtime decision and align architecture docs](https://github.com/isartor-ai/autofac-private/issues/112)
2. [#113 Triage Camunda-first PRs and issue chain after ADR-002](https://github.com/isartor-ai/autofac-private/issues/113)
3. [#114 Add runtime-mode configuration with Agentwerke default and Camunda opt-in](https://github.com/isartor-ai/autofac-private/issues/114)
4. [#115 Add default-runtime BPMN template conformance tests](https://github.com/isartor-ai/autofac-private/issues/115)
5. [#116 Add runtime-neutral SDLC template catalog domain model](https://github.com/isartor-ai/autofac-private/issues/116)
6. [#117 Add template-first SDLC factory authoring UI](https://github.com/isartor-ai/autofac-private/issues/117)
7. [#118 Adapt Workflow Designer to advanced BPMN mode](https://github.com/isartor-ai/autofac-private/issues/118)
8. [#119 Prioritize real agent execution for pilot readiness](https://github.com/isartor-ai/autofac-private/issues/119)
9. [#120 Prioritize enterprise authentication, authorization, and data residency](https://github.com/isartor-ai/autofac-private/issues/120)
10. [#121 Build default-runtime evidence pack and audit export](https://github.com/isartor-ai/autofac-private/issues/121)

Camunda-first triage performed:

- Public PR [#43](https://github.com/isartor-ai/autofac/pull/43) was renamed and reframed as optional Camunda projection groundwork.
- Public PR [#44](https://github.com/isartor-ai/autofac/pull/44) was closed because it moved Camunda deployment/start behavior into the run path before ADR-002 and runtime-mode gating.
- Private issue [#93](https://github.com/isartor-ai/autofac-private/issues/93) was renamed as optional compatibility projection.
- Private issues [#94](https://github.com/isartor-ai/autofac-private/issues/94), [#95](https://github.com/isartor-ai/autofac-private/issues/95), and [#96](https://github.com/isartor-ai/autofac-private/issues/96) were parked and closed as not planned until explicit Camunda adapter mode is justified.
