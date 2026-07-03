# ADR-003: Use OpenSandbox Control Plane with Kata-Class Production Runtime

## Status
Accepted

## Date
2026-06-18

## Context
Agentwerke's sandbox layer is the safety boundary for agent execution. It is responsible for isolating code generation, repository changes, shell access, external calls, file access, and artifact capture away from the core workflow and API processes.

The current `Agentwerke.Sandboxes` implementation is intentionally narrow. It talks directly to the local Docker daemon through `Docker.DotNet`, launches a single ephemeral container, bind-mounts an output directory, and returns logs plus text artifacts. That has been useful for validating the interface and proving lifecycle basics, but it creates four strategic gaps:

- It is Docker-daemon-specific rather than provider-neutral.
- It does not give Agentwerke a strong production isolation boundary for untrusted or LLM-generated code.
- It leaves Agentwerke to own more sandbox lifecycle, network, credential, and artifact plumbing than necessary.
- It does not map cleanly to the Kubernetes-oriented deployment direction already documented for Agentwerke.

Agentwerke needs a sandbox approach that keeps local development straightforward while improving production isolation and preserving the existing `ISandboxExecutor` boundary inside the application code.

## Decision
Agentwerke will treat OpenSandbox as the preferred sandbox control plane candidate, with Kata Containers or Kata plus Firecracker as the default production isolation runtime underneath it.

The control plane choice and the isolation runtime choice are separate decisions:

- **Control plane**: OpenSandbox is the preferred lifecycle and execution layer because it offers sandbox creation, lifecycle management, command execution, file operations, endpoint management, resource limits, network policy, credential proxy or vault integration, and multiple runtime backends behind a consistent API.
- **Production secure runtime**: Kata Containers is the default target for production isolation because it gives Agentwerke a stronger boundary than ordinary containers while fitting OCI, CRI, and Kubernetes deployment patterns. Kata plus Firecracker may be used when a platform owner wants that tighter microVM posture and can operate it.
- **Local and test fallback**: Docker remains acceptable for local development and narrow integration testing through the same Agentwerke sandbox interface. It is not the desired long-term production control plane or isolation boundary.
- **Secondary production option**: gVisor remains a valid lower-overhead production option when a customer prioritizes operational simplicity over the stronger isolation boundary of Kata.

Agentwerke keeps ownership of the application boundary:

- `ISandboxExecutor` remains the internal Agentwerke contract.
- Agentwerke should add a provider-neutral sandbox abstraction that can route to Docker today, OpenSandbox next, and a direct Kubernetes or Kata executor only if the OpenSandbox spike fails.
- Agentwerke should prefer wrapping the OpenSandbox REST or OpenAPI API behind an Agentwerke-owned client interface. The C# SDK is useful, but it should remain an implementation detail rather than a dependency leaked into the rest of Agentwerke.

## Target Topology
The target production topology is:

`Agentwerke worker or agent orchestrator -> ISandboxExecutor -> OpenSandbox-backed provider -> OpenSandbox server -> Kubernetes runtime -> Kata RuntimeClass -> artifact storage, observability, and approved external endpoints`

The target local topology is:

`Agentwerke worker or agent orchestrator -> ISandboxExecutor -> OpenSandbox-backed provider or Docker fallback -> local Docker runtime`

## Why This Decision

### OpenSandbox as control plane

OpenSandbox is a better fit for Agentwerke than a hand-built direct Kata integration because it already models the operational concerns Agentwerke needs:

- sandbox lifecycle and status
- command execution and output streaming
- file operations and artifact access
- resource limits
- network and egress policy
- credential proxy or vault integration
- multiple secure runtime backends
- local Docker mode and Kubernetes mode

That lets Agentwerke focus on policy, workflow, approvals, evidence, and agent behavior instead of rebuilding a full sandbox management layer.

### Kata as default production runtime

Kata is still the recommended production runtime because the key missing property in the current Docker-based path is isolation strength, not just container ergonomics. Agentwerke needs a production boundary that is suitable for untrusted or LLM-generated code while remaining compatible with Kubernetes and the existing self-hosted direction.

### Docker as local fallback only

Local contributors and CI environments need a development loop that works without a secure-runtime-enabled Kubernetes cluster. Docker is still useful there. The important change is that Docker stops being the architectural destination and becomes the fallback implementation behind the same provider-neutral interface.

## Rejected Alternatives

### Continue with direct Docker integration as the primary strategy

Pros:

- already implemented
- easy local developer experience

Cons:

- daemon-specific and weakly isolated for Agentwerke's long-term agent model
- poor fit for production execution of untrusted or LLM-generated code
- leaves Agentwerke to hand-build more lifecycle, network, and credential behavior than necessary

Rejected as the long-term default.

### Build a direct Kubernetes plus Kata executor first

Pros:

- strongest alignment with the target production runtime
- no dependency on an intermediate sandbox platform

Cons:

- Agentwerke would own lifecycle orchestration, command execution, file movement, network policy mapping, and cleanup directly
- higher engineering cost before proving the OpenSandbox integration path

Kept as the fallback architecture if the OpenSandbox spike fails or reveals unacceptable product risk.

### Use gVisor as the default production runtime

Pros:

- lighter operational footprint than Kata in some environments
- still materially better isolated than ordinary containers

Cons:

- weaker isolation boundary than Kata for the most sensitive execution cases
- more appropriate as a lower-cost production option than as the default posture

Rejected as the default, retained as an explicit alternative.

### Adopt Firecracker directly as the first implementation

Pros:

- very strong isolation story
- attractive long-term microVM model

Cons:

- pushes Agentwerke too far into runtime ownership too early
- higher platform complexity than needed for the first secure-runtime rollout

Rejected as the first implementation path.

### Use Podman rootless as the strategic replacement

Pros:

- operationally cleaner than Docker daemon for some environments
- attractive local developer ergonomics

Cons:

- improves engine posture more than isolation posture
- does not solve the main production concern Agentwerke has

Rejected as the long-term production answer.

## Consequences

- Architecture and product docs should stop presenting Docker as the future production sandbox architecture.
- The next sandbox implementation work should be an OpenSandbox spike and provider-neutral refactor, not a direct production-only Kata executor.
- Future sandbox work should keep `ISandboxExecutor` as the Agentwerke-owned boundary and avoid leaking OpenSandbox SDK types into the rest of the codebase.
- Local development documentation should keep Docker as an acceptable fallback path.
- Production deployment planning should assume Kubernetes plus OpenSandbox plus a secure runtime, with Kata as the default target.
- If the OpenSandbox spike fails because of security review, API gaps, operational burden, or maturity risk, Agentwerke should fall back to a direct Kubernetes plus Kata executor behind the same refactored interface.

## References

- Existing implementation: `src/Agentwerke.Sandboxes`
- OpenSandbox: https://github.com/opensandbox-group/OpenSandbox
- OpenSandbox architecture: https://github.com/opensandbox-group/OpenSandbox/blob/main/docs/architecture.md
- OpenSandbox secure runtime guide: https://github.com/opensandbox-group/OpenSandbox/blob/main/docs/secure-container.md
- OpenSandbox C# SDK: https://github.com/opensandbox-group/OpenSandbox/blob/main/sdks/sandbox/csharp/README.md
