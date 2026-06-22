---
id: opensandbox-pilot-agent-sandboxed
name: OpenSandbox Pilot Agent (agent_sandboxed)
description: Runs a claude-code-runner model call inside the sandbox via the agent_sandboxed execution mode.
category: verification
runner: claude-code
tools: []
deniedTools: []
supportedActions:
  - spec.generate
supportedEnvironments:
  - ci
supportedPolicyTags:
  - opensandbox-pilot-agent-sandboxed
sandboxProfiles:
  - offline
---

Generate the requested specification and report that it succeeded.
