# What Autofac Is

Autofac is the control plane around autonomous software-delivery agents. It is not a replacement for source control, CI/CD, issue tracking, or human review. It coordinates those systems through governed workflow runs.

The product thesis is simple: agents can plan, code, test, and open pull requests, but powerful automation needs process boundaries, policy, sandboxing, and evidence.

## The core loop

1. A trigger starts a workflow run. The trigger can be an API call, a GitHub webhook, a scheduled event, or another integration.
2. The workflow engine advances through a BPMN model.
3. Agent tasks assemble a prompt from the workflow task, agent profile, skills, and run context.
4. Model calls and tool calls are limited by budget, permissions, and policy.
5. Human approval tasks pause the run until an approver decides.
6. Wait states resume from timers or external events.
7. The run leaves an evidence pack that can be audited.

## Who uses it

| Role | Typical work |
| --- | --- |
| Operator | Starts runs, watches progress, retries or cancels failed runs, handles operational exceptions. |
| Approver | Reviews approval requests, risk rationale, generated output, and evidence before allowing the workflow to continue. |
| Workflow designer | Models delivery processes in BPMN and configures agent tasks, approvals, policies, and triggers. |
| Administrator | Configures identity, model providers, GitHub/Jira/Slack/Teams integrations, storage, runtime, and sandbox settings. |
| Developer | Extends agents, tools, workflow runtime behavior, integrations, and the web UI. |

## What is open source

The open-source core is intended to run a governed, self-hosted workflow end to end: the BPMN runtime, agent orchestration, Docker sandboxing, GitHub connector, approvals, evidence export, artifact storage, and the web UI. Enterprise features such as SSO/OIDC, fine-grained RBAC, multi-tenant operations, advanced compliance packs, and scale features are commercial-tier capabilities. See [Open Core Boundary](/reference/open-core).

## What to read next

- New to Autofac: [Quickstart](/start/quickstart)
- Operating the product: [Runs](/manual/runs)
- Designing workflows: [Workflow Authoring](/manual/workflow-authoring)
- Administering the platform: [Deployment](/admin/deployment)
