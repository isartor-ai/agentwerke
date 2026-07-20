# Manual Test Scenario: Agent Interaction Channels

Version: Draft v0.1
Status: Active after #224, #225, and #230 land
Date: 2026-07-16

## Purpose

This scenario verifies UI, generic-webhook, Slack/WireMock, retry, and timeout interaction paths
without real provider credentials. The named workflows and WireMock scenarios are supplied by #230.
The preflight fails deliberately on a revision without those fixtures; do not report a false green.

## Prerequisites

- Docker with Compose v2, `curl`, `jq`, `openssl`, and `xxd`.
- Run commands from the repository root.
- The #230 interaction fixture profile publishes its API as `http://localhost:8081` and WireMock as
  `http://localhost:9093`.

## Step 0 — Preflight

```bash
test -f tests/Agentwerke.E2ETests/Fixtures/interaction-human-ask.bpmn
test -f tests/Agentwerke.E2ETests/Fixtures/wiremock-interactions.json
rg -q 'InteractionUiE2ETests' tests/Agentwerke.E2ETests
```

Expected result: all commands exit 0. A failure means #230 is absent; stop here because the remaining
end-to-end assertions cannot be executed honestly.

## Step 1 — Local setup

```bash
docker compose -f docker/docker-compose.e2e.yml up --build -d postgres migrate wiremock api
docker compose -f docker/docker-compose.e2e.yml ps
docker compose -f docker/docker-compose.e2e.yml exec api \
  curl -sf http://localhost:8080/api/health/live

export API=http://localhost:8081
TOKEN=$(curl -sf -X POST "$API/api/auth/token" \
  -H 'Content-Type: application/json' \
  -d '{"role":"Admin","subject":"manual:interaction-operator","expiryHours":2}' \
  | jq -er .token)
AUTH="Authorization: Bearer $TOKEN"
test -n "$TOKEN"
```

Expected result: Postgres, WireMock, and API are healthy and a non-empty development Admin token is
issued. Dev-token issuance must never be enabled in production.

## Step 2 — Blocking question answered in UI/API

```bash
RUN_ID=$(curl -sf -X POST "$API/api/runs" -H "$AUTH" \
  -H 'Content-Type: application/json' \
  -d '{"workflowId":"interaction-human-ask"}' | jq -er .runId)

until [ "$(curl -sf "$API/api/runs/$RUN_ID" -H "$AUTH" | jq -r .status)" = waiting_user ]; do
  sleep 1
done

INTERACTION_ID=$(curl -sf "$API/api/runs/$RUN_ID/interactions" -H "$AUTH" \
  | jq -er '[.[] | select(.status=="pending")][0].id')

curl -sf "$API/api/runs/$RUN_ID/interactions" -H "$AUTH" \
  | jq -e --arg id "$INTERACTION_ID" \
    '.[] | select(.id==$id) | .status=="pending" and .blocking==true'
```

Expected result: the run reaches `waiting_user`, exactly one pending interaction is found, and the
last command prints `true`. Open `http://localhost:3002/approvals` to answer it inline, or exercise
the same API path:

```bash
curl -sf -X POST "$API/api/runs/$RUN_ID/interactions/$INTERACTION_ID/answer" \
  -H "$AUTH" -H 'Content-Type: application/json' \
  -d '{"answer":"Proceed with the documented option"}' \
  | jq -e '.acceptedChannel=="ui"'

until [ "$(curl -sf "$API/api/runs/$RUN_ID" -H "$AUTH" | jq -r .status)" = completed ]; do
  sleep 1
done
```

