# Authoring agents and skills

Agents and skills are plain Markdown files with YAML frontmatter, loaded from
configured directories (`Agents:Registry:AgentsDirectory` and
`Agents:Skills:SkillsDirectory`). The defaults ship under `agents/` and
`.github/skills/`.

When the bundled agents directory is mounted read-only, set
`Agents:Registry:WritableAgentsDirectory` to a writable overlay directory.
Agentwerke loads `AgentsDirectory` first and then `WritableAgentsDirectory`, so
admin UI saves can customize or add agents without mutating the shipped files.
If `WritableAgentsDirectory` is omitted, saves use `AgentsDirectory` for
backward compatibility.

## Agents (`agents/<id>/AGENT.md`)

An agent profile declares what an agent is allowed to do; the Markdown body is
the agent's system prompt.

```markdown
---
id: github-agent
name: GitHub Agent
description: Creates branches and pull requests in the configured repository.
category: integration
runner: agent-model        # agent-model (in-process) | claude-code (sandboxed)
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
# Optional: bind skills to actions (see Skills below)
# skills:
#   - id: branching
#     supportedActions: [github.create_branch]
#     skillManifestId: git-workflow-and-versioning
---

You are the GitHub integration agent for Agentwerke. ...
```

| Field | Notes |
| --- | --- |
| `id` | Unique agent id referenced by `agentTask agent="…"`. |
| `runner` | `agent-model` runs in-process; `claude-code` runs in a sandbox (`agent_sandboxed`). |
| `tools` / `deniedTools` | Tools the agent may or may not use (enforced by the Tool Gateway). |
| `supportedActions` | Actions this agent handles. |
| `supportedEnvironments` / `supportedPolicyTags` | Declared compatibility, surfaced in the designer. |
| `sandboxProfiles` | Allowed sandbox profiles; the task's `sandboxProfile` must be in this list. |
| `skills` | Optional action→skill bindings. A skill that fails to resolve is **skipped** (it's guidance, not a hard requirement) — only a runtime-contract-required skill fails a step. |

## Skills (`.github/skills/<id>/SKILL.md`)

A skill is reusable guidance the agent loads into context. The **skill id is the
directory name**; `name` defaults to the id.

```markdown
---
name: git-workflow-and-versioning
description: Structures git workflow practices. Use when committing, branching, ...
version: 1.0.0            # optional
invocationRules: []        # optional
requiredFiles: []          # optional
optionalTools: []          # optional
---

# Git Workflow and Versioning

## Overview
...
```

Skills are referenced by id (directory name) from an agent profile's `skills`
binding or a workflow's runtime contract. Resolution is by id, then by `name`.

## How prompts are assembled

For each agent step the prompt assembler combines, in order: a system preamble,
the agent profile (description/category and system-prompt body), the **task
prompt** (the `agentTask` `prompt`/`promptFile`/`<autofac:prompt>`, or a default),
the resolved **skill** content, and the **run context**. All `{{…}}` placeholders
are rendered from run context (see [BPMN extensions](bpmn-extensions.md)).
