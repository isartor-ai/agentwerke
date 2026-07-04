# Agents And Skills

Agents and skills are Markdown artifacts. This makes them easy to review, version, diff, and ship with the repository.

## Agent location

Bundled agents live under:

```text
agents/<agent-id>/AGENT.md
```

Agentwerke loads the configured `Agents:Registry:AgentsDirectory`. If the bundled directory is read-only, set `Agents:Registry:WritableAgentsDirectory` to a writable overlay. Agentwerke reads the shipped directory first and then the writable overlay.

## Agent profile shape

```markdown
---
id: github-agent
name: GitHub Agent
description: Creates branches and pull requests in the configured repository.
category: integration
runner: agent-model
tools:
  - github.create_branch
  - github.create_pull_request
deniedTools: []
supportedActions:
  - github.create_branch
  - github.create_pull_request
supportedEnvironments: [github]
supportedPolicyTags: [repo-change, pull-request]
sandboxProfiles: [repo-write]
---

You are the GitHub integration agent for Agentwerke.
```

## Important fields

| Field | Use |
| --- | --- |
| `id` | The workflow references this value from `agentTask agent="..."`. |
| `runner` | `agent-model` runs in-process; `claude-code` runs inside a sandboxed runner. |
| `tools` | Tools the agent may request. The Tool Gateway still enforces policy. |
| `deniedTools` | Explicitly blocked tools. |
| `supportedActions` | Actions this agent should handle. |
| `supportedEnvironments` | Compatibility labels surfaced to designers. |
| `supportedPolicyTags` | Governance tags the agent is designed for. |
| `sandboxProfiles` | Sandbox profiles the agent is allowed to use. |

## Skill location

Skills live under:

```text
.github/skills/<skill-id>/SKILL.md
```

A skill's directory name is its primary id. The frontmatter `name` defaults to the id.

## Skill shape

```markdown
---
name: git-workflow-and-versioning
description: Structures git workflow practices.
version: 1.0.0
invocationRules: []
requiredFiles: []
optionalTools: []
---

# Git Workflow and Versioning

Guidance goes here.
```

## Prompt assembly

For each agent step, Agentwerke assembles:

1. System preamble.
2. Agent profile metadata and prompt body.
3. Task prompt from the BPMN extension.
4. Resolved skill content.
5. Run context.

All <code v-pre>{{...}}</code> placeholders are rendered from run context before the model receives work.

## Authoring guidance

- Keep the agent profile about authority, scope, and operating style.
- Put reusable workflow-specific guidance in skills.
- Keep tool lists narrow and explicit.
- Use denied tools for actions the agent should never request, even if they appear in a broader tool set.
- Treat missing optional skills as degraded guidance, not a reason to fail the step.
- Use a runtime-contract-required skill only when the step cannot safely run without it.

## Review checklist

- The agent id is unique.
- Tool and sandbox permissions match the workflow risk.
- Supported actions match the workflow task actions.
- Skill references resolve by id or name.
- The prompt body does not contain secrets.
- The agent has been tested with the mock provider before a real model/provider run.
