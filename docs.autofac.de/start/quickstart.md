# Quickstart

The fastest way to see Autofac working is the tokenless quickstart. It runs the API and web UI with a deterministic mock model provider, so you do not need API keys, GitHub credentials, or external accounts.

## Prerequisites

- Docker
- Docker Compose

## Start the stack

From the repository root:

```bash
docker compose -f docker/docker-compose.quickstart.yml up --build
```

Wait for the API and web UI containers to start, then open:

| URL | Purpose |
| --- | --- |
| `http://localhost:3002` | Autofac web UI |
| `http://localhost:8081/api/health/live` | API liveness probe |

You can check the API from a terminal:

```bash
curl -sf http://localhost:8081/api/health/live
```

Expected response:

```json
{"status":"live"}
```

## Run the sample workflow

1. Open `http://localhost:3002`.
2. Go to the Runs page.
3. Click **Run sample workflow**.

The seeded workflow is a small governed SDLC slice:

```text
Draft Implementation Note -> Review Sample Output -> Done
```

The agent step uses the mock provider. It completes without model credentials and without token cost.

## Approve the review gate

When the run reaches the review step, it pauses at a human approval task.

1. Open the run detail page.
2. Review the agent output and policy rationale.
3. Click **Approve**.

After the approval decision, the run should move to `completed`.

## Inspect the evidence

Every run can expose an evidence pack through the API:

```bash
curl -sf http://localhost:8081/api/runs/<run-id>/evidence-pack \
  -H "Authorization: Bearer <dev-token>"
```

The quickstart UI handles the easiest path. Use the API path when you are validating automation or collecting evidence in CI.

## Stop the stack

```bash
docker compose -f docker/docker-compose.quickstart.yml down -v
```

The `-v` flag removes the quickstart database volume so the next run starts from a clean seeded state.

## Next steps

- Learn how runs progress: [Runs](/manual/runs)
- Learn approvals and evidence: [Approvals And Evidence](/manual/approvals-evidence)
- Configure a real model provider: [Model Providers](/manual/model-providers)
- Start runs from GitHub issues: [GitHub Issue Trigger](/manual/github-issue-trigger)
- Harden deployment settings: [Deployment](/admin/deployment)
