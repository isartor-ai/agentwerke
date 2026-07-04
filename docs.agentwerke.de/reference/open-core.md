# Open Core Boundary

Agentwerke is open core. The core platform is Apache-2.0 and self-hostable. A separately licensed commercial tier adds enterprise governance, scale, and operations features.

## Open source core

The open-source distribution is intended to run a governed AI software-delivery workflow end to end on your own infrastructure.

It includes:

- BPMN-native workflow engine.
- Postgres-backed durable checkpoints.
- Agent orchestration.
- Policy-enforced Tool Gateway.
- Hook Gateway.
- Skill repository and prompt assembler.
- Docker-sandboxed agent execution.
- GitHub connector for issues, branches, pull requests, and reviews.
- CI/CD trigger support.
- Approval gates.
- Evidence-pack export.
- Artifact storage.
- Audit log.
- Web UI for workflow design, runs, run detail, and approvals.
- Tokenless mock model provider for demos and CI.

## Commercial tier

Commercial capabilities are layered on top:

- Enterprise SSO/OIDC and fine-grained RBAC.
- Multi-tenant control plane and managed/hosted offering.
- Advanced policy and compliance packs.
- Long-horizon evidence and audit retention.
- Horizontal worker scaling and queue-based dispatch at scale.
- Priority support and SLAs.

## Principles

- Open-source features should not move behind the commercial tier.
- Self-hosting remains first-class.
- Contributions to the open core are welcome under Apache-2.0.

## Documentation convention

Manual pages should label commercial-only features in context. Do not make an open-source operator discover a feature boundary only after following setup instructions.
