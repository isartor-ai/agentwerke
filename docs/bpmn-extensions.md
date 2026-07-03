# Agentwerke BPMN extensions reference

Agentwerke runs standard BPMN 2.0, augmented with a small set of `agentwerke:`
extension elements that make a task executable by the agent runtime. The
`agentwerke:` prefix is intentionally retained as the stable workflow XML contract
for existing definitions during the product rename. The engine executes nodes in
document order; sequence flows are honored for layout and gateways.

```xml
<bpmn:definitions
    xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
    xmlns:agentwerke="https://agentwerke.de/bpmn/extensions/v1">
  ...
</bpmn:definitions>
```

> Include a `<bpmndi:BPMNDiagram>` (shape coordinates) so the workflow renders in
> the web designer/viewer. Workflows authored in the UI get this automatically.

## `agentwerke:agentTask` (on `serviceTask` / `scriptTask`)

Makes a task run an agent action or a deterministic tool.

```xml
<bpmn:serviceTask id="Implement" name="Implement">
  <bpmn:extensionElements>
    <agentwerke:agentTask
      agent="implementation-engineer"
      action="implement"
      executionMode="agent_sandboxed"
      sandboxProfile="repo-write"
      purposeType="implementation"
      policyTag="repo-change"
      permissionLevel="read-write"
      allowedTools="sandbox.file_write,sandbox.git">
      <agentwerke:prompt>Implement the change described in {{input.body}}. Keep it minimal.</agentwerke:prompt>
    </agentwerke:agentTask>
  </bpmn:extensionElements>
</bpmn:serviceTask>
```

| Attribute | Req | Notes |
| --- | --- | --- |
| `agent` | ✅ | Agent id (see [agent authoring](agent-and-skill-authoring.md)). Unknown ids run with a generic prompt. |
| `action` | ✅ | Drives behavior. Deterministic tool actions (below) skip the model; anything else runs the model. |
| `purposeType` | ✅ | Purpose label used by policy/risk. |
| `policyTag` | ✅ | Policy bucket used by the policy engine. |
| `environment` | | e.g. `ci`, `staging`, `production`. |
| `requiresEvidence` | | CSV of required evidence items. |
| `executionMode` | | `local` (in-process model), `tool_sandboxed`, or `agent_sandboxed` (runs in a Docker/OpenSandbox container). Defaults based on the agent runner + whether sandboxing is enabled. |
| `sandboxProfile` | | `offline` (default, no network), `repo-read`, `repo-write`, `deployment`. Governs checkout + egress. |
| `permissionLevel` | | `read-only` (default), `read-write`, `full` — the agent's tool permission ceiling. |
| `allowedTools` / `deniedTools` | | CSV allow/deny lists narrowing the agent's tools. |
| `prompt` / `promptFile` / `<agentwerke:prompt>` | | Per-task instructions (inline attr, file path, or child element for multi-line). Supports `{{…}}` interpolation. |
| `includeAgentOutput` / `outputFrom` | | For `github.create_pull_request`: include prior agent output in the PR (all `output.*`, or a specific `output.<nodeId>`). |
| `maxRetries`, `retryBackoffSeconds` | | Retry policy for the step. |

### Deterministic tool actions (no model call)

These dispatch straight to the Tool Gateway connector — no tokens spent:

`github.read_issue`, `github.create_branch`, `github.create_pull_request`
(`github.create_pr`), `github.request_review`, `github.post_review`,
`cicd.trigger_deploy`, and registered `mcp.*` tools.

## `agentwerke:approvalTask` (on `userTask`)

A human-in-the-loop approval gate. The run pauses (`awaiting_approval`) until a
decision is posted to `POST /api/approvals/{id}/decision`.

```xml
<bpmn:userTask id="Review" name="Code Review">
  <bpmn:extensionElements>
    <agentwerke:approvalTask purposeType="code_review" policyTag="human-code-review" />
  </bpmn:extensionElements>
</bpmn:userTask>
```

| Attribute | Req | Notes |
| --- | --- | --- |
| `purposeType` | ✅ | Shown on the approval card; drives risk display. |
| `policyTag` | ✅ | Policy bucket (influences risk level). |

## `agentwerke:externalEvent` (on `receiveTask` / message `intermediateCatchEvent`)

Waits for an inbound event (e.g. a merged PR or green CI) correlated to the run.

```xml
<bpmn:intermediateCatchEvent id="WaitForMerge">
  <bpmn:extensionElements>
    <agentwerke:externalEvent messageName="github.pull_request.merged"
      correlationKeyTemplate="{{input.branch_name}}" />
  </bpmn:extensionElements>
  <bpmn:messageEventDefinition />
</bpmn:intermediateCatchEvent>
```

| Attribute | Req | Notes |
| --- | --- | --- |
| `messageName` | ✅ | Event type to wait for. |
| `correlationKeyTemplate` | ✅ | Templated key matched against the inbound event. |

## Timers

A `intermediateCatchEvent` with a `<bpmn:timerEventDefinition>` pauses the run for
the configured duration.

## Run-context variables (`{{…}}`)

Prompts and templates can interpolate run context:

- `{{input.*}}` — seeded at start (e.g. from a triggering issue: `input.title`, `input.body`).
- `{{output.<NodeId>}}` — the output of a prior agent step.
- `{{event.*}}` — payload merged from a resumed external event.
- Plus task fields: `{{run_id}}`, `{{step_id}}`, `{{node_name}}`, `{{agent_name}}`, `{{action}}`, etc.
