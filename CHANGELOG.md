# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project aims to adhere to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
from the 1.0 release onward.

## [Unreleased]

Work toward the first stable open-source release (1.0). See the
[v1.0 epic](https://github.com/isartor-ai/autofac-private/issues/162).

### Added
- **Agentwerke public rebrand**: README, website, Web UI chrome, deployment
  examples, Helm chart path/metadata, and runtime-mode docs now use
  Agentwerke by Isartor AI. `WorkflowRuntime:Mode=Autofac` remains a legacy
  alias for the default Agentwerke runtime during the transition.
- **Required label on the GitHub issue trigger** (`Integrations:GitHub:RequiredLabel`,
  default `agentwerke`): an issue must carry the label for its `issues` webhook to
  start a run, so every issue opened on the configured repo no longer spends
  model budget by default. Set to empty to restore the old behavior
  (isartor-ai/autofac-private#191).
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

- **Multiple model providers**: OpenAI, Azure OpenAI, and any LiteLLM proxy via
  `Anthropic:Provider=openai|litellm` (OpenAI Chat Completions-compatible).
- **Per-run cost & token budgets** (`MaxRunCostUsd` / `MaxRunTokens`) that halt a
  run's model calls when exceeded.
- **Knowledge retrieval (RAG)**: `knowledge.search` tool over a pluggable
  `IKnowledgeRetriever` (lexical default), returning snippets with citations.
- **Inter-agent coordination**: `agent.post_message` / `agent.read_messages`
  tools over a run-scoped channel.
- **Per-agent feedback & scorecard**: approval decisions captured as feedback;
  `GET /api/agents/{id}/scorecard` + `POST /api/agents/{id}/feedback`.
- **Policy lifecycle & simulation**: draft → publish (`/api/policies/{id}/publish`
  | `unpublish`) and impact analysis (`/api/policies/simulate`), with a Policies
  admin UI; every decision carries a purpose-confidence + risk score.
- **Interactive Slack approvals**: Approve/Reject buttons + a signature-verified
  `/webhooks/slack/interactions` endpoint (notifications were already present).
- **Jira intake enrichment**: Jira-triggered runs seed issue type/priority/status/
  labels/assignee/reporter into run context.
- **Per-policy/project sandbox provider selection** (Docker / OpenSandbox /
  Kubernetes), plus a kata-isolated Kubernetes executor with NetworkPolicy egress.
- **LDAP/Active Directory** group-to-role mapping for authentication.
- **Production deployment**: Helm chart, single-host production compose, and a
  tag-driven container-publish pipeline; CI gates build/test/lint on every PR.

### Fixed
- Approval gate now creates the approval record before the run is observable as
  `awaiting_approval`, so it is always surfaced by `GET /api/approvals`.
- `RunDispatchWorker` outbox query is server-translatable and drains the outbox
  promptly (run pickup dropped from ~25s to ~1s under load).
- An unresolvable skill referenced by an agent **profile** no longer fails the
  step (only a skill required by the runtime contract does).
- Web UI renders BPMN diagrams that lack embedded layout.

[Unreleased]: https://github.com/isartor-ai/autofac/commits/main
