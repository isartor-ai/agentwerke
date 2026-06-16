# Manual Test Scenario: Camunda 8 SDLC Factory Flow

Version: Draft v0.2
Status: Bootstrap in progress
Date: 2026-06-16

## Purpose
This manual test proves the first real dark software factory slice using Camunda 8 as the BPMN runtime.

The target flow is:

```
Manual issue input
  -> Analyze requirement agent task
  -> Approve technical spec user task
  -> Implement change agent task
  -> Run tests agent task
  -> Create pull request agent task
  -> Final review user task
  -> Complete
```

The scenario should use controlled no-op or simulated agents until real agent execution is enabled. The important proof is that Camunda owns workflow execution while Autofac owns agent execution, approval, evidence, and run visibility.

## Prerequisites

- Docker and Docker Compose are installed.
- Autofac API, web UI, database, and Camunda profile can run locally.
- Tickets after `#91` are required before the full Camunda-backed workflow path works end to end.

## Current Scope

Issue `#91` enables only the local Camunda runtime bootstrap:

- start the manual stack with a Zeebe profile
- verify Zeebe readiness and topology from the host machine
- keep the existing non-Camunda manual stack working

The remaining workflow publish, start, worker, and approval steps in this document describe the intended future scenario once the later Camunda issues are implemented.

## Step 1: Start the Camunda-backed stack

Command:

```bash
docker compose -f docker/docker-compose.manual.yml --profile camunda up --build
```

Expected result:

- Autofac API is healthy.
- Autofac web UI is reachable.
- Camunda runtime is reachable.
- The existing manual stack still works whether the `camunda` profile is enabled or not.

## Step 2: Verify runtime health

Check:

- API liveness endpoint returns healthy.
- Camunda topology endpoint responds:

```bash
curl -sf http://localhost:8088/v2/topology | jq .
```

- Camunda readiness endpoint responds:

```bash
curl -sf http://localhost:9600/ready
```

Expected result:

- Camunda Zeebe reports a healthy single-node topology.
- Zeebe readiness responds successfully.
- The stack is ready for later adapter/worker tickets.

## Step 3: Open the Factory workflow builder

Note:

This and the following steps are target-state manual checks for the later Camunda integration tickets. They are not expected to pass yet immediately after issue `#91`.

In the web UI:

1. Open Factory.
2. Select the issue-to-PR SDLC template.
3. Review stages, agents, approvals, and evidence requirements.

Expected result:

- Template opens without validation errors.
- Agent tasks show assigned agents.
- Approval gates show required decision context.

## Step 4: Validate and publish to Camunda

In the web UI:

1. Click Validate.
2. Review Autofac validation results.
3. Click Publish.
4. Confirm Camunda deployment success.

Expected result:

- Autofac validation passes.
- Camunda deployment succeeds.
- Workflow status becomes published.
- Publish details include deployment/process identifiers for operator diagnostics.

## Step 5: Start a test run

Start the workflow manually with input:

- Title: "Add audit trail for approval decisions"
- Body: "Every approval decision should capture who decided, what they decided, when, and why."
- External URL: a placeholder issue URL

Expected result:

- Run is created in Autofac.
- Camunda process instance starts.
- Run detail shows the first stage as active or completed.

## Step 6: Observe first agent job

Open Run Detail.

Expected result:

- Analyze requirement step runs through the Autofac agent worker.
- Worker records job activation and completion events.
- Agent output is visible as a spec or analysis artifact.
- Evidence produced is visible.

## Step 7: Approve the technical spec

Open Approvals.

1. Find the pending spec approval.
2. Open the approval detail.
3. Review agent output, risk, evidence, and related run.
4. Approve with a rationale.

Expected result:

- Approval status changes to approved.
- Camunda user task completes.
- Workflow advances to implementation.
- Run event log records the approval decision.

## Step 8: Observe implementation and test agent jobs

Return to Run Detail.

Expected result:

- Implementation agent task completes.
- Test agent task completes.
- Evidence includes implementation summary and test result references.
- Camunda and Autofac states remain consistent.

## Step 9: Observe PR creation task

Expected result:

- PR creation task runs through an Autofac job worker.
- If GitHub is simulated, the artifact records a fake PR URL.
- If GitHub credentials are configured, a real branch and PR are created.
- Run detail shows PR evidence.

## Step 10: Complete final review

In Approvals:

1. Open final review approval.
2. Review evidence and PR link.
3. Approve or reject.

Expected result for approve:

- Workflow reaches completed state.
- Run detail shows completed status and completion time.
- Evidence chain includes spec, implementation summary, tests, PR, and approvals.

Expected result for reject:

- Workflow follows the configured rejection behavior.
- Run detail clearly shows who rejected and why.

## Step 11: Test failure path

Start another run with a worker setting or input that causes the implementation agent job to fail.

Expected result:

- Camunda retries according to configured retries.
- Autofac run detail shows retry attempts.
- If retries are exhausted, run shows incident or blocked state.
- Operator can see failure reason and affected step.

## Step 12: Test validation failure

Edit the template to remove required agent metadata from one service task.

Expected result:

- Autofac validation blocks publish.
- Error identifies the affected step.
- Camunda deployment is not attempted.

## Pass Criteria

The manual test passes when:

- Camunda is the active execution runtime.
- Workflow publish deploys to Camunda.
- Agent service tasks execute through Autofac workers.
- User tasks create Autofac approvals.
- Approval decisions advance the Camunda process.
- Evidence and artifact references are visible.
- Failure and retry state are visible to the operator.
- The whole flow can be completed from the UI without raw BPMN or Camunda knowledge.

## Notes

This scenario replaces the old in-process runtime manual approval scenario as the product-direction test. The older scenario can remain for regression coverage until the in-process runtime is removed or test-only.
