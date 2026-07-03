# Manual Test Scenario: End-to-End Autonomous SDLC Workflow

Version: Draft v0.1
Status: Active
Date: 2026-06-24

## Purpose

This is issue #89's own "after Phase E: full scenario runs end-to-end against a test repo"
checkpoint. It walks a single run of the `autonomous-sdlc` template (`SdlcTemplateSeeds.AutonomousSdlc`)
through all 7 stages from #89's target scenario:

`requirement-design` (BA, approval gate) → `architecture-design` (Architect, approval gate) →
`technical-analysis` (Analyst) → `implementation` (Implementation Engineer, sandboxed) →
`senior-review` (Senior Reviewer, sandboxed) → wait for PR merge → `cicd.trigger_deploy` + wait
for CI green → `test` (Tester, sandboxed).

Every node in this template was built and unit/integration-tested by its own phase issue
(#134–#140). This document exists to catch what those per-phase issues can't see in isolation —
prompt quality across the BA→Architect→Analyst handoff, approval-card UX across two real gates
back to back, and timing/correctness of the webhook-resume path. Unlike the other
`docs/manual-test-*.md` scenarios, this one is **not fully automatable without real
infrastructure this repo doesn't own**: a real (or disposable) GitHub repository, a real GitHub
App/PAT with webhook delivery configured, and a publicly reachable Agentwerke instance for GitHub to
call back into. Sections below are explicit about what you can verify locally vs. what requires
that infrastructure.

## Start-time inputs for this scenario

`POST /api/runs` accepts an optional `inputs` object. Each entry is written to run context as
`input.<key>` before the workflow starts, so this scenario can seed `input.branch_name` for the two
external wait gates authored with `correlationKeyTemplate="{{input.branch_name}}"`. The GitHub
`issues` webhook trigger also seeds trigger-derived inputs such as `input.repository` and
`input.issue_url`; template `RequiredInputs` remain descriptive catalog metadata for now.

## Prerequisites

- A running Agentwerke API + Postgres (the standard `docker/docker-compose.yml` dev stack, or
  `docker/docker-compose.e2e.yml`).
- An operator account (`AutofacPolicies.Operator`/`Approver`) to start runs, decide approvals, and
  call the manual resume endpoint.
- For Part B only: a disposable GitHub repository you control, a GitHub PAT with `repo` scope and
  `workflow_dispatch` permission on a deploy-to-test Actions workflow, and a way to receive GitHub
  webhooks at the Agentwerke instance (a public URL, or `smee.io`/`ngrok` tunnel to a local instance).

## Part 0 — Automated agent-layer proof (gated, no infrastructure)

Before the manual full-workflow run below, the **real Claude agent path** can be proven automatically
at the agent layer — no GitHub repo, webhook, or host required. This exercises the production
`AnthropicLanguageModelClient` (through its `IHttpClientFactory` resilience pipeline) driving a real
tool-use loop, which is the core of issue [#143](https://github.com/isartor-ai/autofac-private/issues/143).

The test is gated on an API key so it is a no-op without credentials:

```bash
# Skipped (no-op) when the env var is absent:
dotnet test tests/Autofac.Agents.Tests/Autofac.Agents.Tests.csproj \
  --filter "FullyQualifiedName~RealClaudeIntegrationTests"

# Runs against the real API when a key is present:
AUTOFAC_E2E_ANTHROPIC_API_KEY=sk-ant-... \
  dotnet test tests/Autofac.Agents.Tests/Autofac.Agents.Tests.csproj \
  --filter "FullyQualifiedName~RealClaudeIntegrationTests"
```

It asserts the model invoked the declared tool (`record_decision`), returned a final text reply, and
reported real token usage. Optionally override the model with `AUTOFAC_E2E_ANTHROPIC_MODEL`.

> **Record your run here.** After running with a real key, note the date, model id, and observed
> token usage / approximate cost (the `agent.model.cost_usd` metric, which now includes prompt-cache
> read/write tokens) so this file carries a dated proof:
>
> | Date | Model | Input tok | Output tok | Cache read/write tok | Approx. cost |
> | --- | --- | --- | --- | --- | --- |
> | _pending first real run_ | | | | | |

What this does **not** cover (still manual, see Parts A/B): the full BPMN template, approval-card UX,
and the live webhook-resume path — those need infrastructure this repo does not own.

## Part A — Local validation (manual resume, no real GitHub required)

### Step 1: Clone the template

```bash
curl -X POST http://localhost:8080/api/templates/autonomous-sdlc/clone \
  -H "Content-Type: application/json" \
  -d '{"name": "SDLC E2E Validation Run"}'
```

Expected result: `201 Created` with a `workflowId`. The cloned workflow imports cleanly — this is
the same validation `SdlcTemplateSeedsValidationTests.Template_ValidatesWithoutErrors` already
runs automatically for every seeded template, including this one.

### Step 2: Publish and start a run

Publish the cloned workflow (via the designer UI, or `POST /api/workflows/{workflowId}/publish`),
then:

```bash
curl -X POST http://localhost:8080/api/runs \
  -H "Content-Type: application/json" \
  -d '{
        "workflowId": "<workflowId from step 1>",
        "inputs": {
          "issue_url": "https://github.com/isartor-ai/autofac/issues/<issue-number>",
          "repository": "isartor-ai/autofac",
          "branch_name": "feature/autonomous-sdlc-e2e"
        }
      }'
```

Expected result: `202 Accepted` with a `runId`. Poll `GET /api/runs/{runId}` to follow progress.
The two external wait gates should now render their correlation key as
`feature/autonomous-sdlc-e2e` instead of leaving `{{input.branch_name}}` unresolved.

### Step 3: Requirement design + approval

`RequirementDesign` (agent: `business-analyst`) runs first. Watch for it to complete, then for a
pending approval to appear:

```bash
curl http://localhost:8080/api/approvals | jq '.[] | select(.runId == "<runId>")'
```

What's observable: the approval card shows the rendered requirements spec as the artifact (the
same artifact-aware rendering built in #134) — confirm it's legible Markdown, not raw escaped
text, and that it's actually the BA's output and not empty/placeholder text.

Approve it:

```bash
curl -X POST http://localhost:8080/api/approvals/<approvalId>/decision \
  -H "Content-Type: application/json" \
  -d '{"decision": "approve"}'
```

### Step 4: Architecture design + approval

`ArchitectureDesign` (agent: `solution-architect`) runs next, consuming the approved requirements
spec from run context. Repeat step 3's approval flow for the architecture approval card.

What to watch for specifically: does the architecture spec actually reference the requirements
spec's content, or does it look like it ignored prior-stage context? This is exactly the kind of
cross-stage prompt-quality gap this scenario exists to catch.

### Step 5: Technical analysis

`TechnicalAnalysis` (agent: `technical-analyst`) runs with no approval gate. Confirm it completes
and its output is a plan that references both prior specs.

### Step 6: Implementation (sandboxed)

`Implementation` (agent: `implementation-engineer`, `executionMode="agent_sandboxed"`,
`sandboxProfile="repo-write"`) runs inside a real sandbox container. This is the stage that
exercises #140's code-writing tools for real — confirm via `runtimeSnapshot.toolInvocations` on
the step that `sandbox.git`, `sandbox.file_write`/`sandbox.file_edit`, and
`github.create_pull_request` were actually called, not just `sandbox.shell` busywork. If you have
real GitHub credentials configured (`Integrations:GitHub:*`), this opens a real draft PR — note
its branch name for step 8.

### Step 7: Senior review (sandboxed)

`SeniorReview` (agent: `senior-code-reviewer`, `sandboxProfile="repo-read"`) runs next. Confirm via
`toolInvocations` that it actually re-ran the build/tests via `sandbox.shell` rather than approving
on faith, and that `github.post_review` was called.

### Step 8: Wait for PR merge

The run is now `waiting_external` on `WaitForMerge`. Confirm via `GET /api/runs/{runId}` that
`status` is `waiting_external`. Resolve it manually:

```bash
curl -X POST http://localhost:8080/api/runs/<runId>/resume-external \
  -H "Content-Type: application/json" \
  -d '{"correlationKey": "feature/autonomous-sdlc-e2e", "payload": {"merged": "true", "pr_number": "1"}}'
```

Use the same branch name you provided in the start-run `inputs.branch_name` value.

### Step 9: Trigger deploy

`TriggerDeploy` (agent: `deploy-agent`, action `cicd.trigger_deploy`) runs immediately after resume
— this is a direct in-process tool call (#139), not sandboxed, so it should complete in well under
a second. Without real GitHub Actions credentials configured, expect a clean failure here
(`"GitHub repository owner/name is not configured"` or similar) — that's expected in Part A; see
Part B to exercise this for real.

### Step 10: Wait for CI green (manual resume)

Same mechanics as step 8, against `WaitForCiGreen`, with a payload resembling a real
`workflow_run.completed` event (`{"status": "completed", "conclusion": "success"}`).

### Step 11: Test (sandboxed)

`Test` (agent: `tester`, `sandboxProfile="repo-read"`) runs last. Confirm via `toolInvocations`
that `sandbox.run_tests` actually ran a real test command and that the step's final output clearly
states pass/fail — not just "done."

Expected end state: `GET /api/runs/{runId}` shows `status: completed`, all 12 non-gateway nodes
present in `steps` with `status: completed`, and both approval decisions plus both external-event
resumes visible in the run's event history.

## Part B — Full validation against a real GitHub repository

Repeat steps 6 through 10 above, but:

- Configure real `Integrations:GitHub:*` settings (owner/repo/PAT) pointing at your disposable test
  repository, per `docs/manual-test-opensandbox.md`'s pilot-stack credential pattern.
- Configure a real deploy-to-test GitHub Actions workflow in that repo (matching
  `GitHubOptions.DeployWorkflowFileName`, default `deploy-to-test.yml`).
- Configure the repo's webhook deliveries (`pull_request`, `workflow_run`) to point at your
  Agentwerke instance's `POST /webhooks/github`, with the shared secret matching
  `Integrations:GitHub:WebhookSecret`.
- Confirm the implementation stage creates or uses the same branch name you seeded as
  `inputs.branch_name`. When it does, the real `pull_request.merged` and
  `workflow_run.completed` webhook deliveries should be received, signature-validated, recorded as
  `ExternalWorkflowEvent`s (per #136), matched against the waiting correlation, and used to
  auto-resume the run.
- File any further prompt-quality or timing gaps found here referencing this document and #89.

## Gap log

- **#142** — Resolved by start-run `inputs` seeding and trigger-derived issue inputs. This lets
  `input.branch_name` resolve for the `autonomous-sdlc` template's two wait gates when callers
  provide the branch name at run start.

## References

- `src/Autofac.Application/Workflows/SdlcTemplateSeeds.cs` — the `AutonomousSdlc` template
- `tests/Autofac.Workflows.Tests/SdlcTemplateSeedsValidationTests.cs` — automated structural
  validation of this template (and every other seeded template) against the real BPMN validator
- `docs/manual-test-opensandbox.md` — the `agent_sandboxed` validation this scenario builds on
- isartor-ai/autofac-private#89 — the parent SDLC epic and original target scenario
- isartor-ai/autofac-private#134–#140 — the phase issues this scenario integrates
