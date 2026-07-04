# Security Model

Agentwerke's security model is built around autonomy without blind trust. Agents can act, but every meaningful action crosses a governance boundary and leaves evidence.

## Layers of control

1. BPMN process boundary.
2. Policy-enforced Tool Gateway.
3. Agent permissions and allowed/denied tool lists.
4. Sandboxed execution.
5. Human approval gates.
6. Evidence and audit records.

## Process boundary

The workflow defines what work may happen and in what order. Agent behavior is attached to versioned BPMN tasks, not left as an open-ended chat session.

## Tool Gateway

Every tool or connector call is brokered. The gateway evaluates policy before execution and records the invocation. Decisions can be:

| Decision | Meaning |
| --- | --- |
| `allow` | The action can run. |
| `escalate` | The action requires human review or a modeled approval path. |
| `reject` | The action is blocked. |

## Permissions

Each agent task can carry:

- `permissionLevel`
- `allowedTools`
- `deniedTools`
- `purposeType`
- `policyTag`
- `sandboxProfile`

The gateway denies anything outside the task's granted surface.

## Sandbox network policy

| Profile | Docker behavior |
| --- | --- |
| `offline` | No network. |
| `repo-read` | Bridge networking unless a stronger provider/proxy enforces egress. |
| `repo-write` | Bridge networking unless a stronger provider/proxy enforces egress. |
| `deployment` | Bridge networking unless a stronger provider/proxy enforces egress. |

Plain Docker bridge networking does not enforce per-host allow-lists on its own. Use an egress proxy, OpenSandbox, or Kubernetes-backed isolation when host-level network restrictions are required.

## Secrets

- Model API keys and connector tokens are read from the secret store or deployment configuration.
- Secret values should be injected only where needed.
- The Settings API returns only secret status, source, and fingerprint.
- Secret rotations are write-only from the browser.
- Prompts are redacted in persisted snapshots, but prompts and run context should never contain secrets.

## Authentication

The API supports JWT auth with role-based policies:

- `Viewer`
- `Operator`
- `Approver`
- `Admin`

Development tokens and development identity are for local use only.

## Evidence pack

The evidence pack records:

- redacted prompts
- model usage and cost
- tool invocations
- policy decisions
- sandbox executions
- approvals
- artifacts
- audit log
- BPMN hash

Use it as the primary artifact for run review, investigation, and compliance records.
