# Manual Test Scenario — Production Deployment Approval

## Overview

This scenario walks through the full workflow lifecycle using the
`manual-test-deploy-approval.bpmn` file:

```
Start → Pre-flight Check → SAST Scan → [APPROVAL GATE] → Execute Deployment → End
```

Two service tasks run automatically with **simulated output** (no real agents or LLMs
are invoked). The workflow pauses at the `userTask` and waits for a human decision.
You then approve or reject it and watch the run complete or cancel.

**What you need:** Docker + Docker Compose. No credentials, no cloud accounts.

---

## Step 0 — Start the stack

```bash
docker compose -f docker/docker-compose.manual.yml up --build
```

Wait until you see:
```
api-1  | Now listening on: http://[::]:8080
```

Services once up:

| URL | What it is |
|-----|-----------|
| http://localhost:3002 | Web UI |
| http://localhost:8081 | API base |
| http://localhost:8081/api/health/live | Liveness check |
| http://localhost:9090/__admin/mappings | WireMock stub inspector |

Verify the API is ready:
```bash
curl -sf http://localhost:8081/api/health/live | jq .
```
Expected: `{"status":"live"}`

---

## Step 1 — Import the BPMN workflow

Read the BPMN file into a variable and POST it to the import endpoint.
The API assigns a generated ID (e.g. `wf_abc123...`) — capture it from the response.

```bash
BPMN_XML=$(cat docs/manual-test-deploy-approval.bpmn)

IMPORT=$(curl -sf -X POST http://localhost:8081/api/workflows/import \
  -H "Content-Type: application/json" \
  -d "{\"fileName\": \"manual-test-deploy-approval.bpmn\", \"bpmnXml\": $(echo "$BPMN_XML" | jq -Rs .)}")

echo "$IMPORT" | jq .

WORKFLOW_ID=$(echo "$IMPORT" | jq -r .workflowId)
echo "Workflow ID: $WORKFLOW_ID"
```

Expected response:
```json
{
  "workflowId": "wf_4bc67702c49941bc996ba87ef175528a",
  "validation": {
    "isValid": true,
    "processId": "manual-test-deploy-approval",
    "processName": "Production Deployment Approval",
    "errors": [],
    "warnings": [ ... ]
  }
}
```

> **Note:** The `workflowId` is a generated UUID, not the BPMN process id. Always
> capture it from the import response — or look it up later with `GET /api/workflows`.

> **Tip:** The import endpoint is idempotent — re-running it creates a new version of the same workflow.

---

## Step 2 — Validate (optional)

Check the BPMN parses cleanly before publishing:

```bash
BPMN_XML=$(cat docs/manual-test-deploy-approval.bpmn)

curl -sf -X POST http://localhost:8081/api/workflows/validate \
  -H "Content-Type: application/json" \
  -d "{\"bpmnXml\": $(echo "$BPMN_XML" | jq -Rs .)}" \
  | jq '{isValid, errors, warnings: (.warnings | length)}'
```

Expected: `isValid: true` with 0 errors.

---

## Step 3 — Publish the workflow

Publishing locks in the BPMN version and makes it runnable.

```bash
BPMN_XML=$(cat docs/manual-test-deploy-approval.bpmn)

curl -sf -X POST "http://localhost:8081/api/workflows/$WORKFLOW_ID/publish" \
  -H "Content-Type: application/json" \
  -d "{\"bpmnXml\": $(echo "$BPMN_XML" | jq -Rs .), \"description\": \"Initial publish for manual test\"}" \
  | jq .
```

Expected response:
```json
{
  "workflowId": "manual-test-deploy-approval",
  "version": 1,
  "publishedAt": "2026-06-14T..."
}
```

---

## Step 4 — Start a run

Kick off the workflow. The engine immediately executes the two service tasks
(`PreflightCheck` and `SastScan`) with simulated output, then pauses at the
`ApproveDeployment` user task.

```bash
RUN=$(curl -sf -X POST http://localhost:8081/api/runs \
  -H "Content-Type: application/json" \
  -d "{\"workflowId\": \"$WORKFLOW_ID\"}" \
  | jq .)

echo "$RUN" | jq .

RUN_ID=$(echo "$RUN" | jq -r .runId)
echo "Run ID: $RUN_ID"
```

Expected response:
```json
{
  "runId": "run-xxxx",
  "workflowId": "manual-test-deploy-approval",
  "status": "awaiting_approval"
}
```

