# Open-core model

Autofac is **open core**. The core platform is free and open source under
[Apache-2.0](../LICENSE); a separately-licensed commercial tier adds the
enterprise governance, scale, and operations features that regulated
organizations need.

The goal is a clean, predictable line: everything required to **run a governed
AI software-delivery workflow end-to-end on your own infrastructure** is open
source. The commercial tier is about operating that at enterprise scale.

## Open source (Apache-2.0)

The full single-tenant, self-hostable platform:

- BPMN-native workflow engine (Postgres-backed, durable checkpoints) and the
  in-process default runtime.
- Agent orchestration: real model-backed agents, the policy-enforced Tool
  Gateway, Hook Gateway, Skill repository, and prompt assembler.
- Docker-sandboxed agent execution.
- GitHub connector (issues, branches, pull requests, reviews) and CI/CD trigger.
- Approval gates, evidence-pack export, artifact storage, audit log.
- Web UI: workflow designer, run board, run detail, approvals.
- Tokenless mock model provider for demos and CI.

## Commercial tier

Enterprise capabilities layered on top of the open core:

- Enterprise SSO / OIDC and fine-grained RBAC.
- Multi-tenant control plane and managed/hosted offering.
- Advanced policy & compliance packs and long-horizon evidence/audit retention.
- Horizontal worker scaling and queue-based dispatch at scale.
- Priority support and SLAs.

## Principles

- **No open-source feature moves behind the commercial tier.** The line only
  ever moves outward (more becomes open), never inward.
- **Self-hosting is first-class.** The open core runs entirely on your
  infrastructure with your own model keys and your own data boundary.
- **Contributions** to the open core are welcome under Apache-2.0 (see
  [CONTRIBUTING.md](../CONTRIBUTING.md)).

> This document describes intent and may evolve before the 1.0 release. The
> authoritative license for the code in this repository is [Apache-2.0](../LICENSE).
