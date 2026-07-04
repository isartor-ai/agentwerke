# Core Concepts

Agentwerke makes agentic delivery inspectable by turning each run into a process, not a transcript. These are the concepts you will see throughout the product and API.

## Workflow

A workflow is a BPMN process definition. It describes the order of work, agent tasks, approval gates, wait states, timers, and external-event correlations. A published workflow can be started manually or by an integration trigger.

## Run

A run is one execution of a workflow. It has a status, current step, risk level, initiator, timestamps, step history, context values, approvals, artifacts, and evidence.

Common run statuses include:

| Status | Meaning |
| --- | --- |
| `running` | The engine is actively advancing or dispatching work. |
| `awaiting_approval` | A human approval task is pending. |
| `waiting_user` | An agent asked a human a blocking question. |
| `completed` | The workflow reached its end event. |
| `failed` | A step failed and the run could not continue automatically. |

## Agent task

An agent task is a BPMN task with an `autofac:agentTask` extension. It names the agent, action, purpose, policy tag, permission level, sandbox profile, and prompt. Some actions call deterministic tools without spending model tokens.

## Agent

An agent is a Markdown profile with YAML frontmatter and a prompt body. The profile declares tools, denied tools, supported actions, environments, policy tags, and allowed sandbox profiles.

## Skill

A skill is reusable Markdown guidance. Agents can load skills based on action bindings or workflow runtime contracts. Skills are guidance unless a runtime contract marks one as required.

## Tool Gateway

The Tool Gateway brokers tool and connector calls. It checks permissions and policy before the action runs, records the decision, and ensures actions stay within the task's allowed surface.

## Policy decision

Policy decisions can allow, escalate, or reject an action. Decisions carry risk information and rationale so operators can understand why a run continued, paused, or stopped.

## Approval task

An approval task is a BPMN user task with an `autofac:approvalTask` extension. It creates an approval request, pauses the run, and resumes only after an approver posts a decision.

## Sandbox profile

Sandbox profiles describe the execution boundary for code-producing or tool-heavy work. Agentwerke supports Docker, OpenSandbox, and Kubernetes-backed providers. Profiles such as `offline`, `repo-read`, `repo-write`, and `deployment` shape network and repository access.

## Evidence pack

The evidence pack is a structured record of a run. It includes redacted prompt snapshots, model usage, tool invocations, policy decisions, sandbox executions, approvals, artifacts, audit entries, and the workflow BPMN hash.