Expected result: `202 Accepted`, `respondedChannel` is `ui`, and the run completes after exactly one
resume (the #230 test asserts the attempt count, not only the final status).

## Step 3 — Signed generic-webhook response

```bash
RUN_ID=$(curl -sf -X POST "$API/api/runs" -H "$AUTH" \
  -H 'Content-Type: application/json' \
  -d '{"workflowId":"interaction-webhook-ask"}' | jq -er .runId)

until [ "$(curl -sf "$API/api/runs/$RUN_ID" -H "$AUTH" | jq -r .status)" = waiting_user ]; do
  sleep 1
done

INTERACTION_ID=$(curl -sf "$API/api/runs/$RUN_ID/interactions" -H "$AUTH" \
  | jq -er '[.[] | select(.status=="pending")][0].id')

curl -sf http://localhost:9093/__admin/requests \
  | jq -e --arg id "$INTERACTION_ID" \
    '.requests[] | select(.request.bodyAsJson.interactionId==$id) |
     .request.bodyAsJson | has("prompt") and (has("artifacts")|not) and (has("context")|not)'
```

Expected result: WireMock received the bounded payload and the final assertion prints `true`.

```bash
SECRET='interaction-e2e-secret'
TIMESTAMP=$(date +%s)
NONCE=$(openssl rand -hex 16)
BODY=$(jq -cn --arg id "$INTERACTION_ID" --arg nonce "$NONCE" \
  '{interactionId:$id,response:"approve",decision:"approve",
    responder:{id:"wiremock-operator",displayName:"WireMock Operator"},nonce:$nonce}')
SIGNATURE="sha256=$(printf '%s.%s' "$TIMESTAMP" "$BODY" \
  | openssl dgst -sha256 -hmac "$SECRET" -binary | xxd -p -c 256)"

test "$(curl -s -o /tmp/interaction-response.json -w '%{http_code}' \
  -X POST "$API/webhooks/interactions/response" \
  -H 'Content-Type: application/json' \
  -H "X-Agentwerke-Timestamp: $TIMESTAMP" \
  -H "X-Agentwerke-Signature: $SIGNATURE" \
  --data-binary "$BODY")" = 202
jq -e '.acceptedChannel=="webhook"' /tmp/interaction-response.json
```

Expected result: both assertions pass; the interaction records `webhook` as the winning channel and
`webhook:wiremock-operator` as responder.

Replay the same bytes and nonce:

```bash
test "$(curl -s -o /tmp/interaction-replay.json -w '%{http_code}' \
  -X POST "$API/webhooks/interactions/response" \
  -H 'Content-Type: application/json' \
  -H "X-Agentwerke-Timestamp: $TIMESTAMP" \
  -H "X-Agentwerke-Signature: $SIGNATURE" \
  --data-binary "$BODY")" = 409
```

Expected result: HTTP 409 and no second resume. This is a required #224/#230 gate and fails on a
revision that accepts a nonce without persistent replay protection.

## Step 4 — Slack against WireMock

```bash
dotnet test tests/Agentwerke.E2ETests/Agentwerke.E2ETests.csproj \
  --filter 'FullyQualifiedName~InteractionChannelE2ETests.SlackAnswer'
```

Expected result: the test passes; WireMock receives a namespaced Block Kit interaction action,
`respondedChannel` is `slack`, the run resumes once, and the legacy Slack approval regression remains
green. No real Slack credential is used.

## Step 5 — Delivery failure and retry

```bash
curl -sf -X POST http://localhost:9093/__admin/scenarios/interaction-delivery/state \
  -H 'Content-Type: application/json' -d '{"state":"Failing"}' >/dev/null

RUN_ID=$(curl -sf -X POST "$API/api/runs" -H "$AUTH" \
  -H 'Content-Type: application/json' \
  -d '{"workflowId":"interaction-webhook-ask"}' | jq -er .runId)

until INTERACTION_ID=$(curl -sf "$API/api/runs/$RUN_ID/interactions" -H "$AUTH" \
  | jq -er '[.[] | select(.status=="pending")][0].id'); do
  sleep 1
done

curl -sf "$API/api/runs/$RUN_ID/interactions" -H "$AUTH" \
  | jq -e --arg id "$INTERACTION_ID" \
    '.[] | select(.id==$id) | .deliveries[] | select(.channel=="webhook") |
     .status=="failed" and .attempts>=1 and (.lastError|length)>0'
```

Expected result: `true`; the interaction remains pending and answerable in the UI.

```bash
curl -sf -X POST http://localhost:9093/__admin/scenarios/interaction-delivery/state \
  -H 'Content-Type: application/json' -d '{"state":"Started"}' >/dev/null

curl -sf -X POST \
  "$API/api/interactions/$INTERACTION_ID/deliveries/webhook/retry" -H "$AUTH" \
  | jq -e '.status=="delivered"'
```

Expected result: `true`; retry updates the existing channel row rather than adding a duplicate.

## Step 6 — Timeout

```bash
RUN_ID=$(curl -sf -X POST "$API/api/runs" -H "$AUTH" \
  -H 'Content-Type: application/json' \
  -d '{"workflowId":"interaction-timeout-fail"}' | jq -er .runId)

until INTERACTION=$(curl -sf "$API/api/runs/$RUN_ID/interactions" -H "$AUTH" \
  | jq -cer '[.[] | select(.status=="pending")][0]'); do
  sleep 1
done

echo "$INTERACTION" | jq -e '.timeoutAt != null and .expiresAction=="fail"'

until [ "$(curl -sf "$API/api/runs/$RUN_ID/interactions" -H "$AUTH" \
  | jq -r '.[0].status')" = expired ]; do
  sleep 1
done
```

Expected result: the interaction becomes `expired`, its step fails, and a correctly signed late
response returns 409 without another resume.

## Step 7 — Clean up

```bash
docker compose -f docker/docker-compose.e2e.yml down -v
```

Expected result: test containers, networks, and volumes are removed.

## Credentialed Slack smoke test

Do not put real Slack credentials in CI. Follow the app setup and click-through checklist in
[interaction-channels.md](interaction-channels.md#credentialed-slack-smoke-test-manual-never-ci) from a
private test workspace and public TLS callback URL. Record workspace, app version, run id, and date in
release evidence without recording webhook URLs, signing secrets, or bot tokens.
