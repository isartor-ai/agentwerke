# Sandbox Control Plane GitHub Issue Drafts

This directory captures the replacement sandbox execution plan accepted on 2026-06-18:

- Agentwerke keeps `ISandboxExecutor` as its application boundary.
- OpenSandbox is the preferred sandbox control plane candidate.
- Kata-class secure runtimes are the default production isolation target.
- Docker remains an acceptable local fallback and narrow integration-test path.
- Direct Kubernetes plus Kata remains the fallback architecture if the OpenSandbox spike fails.

Decision:

- `docs/decisions/ADR-003-use-opensandbox-control-plane-with-kata-runtime.md`

Target repository:

```text
isartor-ai/agentwerke-private
```

Created GitHub issues:

1. [#124 Evaluate OpenSandbox as Agentwerke's sandbox control plane with Kata underneath](https://github.com/isartor-ai/agentwerke-private/issues/124)
2. [#125 Refactor Agentwerke.Sandboxes for provider-neutral OpenSandbox integration](https://github.com/isartor-ai/agentwerke-private/issues/125)
3. [#126 Implement an OpenSandbox-backed sandbox executor for Agentwerke](https://github.com/isartor-ai/agentwerke-private/issues/126)
4. [#127 Map Agentwerke sandbox profiles to OpenSandbox policy, resources, and credentials](https://github.com/isartor-ai/agentwerke-private/issues/127)
5. [#128 Add local fallback, deployment, and E2E verification for OpenSandbox rollout](https://github.com/isartor-ai/agentwerke-private/issues/128)

Recommended implementation order:

1. ADR and architecture alignment
2. Provider-neutral sandbox contract
3. OpenSandbox-backed executor spike
4. Sandbox profile and policy mapping
5. Rollout, local fallback, and E2E coverage
