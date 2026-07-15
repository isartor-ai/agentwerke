# Document interaction channels, configuration, and operations

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 013, 016.
**Blocks:** nothing — last task.
**Layer:** docs.

## Context

The repo documents behavior in `docs/functional-specification.md`, schema in
`docs/persistence-schema.md`, architecture in `docs/architecture-design.md`, deployment in
`docs/deployment.md`, and manual scenarios in `docs/manual-test-*.md` (see
`docs/manual-test-opensandbox.md` for the house style — exact commands, exact assertions).

## Objective

An operator can configure a channel, answer an agent's question, and diagnose a failed delivery from
the docs alone.

## Implementation steps

1. **`docs/functional-specification.md`** — the interaction lifecycle: the six kinds, blocking vs
   non-blocking vs confirmation, the state machine from 001, and first-valid-response-wins. Be explicit
   that **rejecting a confirmation fails the step** rather than returning "no" to the agent; that is
   surprising until you know why, and the reason (not letting a model argue past a human's no) is the
   point of the feature.

2. **`docs/persistence-schema.md`** — the new `agent_interactions` columns and `interaction_deliveries`.
   Document the `Version` concurrency token and **why it is an `int` and not `xmin`** — every unit-test
   fake is hand-rolled and cannot emulate an EF shadow property, so `xmin` would make the single-winner
   race testable only in Docker. Write the reason down, or someone will "optimize" it to `xmin` later. Document that timestamps are ISO-8601 `"o"` strings
   and that the sweeper's ordinal comparison depends on it.

3. **`docs/architecture-design.md`** — the `IInteractionChannel` boundary and the rule that no provider
   type may be referenced from `Domain`/`Agents`/`Application`. Note the architecture test in 006
   enforces it.

4. **Channel configuration** (in `deployment.md` or a new `docs/interaction-channels.md`):
   `Integrations:Interactions` options, per-provider setup, secrets via `ISecretStore`, and the
   layered channel precedence. State plainly that **Teams cannot accept replies in v1** and why — an
   operator who enables Teams and waits for a click deserves to have been told.

5. **Webhook contract** — the outbound payload, the signature scheme
   (`X-Agentwerke-Signature: sha256=<hex>` over raw bytes, `X-Agentwerke-Timestamp`), the ±5-minute
   window, nonce replay, and a worked signing example. Document that the interaction endpoint **fails
   closed** without a secret, unlike the Jira/GitHub trigger endpoints — that asymmetry is deliberate
   and will otherwise look like a bug.

6. **API reference** — the endpoints from 013 with 001's status table. Note the deliberate difference:
   Slack's callback returns `200` with a message where the API returns `409`, because Slack renders the
   body to the user.

7. **`docs/manual-test-interactions.md`** (new) — following `manual-test-opensandbox.md`'s style:
   local setup, a blocking question answered in the UI, a webhook answer, a Slack answer against
   WireMock, a delivery failure and retry, a timeout. Exact commands, exact expected output.

8. **Credentialed Slack smoke test** — documented here, explicitly **not in CI**: creating the app, the
   signing secret, the callback URL, and what to click. Say why it is manual (no real credentials in
   CI) so nobody "fixes" its absence.

9. **Operational runbook** — the metrics and audit actions from 001, what a rising race-loss counter
   means (normal under fan-out; alarming if it spikes without fan-out), how to find and retry failed
   deliveries, how to tune timeouts, and that **the default timeout is null/never** so nothing starts
   expiring on upgrade.

10. **Update `docs/github-issues/agent-interaction-channels/README.md`** with final status.

## Files

- `docs/functional-specification.md`, `docs/persistence-schema.md`, `docs/architecture-design.md`,
  `docs/deployment.md`
- `docs/interaction-channels.md`, `docs/manual-test-interactions.md` (new)
- `docs/github-issues/agent-interaction-channels/README.md`

## Acceptance criteria

- An operator who has not read this ticket can configure Slack and answer an agent question from the
  docs alone.
- Every documented command is copy-pasteable and was actually run.
- The webhook signing example verifies against the implementation.
- Teams' v1 limitation is stated wherever Teams is configurable.
- Fail-closed behavior and its asymmetry with the trigger endpoints are documented.

## Verification

```bash
# Follow docs/manual-test-interactions.md start to finish on a clean checkout.
docker compose -f docker/docker-compose.e2e.yml up -d
```

Review gate: someone other than the implementer follows the manual test and succeeds without asking a
question.

## Out of scope

Dashboards beyond the metrics/log documentation. Marketing or docs.agentwerke.de content.
