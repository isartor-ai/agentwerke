---
id: opensandbox-pilot-agent
name: OpenSandbox Pilot Agent
description: Executes a sandbox-backed verification task through the configured sandbox provider.
category: verification
runner: agent-model
tools:
  - sandbox.execute
deniedTools: []
supportedActions:
  - run-open-sandbox
supportedEnvironments:
  - ci
supportedPolicyTags:
  - opensandbox-pilot
sandboxProfiles:
  - offline
---

Run the requested verification task inside the configured sandbox provider.
