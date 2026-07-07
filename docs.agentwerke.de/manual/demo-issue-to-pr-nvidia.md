# NVIDIA NIM Issue To PR Demo

This runbook starts a local Agentwerke demo that uses NVIDIA NIM models through LiteLLM, receives GitHub webhooks from `isartor-ai/agentwerke-demo`, runs sandboxed coding agents, opens a pull request, waits for merge, and closes the issue.

The LiteLLM config maps local aliases to NVIDIA NIM provider strings such as `nvidia_nim/meta/llama-3.1-8b-instruct`. It keeps `glm-5.2` available for direct testing, but the Agentwerke demo defaults to `llama-3.1-8b-instruct` because NVIDIA's hosted GLM 5.2 endpoint can take minutes to produce a first token.

## Prerequisites

- Docker Desktop with at least 4 GB available memory.
- An NVIDIA API key for `https://integrate.api.nvidia.com/v1`.
- A GitHub token for `isartor-ai/agentwerke-demo`.
  - Classic PAT: `repo`.
  - Fine-grained token: Metadata read, Contents read/write, Issues read/write, Pull requests read/write.
- A public webhook tunnel, such as `smee.io` or `cloudflared`.

This local stack mounts `/var/run/docker.sock` into the API container so Agentwerke can start sandbox containers. Use it only on a trusted local machine.

## Start The Stack

From the repository root:

```bash
cp docker/demo-nvidia/.env.example docker/demo-nvidia/.env
```

Edit `docker/demo-nvidia/.env`:

```bash
NVIDIA_API_KEY=nvapi-...
LITELLM_MASTER_KEY=sk-agentwerke-demo-local-change-me
Anthropic__ApiKey=sk-agentwerke-demo-local-change-me
Integrations__GitHub__PersonalAccessToken=github_pat_...
Integrations__GitHub__WebhookSecret=<long-random-secret>
```

`LITELLM_MASTER_KEY` and `Anthropic__ApiKey` must match. Do not commit `.env`.

Start:

```bash
docker compose -f docker/docker-compose.demo-nvidia.yml up -d --build
```

Open:

- Web UI: `http://localhost:3007`
- API health: `http://localhost:8087/api/health/live`
- LiteLLM health: `http://localhost:4001/health/liveliness`

Smoke-test LiteLLM:

```bash
set -a
source docker/demo-nvidia/.env
set +a

curl -sS --max-time 10 http://localhost:4001/v1/models \
  -H "Authorization: Bearer $LITELLM_MASTER_KEY"
```

That should list `llama-3.1-8b-instruct`, `nemotron-mini-4b-instruct`, and `glm-5.2`. Then test a generation request:

```bash
curl -sS --max-time 60 http://localhost:4001/v1/chat/completions \
  -H "Authorization: Bearer $LITELLM_MASTER_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "llama-3.1-8b-instruct",
    "messages": [{"role": "user", "content": "Reply with: Agentwerke demo ready"}],
    "max_tokens": 32
  }'
```

The seed service activates workflow `wf-demo-nvidia-issue-to-pr` and the API mounts the demo agents from `agents/`.

## Expose The Webhook

Agentwerke receives GitHub webhooks at:

```text
http://localhost:8087/webhooks/github
```

With smee:

```bash
npx smee-client \
  --url https://smee.io/<your-channel> \
  --target http://localhost:8087 \
  --path /webhooks/github
```

Use `https://smee.io/<your-channel>` as the GitHub webhook payload URL.

With cloudflared:

```bash
cloudflared tunnel --url http://localhost:8087
```

Use the printed `https://*.trycloudflare.com/webhooks/github` URL as the GitHub webhook payload URL.

In `isartor-ai/agentwerke-demo` repository settings, add a webhook:

- Content type: `application/json`
- Secret: `Integrations__GitHub__WebhookSecret` from `docker/demo-nvidia/.env`
- Events: `issues`, `issue_comment`, `pull_request`

## Run The Demo

1. Create or confirm the `agentwerke` label exists in `isartor-ai/agentwerke-demo`.
2. Open a GitHub issue describing the Todo List app request and add the `agentwerke` label.
3. Watch `http://localhost:3007/runs`. A run starts from the `issues.opened` or `issues.labeled` webhook.
4. The Analysis agent writes requirements, and Agentwerke posts them as an issue comment.
5. Approve gate 1 by adding an issue comment containing exactly:

```text
approved
```

6. The Architecture agent writes a design document, and Agentwerke posts it as an issue comment.
7. Approve gate 2 with another issue comment:

```text
approved
```

8. The Developer agent runs in a Docker sandbox, pushes branch `agentwerke/todo-<issue-number>`, and opens a pull request whose body includes `Closes #<issue-number>`.
9. The Senior Developer agent reviews the PR and posts a GitHub PR review.
10. Review and merge the PR in GitHub.
11. The `pull_request` merge webhook resumes the waiting run. Agentwerke closes the issue if GitHub has not already closed it and the run completes.

The two approval waits and the PR-merge wait appear as `waiting_external` in the run timeline. Evidence is available from the run detail page through the evidence pack actions.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| LiteLLM returns 401 | Confirm `NVIDIA_API_KEY` is valid and `LITELLM_MASTER_KEY` matches `Anthropic__ApiKey`. |
| Model not found | Confirm requests use a model alias from `docker/demo-nvidia/litellm-config.yaml`, such as `llama-3.1-8b-instruct` or `glm-5.2`. |
| GLM 5.2 smoke-test curl appears to hang | If `/v1/models` works but `glm-5.2` chat completions time out, the request reached NVIDIA and the hosted GLM 5.2 endpoint is not producing a first token quickly. Use `llama-3.1-8b-instruct` or `nemotron-mini-4b-instruct` for the manual demo. |
| Webhook does not arrive | Check the tunnel process, GitHub delivery logs, and payload URL ending in `/webhooks/github`. |
| Webhook skipped | Confirm the issue has label `agentwerke` and event action is `opened` or `labeled`. |
| Approval comment does not resume | Add a new issue comment whose only content is `approved`; edited comments do not trigger the approval gate. |
| Sandbox cannot clone or push | Confirm the GitHub token has Contents read/write and Pull requests read/write permissions. |
| Run stuck waiting for PR merge | Confirm the PR branch is `agentwerke/todo-<issue-number>` and the webhook includes `pull_request` events. |
| API cannot reach LiteLLM | Confirm `litellm` is healthy on `localhost:4001`; the API and sandboxes use `host.docker.internal:4001/v1`. |

Stop and remove demo data:

```bash
docker compose -f docker/docker-compose.demo-nvidia.yml down -v
```
