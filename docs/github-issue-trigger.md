# GitHub issue trigger

Autofac can start a workflow run directly from a GitHub issue: someone opens
(or labels) an issue describing a feature or bug, and — if the issue opts in —
an agent reads it, plans a change, and opens a branch/PR against the
configured repository.

## How it works

1. GitHub sends an `issues` webhook to `POST /webhooks/github` on your Autofac
   instance (signed with `X-Hub-Signature-256`).
2. `WebhooksController` validates the signature against
   `Integrations:GitHub:WebhookSecret`, then checks the issue `action`
   (`opened`, `labeled`, ...) against `Integrations:GitHub:TriggerActions`.
3. **The issue must also carry the required label** (see below). Issues
   without it are acknowledged (`200 OK`, `{ "skipped": true }`) but do not
   start a run.
4. Autofac resolves the active workflow tagged `github-trigger` and starts a
   run, seeding the trigger context from the issue's title, body, and URL.
5. The workflow's agent task reads that context, and any
   `github.create_branch` / `github.create_pull_request` tool calls run
   through the policy-enforced tool gateway.

See [architecture (as-built)](architecture.md) for how the run proceeds
through the workflow engine after this point.

## Required label

By default, an issue must carry the label **`autofac`** for its webhook to
start a run. This exists because `TriggerActions` alone (default: `opened`)
would otherwise fire on **every** issue opened on the configured repository —
spending model budget and potentially opening PRs for issues that were never
meant for Autofac. A label is the native, discoverable GitHub mechanism for
opting an issue in (as opposed to, say, a text convention in the title, which
is fragile and invisible in the GitHub UI).

Configure it via `Integrations:GitHub:RequiredLabel`:

```json
"Integrations": {
  "GitHub": {
    "RequiredLabel": "autofac"
  }
}
```

or as an environment variable:

```bash
Integrations__GitHub__RequiredLabel=autofac
```

- Matching is case-insensitive (`autofac`, `AutoFac`, `AUTOFAC` all match).
- Set it to an empty string to disable the check — every issue matching
  `TriggerActions` will start a run again, restoring pre-#191 behavior.

## Setup checklist

1. Create the `autofac` label on your repository (or pick your own value for
   `RequiredLabel`).
2. Tag the workflow you want issues to start with `github-trigger`.
3. Configure `Integrations:GitHub:WebhookSecret`, `RepositoryOwner`,
   `RepositoryName`, and `PersonalAccessToken` (see
   [getting started](getting-started.md#next-steps)).
4. Point the repository's webhook deliveries (`issues` event) at
   `POST /webhooks/github` on your Autofac instance, using the same shared
   secret as `WebhookSecret`.
5. File an issue, apply the `autofac` label (or open it with the label
   already applied via an issue template), and confirm a run starts.

## Reference

- `src/Autofac.Api/Controllers/WebhooksController.cs` —
  `HandleIssuesEventAsync` / `HasRequiredLabel`
- `src/Autofac.Integrations/IntegrationOptions.cs` — `GitHubOptions`
- `tests/Autofac.Api.Tests/WebhooksControllerTests.cs` — trigger/skip/opt-out
  coverage
- isartor-ai/autofac-private#191 — the issue that introduced this gate
