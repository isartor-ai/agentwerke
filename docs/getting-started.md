# Getting started (5-minute tokenless quickstart)

Run a real governed Autofac workflow end to end — **no API keys, no accounts**.
Model calls use a deterministic mock provider and GitHub is disabled, so you can
see the engine, agents, approval gates, and evidence working before wiring up
real credentials.

## Prerequisites

- Docker + Docker Compose. That's it.

## 1. Start the stack

```bash
docker compose -f docker/docker-compose.quickstart.yml up --build
```

Wait for the API to report healthy, then:

| URL | What it is |
| --- | --- |
| http://localhost:3002 | Web UI |
| http://localhost:8081/api/health/live | API liveness (`{"status":"live"}`) |

```bash
curl -sf http://localhost:8081/api/health/live
```

## 2. Import the sample workflow

A tiny SDLC slice — **Analyze (agent) → Review (approval) → Done** — is in
[`docs/quickstart/hello-sdlc.bpmn`](quickstart/hello-sdlc.bpmn).

The quickstart API runs with a dev token, so set it once for the calls below:

```bash
TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJkZXY6YWRtaW4iLCJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lkZW50aXR5L2NsYWltcy9uYW1lIjoiZGV2OmFkbWluIiwiaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNvbS93cy8yMDA4LzA2L2lkZW50aXR5L2NsYWltcy9yb2xlIjoiQWRtaW4iLCJpc3MiOiJhdXRvZmFjLWRldiIsImF1ZCI6ImF1dG9mYWMtZGV2IiwiZXhwIjoxODkzNDU2MDAwfQ.1koOCkdx_pfBXg8WIobkTotJevt-3H2ofM66IecvVmQ"
API=http://localhost:8081
XML=$(cat docs/quickstart/hello-sdlc.bpmn)

WID=$(curl -sf "$API/api/workflows/import" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"fileName\":\"hello-sdlc.bpmn\",\"bpmnXml\":$(printf '%s' "$XML" | jq -Rs .)}" | jq -r .workflowId)

curl -sf "$API/api/workflows/$WID/publish" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"bpmnXml\":$(printf '%s' "$XML" | jq -Rs .),\"description\":\"quickstart\"}" >/dev/null
echo "workflow: $WID"
```

(Or open the **Workflows** page in the UI and import the file there.)

## 3. Start a run

```bash
RID=$(curl -sf "$API/api/runs" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d "{\"workflowId\":\"$WID\"}" | jq -r .runId)
echo "run: $RID"
```

The **Analyze** agent step runs immediately on the mock provider (zero tokens),
then the run pauses at the **Review** approval gate.

## 4. Watch it and approve

Open **http://localhost:3002 → Runs → your run**. The BPMN diagram shows
*Analyze* completed and *Review* awaiting approval. Click **Approve** — or:

```bash
AID=$(curl -sf "$API/api/approvals" -H "Authorization: Bearer $TOKEN" \
  | jq -r "[.[] | select(.runId==\"$RID\" and .status==\"pending\")][0].id")
curl -sf "$API/api/approvals/$AID/decision" -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" -d '{"decision":"approve","comment":"LGTM"}' >/dev/null

curl -sf "$API/api/runs/$RID" -H "Authorization: Bearer $TOKEN" | jq .status   # -> "completed"
```

## 5. Inspect the evidence

Every run produces a tamper-evident evidence pack — prompts, agent output,
policy decisions, token usage, and the audit log:

```bash
curl -sf "$API/api/runs/$RID/evidence-pack" -H "Authorization: Bearer $TOKEN" | jq '{runId, modelUsage, approvals}'
```

## 6. Tear down

```bash
docker compose -f docker/docker-compose.quickstart.yml down -v
```

## Next steps

- **Use a real model:** set `Anthropic__Provider=anthropic` and `Anthropic__ApiKey=sk-ant-...`
  on the `api` service (or drop `Provider` and just set the key).
- **Wire up GitHub:** set `Integrations__GitHub__Enabled=true` plus
  `RepositoryOwner` / `RepositoryName` / `PersonalAccessToken` to let agents open
  real branches and pull requests.
- **Author your own workflows** in the designer, or see the BPMN extension and
  agent-authoring references under [`docs/`](.).
- **Deploy for real:** do not keep the dev JWT/dev-token settings in production —
  see the deployment guidance in `docs/`.
