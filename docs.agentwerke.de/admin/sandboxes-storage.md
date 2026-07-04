# Sandboxes And Storage

Sandbox and storage settings define where agent work happens and where run artifacts live. Treat both as part of the security boundary.

## Sandbox purpose

Agent tasks can generate code, run commands, inspect repositories, or call tools. Sandboxes isolate that work from the control plane and let policy choose the right execution boundary.

## Providers

| Provider | Use when |
| --- | --- |
| Docker | Local development, quickstart, and simple self-hosted installs. |
| OpenSandbox | Stronger isolated execution and egress control. |
| Kubernetes | Cluster-native execution, including Kata-style isolation when configured. |

## Profiles

| Profile | Typical use |
| --- | --- |
| `offline` | Read prompt/context only; no network. |
| `repo-read` | Inspect source without writing changes. |
| `repo-write` | Create commits, branches, or generated artifacts. |
| `deployment` | Interact with deployment targets under strict policy. |

With the Docker provider, `offline` maps to no network. Other profiles map to Docker bridge networking unless an egress proxy or stronger provider enforces per-host rules. Use OpenSandbox or Kubernetes when host-level network policy is required.

## Sandbox checklist

- Enable sandboxing before running untrusted or code-writing tasks.
- Match workflow `sandboxProfile` to the agent profile's allowed profiles.
- Keep repository write access separate from deployment access.
- Pass only the secrets the task needs.
- Prefer read-only mounts unless the task must write.
- Record sandbox execution evidence for later audit.

## Artifact storage

Agentwerke can store artifact bytes on the local filesystem or S3-compatible storage.

### Filesystem

```bash
Storage__Provider=filesystem
Storage__RootPath=/var/lib/agentwerke/artifacts
```

Use persistent volumes for production. Back up the artifact path if evidence retention requires it.

### S3

```bash
Storage__Provider=s3
Storage__S3__Bucket=<bucket>
Storage__S3__Region=<region>
Storage__S3__AccessKeyId=<secret>
Storage__S3__SecretAccessKey=<secret>
```

Use bucket policies, encryption, lifecycle rules, and region selection that match the deployment's compliance requirements.

## Evidence retention

Evidence packs reference prompts, actions, decisions, approvals, artifacts, and audit entries. Decide retention policy before production:

- How long to keep run metadata in Postgres.
- How long to keep artifact bytes.
- Whether evidence packs are exported to an external records system.
- Who can read evidence packs.
- How incident responders retrieve evidence after a failed or disputed run.

## Storage safety checklist

- Do not store artifacts on ephemeral container filesystems in production.
- Encrypt storage at rest.
- Restrict bucket/path access to the Agentwerke service identity.
- Keep artifact retention aligned with audit requirements.
- Verify evidence downloads after each deployment change.
