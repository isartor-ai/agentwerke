# Evaluate OpenSandbox as Agentwerke's sandbox control plane with Kata underneath

## Summary
Evaluate and adopt OpenSandbox as the preferred sandbox control plane candidate for Agentwerke, with Kata Containers or Kata plus Firecracker configured as the production isolation runtime underneath it.

## Why
Initial sandbox planning treated Kata as something Agentwerke would integrate directly through Kubernetes RuntimeClass. After reviewing OpenSandbox, the better first move is to use OpenSandbox as the higher-level sandbox lifecycle and execution layer, then configure its secure runtime support for Kata, gVisor, or Firecracker depending on environment.

OpenSandbox gives Agentwerke a REST or OpenAPI lifecycle API, optional C# SDK path, command and file execution APIs, Kubernetes and Docker runtime backends, TTL cleanup, resource limits, network policy, egress controls, credential vault support, snapshots, and secure runtime integration. Kata remains the recommended production isolation primitive, but OpenSandbox may save Agentwerke from owning all lifecycle, exec, egress, and credential plumbing directly.

## Scope
- Write an ADR comparing direct Kubernetes plus Kata against OpenSandbox-backed execution.
- Decide whether OpenSandbox becomes the default sandbox provider abstraction target.
- Define target topology: Agentwerke worker -> `ISandboxExecutor` -> OpenSandbox provider -> OpenSandbox server -> Kubernetes runtime -> Kata RuntimeClass.
- Document Docker or runc as local development only, gVisor as a lower-overhead production option, and Kata or Kata plus Firecracker as the high-isolation production options.
- Identify security, operational, dependency, and maturity risks before committing to implementation.

## Acceptance Criteria
- ADR merged with updated candidate evaluation: Docker direct, Podman, gVisor direct, direct Kata or Kubernetes, Firecracker direct, and OpenSandbox.
- Recommendation explicitly separates control plane choice from isolation runtime choice.
- Recommendation says whether OpenSandbox should be spiked before direct Kata implementation.
- Rollout plan describes an exit path if OpenSandbox is rejected after the spike.

## Verification
- Architecture review with engineering and platform owners.
- Security review of OpenSandbox deployment model, API-key auth, credential vault, ingress or egress components, and secure runtime configuration.
- Dependency review for NuGet SDK vs REST or OpenAPI wrapper.

## Suggested Files
- `docs/decisions/`
- `docs/architecture-design.md`
- `docs/functional-specification.md`
- `src/Agentwerke.Sandboxes`
