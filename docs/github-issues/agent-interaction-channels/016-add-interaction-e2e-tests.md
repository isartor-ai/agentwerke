# Add end-to-end tests for interaction channels, races, and recovery

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 008, 009, 010, 011, 012, 014, 015.
**Blocks:** nothing — this is the evidence gate.
**Layer:** E2E.

## Context

`tests/Agentwerke.E2ETests/` runs against `docker/docker-compose.e2e.yml` (Postgres + WireMock + API)
via `E2ETestBase`/`ApiClient`. WireMock already stubs the Anthropic Messages API — see
`tests/Agentwerke.E2ETests/Fixtures/wiremock-anthropic-stub.json` and `docs/manual-test-opensandbox.md`,
which is the pattern for driving a model into emitting a specific tool call without an API key.

That same WireMock instance is the Slack/Teams/webhook fake. **No real Slack or Teams credentials in
CI** — point each connector's URL at WireMock and assert on its request journal.

## Objective

Prove the epic end to end, especially the properties unit tests cannot: races, restart, and real
delivery.

## Implementation steps

1. **Add fixture workflows** under `tests/Agentwerke.E2ETests/Fixtures/`, with Anthropic stubs driving
   each: `human.ask` (blocking), `human.confirm`, `human.notify` (non-blocking), `agent.request`
   blocking, `agent.request` non-blocking.

2. **UI scenario** (`InteractionUiE2ETests`):
   run → assert `waiting_user` → assert `GET /runs/{id}/interactions` has exactly one `pending` →
   `POST .../answer` → assert the step re-runs **exactly once** (check step attempt count, not just
   completion — "resumes exactly once" is the requirement, and a double-resume still completes) →
   assert run completes → assert audit has create, answer with responder + channel, resume.

3. **Webhook scenario:** route to WireMock → capture the outbound payload from the request journal →
   assert shape and that no redacted value appears → post a correctly signed response → assert one
   resume → replay → assert `409` and still one resume → post an invalid signature → assert `401` and
   no state change → stub a `500` → assert `failed` delivery with attempts → retry → assert success.

4. **Slack scenario:** same shape, with a `block_actions` payload. **Plus a regression assertion: an
   approval payload still routes to the approval path** — 011's whole risk is breaking a working
   feature, and this is where it would show up.

5. **Teams scenario:** assert the outbound card reaches WireMock and the delivery row is
   `not_supported` for a blocking interaction, `delivered` for a notify. This is AC 4 at v1 scope; do
   not write an inbound Teams test.

6. **Agent-to-agent:** blocking → A waits, B replies, A resumes with B's response, correlation present
   → non-blocking → A does not suspend → target failure → cancellation → timeout → depth cap → cycle.

7. **Race scenario — the one that matters most.** Fire UI + Slack + webhook responses concurrently at
   one interaction. Assert **exactly one** terminal transition, **exactly one** outbox `Resume`, two
   `409`s, and a `respondedChannel` naming a real winner. Run it repeatedly (say 20 iterations) — a
   race test that passes once proves nothing. If it is flaky, the concurrency token (003/004) is wrong;
   do not paper over it with a retry.

8. **Restart scenario:** park a run on a blocking ask →
   `docker compose -f docker/docker-compose.e2e.yml restart api` → answer after restart → assert the run
   resumes. Wait on the health endpoint rather than sleeping.

9. **Timeout scenario:** set a short `timeout_seconds`, let the sweeper fire, assert `expired` and each
   `ExpiresAction`. Then post a **late response after expiry** and assert no resume (AC 15's sibling).

10. **Cancelled-run scenario:** cancel a run parked on an interaction, then answer → assert rejected,
    no resume.

11. **Compose wiring:** add the WireMock stubs for Slack/Teams/webhook endpoints and the interaction
    config (`Integrations:Interactions:Enabled=true`, secrets) to `docker-compose.e2e.yml`, following
    how the existing Anthropic stub and `--profile` options are wired.

## Files

- `tests/Agentwerke.E2ETests/InteractionUiE2ETests.cs`, `InteractionChannelE2ETests.cs`,
  `InteractionConcurrencyE2ETests.cs`, `AgentToAgentE2ETests.cs` (new)
- `tests/Agentwerke.E2ETests/Fixtures/` (workflows + WireMock stubs)
- `docker/docker-compose.e2e.yml`

## Acceptance criteria

All 16 of 001's acceptance criteria have an executing test, except AC 4's inbound half (deferred, see
012). Specifically proven here rather than in unit tests: AC 3, 5, 10, 13, 14, 15, 16.

- The race test passes 20/20 runs.
- No test requires a real Slack or Teams credential.
- The Slack approval regression assertion passes.

## Verification

```bash
docker compose -f docker/docker-compose.e2e.yml up -d
dotnet build Agentwerke.sln
dotnet test tests/Agentwerke.E2ETests --no-build
docker compose -f docker/docker-compose.e2e.yml down -v
```

## Out of scope

A credentialed smoke test against a real Slack workspace — documented in 017, deliberately not in CI.
