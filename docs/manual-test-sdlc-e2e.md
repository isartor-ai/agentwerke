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
App/PAT with webhook delivery configured, and a publicly reachable Autofac instance for GitHub to
call back into. Sections below are explicit about what you can verify locally vs. what requires
that infrastructure.

## Known gap found while writing this scenario (see "Gap log" below)

`POST /api/runs` (`StartRunRequest`) takes only a `workflowId` — there is no way to seed custom
run-context input values (e.g. `branch_name`) when starting a templated run, and the GitHub
`issues` webhook trigger (`WebhooksController.HandleIssuesEventAsync` → `SeedTriggerContextAsync`)
only seeds `input.source`/`event_type`/`external_id`/`external_url`/`title`/`body` — never a
custom key like `branch_name`. The template's two wait nodes are written against the *intended*
target design (`correlationKeyTemplate="{{input.branch_name}}"`), but **today, nothing populates
`input.branch_name`**, so that placeholder never resolves to a real value and the live
webhook-driven auto-resume cannot complete for this template as authored. Filed as
[isartor-ai/autofac-private#142](https://github.com/isartor-ai/autofac-private/issues/142). Until
that lands, Part A below uses the **manual resume API** (`POST /api/runs/{runId}/resume-external`)
at both wait points instead of relying on a live GitHub webhook to auto-resume — this still proves
every other stage of the pipeline, and proves the wait/resume mechanics themselves (already
covered by #137/#138's own test suites), just not the "no human in the loop" property for these
two gates specifically.

## Prerequisites

- A running Autofac API + Postgres (the standard `docker/docker-compose.yml` dev stack, or
  `docker/docker-compose.e2e.yml`).
- An operator account (`AutofacPolicies.Operator`/`Approver`) to start runs, decide approvals, and
  call the manual resume endpoint.
- For Part B only: a disposable GitHub repository you control, a GitHub PAT with `repo` scope and
  `workflow_dispatch` permission on a deploy-to-test Actions workflow, and a way to receive GitHub
  webhooks at the Autofac instance (a public URL, or `smee.io`/`ngrok` tunnel to a local instance).

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
  -d '{"workflowId": "<workflowId from step 1>"}'
```

Expected result: `202 Accepted` with a `runId`. Poll `GET /api/runs/{runId}` to follow progress.

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
cross-stage prompt-quality gap this issue exists to catch.

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

### Step 8: Wait for PR merge (manual resume, see the gap note above)

The run is now `waiting_external` on `WaitForMerge`. Confirm via `GET /api/runs/{runId}` that
`status` is `waiting_external`. Resolve it manually:

```bash
curl -X POST http://localhost:8080/api/runs/<runId>/resume-external \
  -H "Content-Type: application/json" \
  -d '{"correlationKey": "{{input.branch_name}}", "payload": {"merged": "true", "pr_number": "1"}}'
```

Note the literal `correlationKey` value: because of the gap above, the checkpoint's recorded
correlation key really is the unresolved template string, not a real branch name — copy it exactly
as shown in the run's `waitingOnNodeId`/checkpoint detail in the API response, don't substitute a
real branch name yourself, or the resume call will 404/mismatch.

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
  Autofac instance's `POST /webhooks/github`, with the shared secret matching
  `Integrations:GitHub:WebhookSecret`.
- **Until the gap above is closed**, the live webhook won't auto-resume the run (its correlation
  hint will be a real branch name, but the checkpoint is waiting on the literal unresolved
  template string) — you'll still need the manual resume calls from Part A, but you can now
  cross-check the *webhook side*: confirm in the Autofac logs that the real `pull_request.merged`
  and `workflow_run.completed` webhooks were received, signature-validated, and recorded as
  `ExternalWorkflowEvent`s (per #136), even though they don't (yet) find a matching waiting run.
- File any further prompt-quality or timing gaps found here the same way as the one above —
  referencing this document and #89.

## Gap log

- **#142** — No mechanism to seed custom run-context input values (e.g. `branch_name`) when
  starting a templated run via `POST /api/runs`, or via the GitHub `issues` webhook trigger. This
  is what currently blocks live webhook auto-resume for the `autonomous-sdlc` template's two wait
  gates. See the note at the top of this document.

## References

- `src/Autofac.Application/Workflows/SdlcTemplateSeeds.cs` — the `AutonomousSdlc` template
- `tests/Autofac.Workflows.Tests/SdlcTemplateSeedsValidationTests.cs` — automated structural
  validation of this template (and every other seeded template) against the real BPMN validator
- `docs/manual-test-opensandbox.md` — the `agent_sandboxed` validation this scenario builds on
- isartor-ai/autofac-private#89 — the parent SDLC epic and original target scenario
- isartor-ai/autofac-private#134–#140 — the phase issues this scenario integrates
