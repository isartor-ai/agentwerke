# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
from the 1.0 release onward.

## [Unreleased]

Work toward the first stable open-source release (1.0). See the
[v1.0 epic](https://github.com/isartor-ai/autofac-private/issues/162).

### Added
- Tokenless **mock model provider** (`Anthropic:Provider=mock`) so full
  workflows run end to end with no API key — for demos and CI.
- **Per-task prompts** on BPMN `agentTask` (child `<autofac:prompt>` element,
  `prompt` / `promptFile` attributes) with `{{input.*}}` / `{{output.*}}`
  run-context interpolation.
- `github.create_pull_request` can **include prior agent output** in the PR
  (`includeAgentOutput` / `outputFrom`).
- Production-grade Anthropic client: `IHttpClientFactory` pipeline with bounded
  retries (429/529/5xx), prompt caching, and richer tool schemas.
- **Apache-2.0 LICENSE**, open-core boundary doc, and community health files
  (security policy, code of conduct, issue templates, this changelog).

### Fixed
- Approval gate now creates the approval record before the run is observable as
  `awaiting_approval`, so it is always surfaced by `GET /api/approvals`.
- `RunDispatchWorker` outbox query is server-translatable and drains the outbox
  promptly (run pickup dropped from ~25s to ~1s under load).
- An unresolvable skill referenced by an agent **profile** no longer fails the
  step (only a skill required by the runtime contract does).
- Web UI renders BPMN diagrams that lack embedded layout.

[Unreleased]: https://github.com/isartor-ai/autofac/commits/main