The status `awaiting_approval` confirms the workflow has passed the two automated
tasks and is now waiting at the human approval gate.

---

## Step 5 — Inspect the run

View run details, timeline of events, and completed steps:

```bash
curl -sf "http://localhost:8081/api/runs/$RUN_ID" | jq '{
  status,
  currentStep,
  steps: [.steps[] | {name, status, agentName}],
  events: [.events[] | {type, message}]
}'
```

You should see:

- `PreflightCheck` → `completed`
- `SastScan` → `completed`
- `ApproveDeployment` → `running` (waiting for user input)
- `status: "awaiting_approval"`

---

## Step 6 — Find the pending approval

```bash
APPROVAL=$(curl -sf http://localhost:8081/api/approvals \
  | jq "[.[] | select(.runId == \"$RUN_ID\") | select(.status == \"pending\")] | first")

echo "$APPROVAL" | jq .

APPROVAL_ID=$(echo "$APPROVAL" | jq -r .id)
echo "Approval ID: $APPROVAL_ID"
```

Inspect the approval details — risk level, risk score, affected systems:

```bash
echo "$APPROVAL" | jq '{
  actionRequested,
  riskLevel,
  riskScore,
  riskFactors,
  affectedSystems,
  agentName,
  policyRationale
}'
```

Expected: `riskLevel: "high"` (because `policyTag` contains `"critical"` and `"prod"`).

---

## Step 7A — Approve the deployment

```bash
curl -sf -X POST "http://localhost:8081/api/approvals/$APPROVAL_ID/decision" \
  -H "Content-Type: application/json" \
  -d '{"decision": "approve", "comment": "Looks good, pre-flight and SAST passed."}' \
  | jq .
```

After approval the engine resumes from `ExecuteDeployment`, runs it with simulated
output, then reaches `End`. Poll the run to confirm:

```bash
curl -sf "http://localhost:8081/api/runs/$RUN_ID" | jq '{status, completedAt}'
```

Expected: `status: "completed"`.

---

## Step 7B — Reject the deployment (alternative path)

To test the rejection path, start a **fresh run** (Step 4 again) and reject:

```bash
curl -sf -X POST "http://localhost:8081/api/approvals/$APPROVAL_ID/decision" \
  -H "Content-Type: application/json" \
  -d '{"decision": "reject", "comment": "SAST scan output needs review first."}' \
  | jq .
```

Poll the run:
```bash
curl -sf "http://localhost:8081/api/runs/$RUN_ID" | jq '{status, completedAt}'
```

Expected: `status: "cancelled"`.

---

## Step 8 — Check metrics (observability)

The Prometheus scraping endpoint exposes counters for run lifecycle events.
The OTel exporter uses the instrument name as prefix (not the meter name), so
metrics start with `workflow_`, tagged with `otel_scope_name="Autofac.Workflows"`.

```bash
curl -sf http://localhost:8081/metrics | grep 'otel_scope_name="Autofac' | grep -v "bucket\|_sum\|_count"
```

You should see counters like:
```
workflow_runs_started_total{otel_scope_name="Autofac.Workflows",...,workflow_name="Production Deployment Approval"} 1
workflow_runs_completed_total{otel_scope_name="Autofac.Workflows",...} 1
workflow_approvals_created_total{otel_scope_name="Autofac.Workflows",...,risk_level="high"} 1
workflow_approvals_decided_total{otel_scope_name="Autofac.Workflows",...,decision="approve",risk_level="high"} 1
```

---

## Teardown

```bash
docker compose -f docker/docker-compose.manual.yml down -v
```

The `-v` flag removes the Postgres volume so the next test run starts clean.

---

## Quick reference — all API endpoints used

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/health/live` | Liveness check |
| `POST` | `/api/workflows/import` | Import BPMN draft |
| `POST` | `/api/workflows/validate` | Validate BPMN (no save) |
| `GET` | `/api/workflows` | List all workflow definitions |
| `POST` | `/api/workflows/{id}/publish` | Publish a workflow version |
| `POST` | `/api/runs` | Start a new run |
| `GET` | `/api/runs` | List all runs |
| `GET` | `/api/runs/{runId}` | Get run detail + steps + events |
| `GET` | `/api/approvals` | List all approval requests |
| `GET` | `/api/approvals/{id}` | Get one approval |
| `POST` | `/api/approvals/{id}/decision` | Approve / reject / escalate |
| `GET` | `/metrics` | Prometheus metrics |
