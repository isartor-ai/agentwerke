# BPMN Extensions

Autofac runs standard BPMN 2.0 with `autofac:` extension elements in the namespace:

```text
https://autofac.de/bpmn/extensions/v1
```

Include BPMN diagram layout information so the workflow renders correctly in the designer and run detail views.

## `autofac:agentTask`

Use `autofac:agentTask` on a `serviceTask` or `scriptTask` to make a task executable by an agent or deterministic tool.

```xml
<bpmn:serviceTask id="Implement" name="Implement">
  <bpmn:extensionElements>
    <autofac:agentTask
      agent="implementation-engineer"
      action="implement"
      executionMode="agent_sandboxed"
      sandboxProfile="repo-write"
      purposeType="implementation"
      policyTag="repo-change"
      permissionLevel="read-write"
      allowedTools="sandbox.file_write,sandbox.git">
      <autofac:prompt>Implement the change described in {{input.body}}.</autofac:prompt>
    </autofac:agentTask>
  </bpmn:extensionElements>
</bpmn:serviceTask>
```

| Attribute | Required | Notes |
| --- | --- | --- |
| `agent` | Yes | Agent id. Unknown ids run with a generic prompt. |
| `action` | Yes | Drives behavior. Some deterministic actions skip the model. |
| `purposeType` | Yes | Purpose label used by policy and risk. |
| `policyTag` | Yes | Policy bucket used by the policy engine. |
| `environment` | No | Example: `ci`, `staging`, `production`. |
| `requiresEvidence` | No | CSV of required evidence items. |
| `executionMode` | No | `local`, `tool_sandboxed`, or `agent_sandboxed`. |
| `sandboxProfile` | No | `offline`, `repo-read`, `repo-write`, or `deployment`. |
| `permissionLevel` | No | `read-only`, `read-write`, or `full`. |
| `allowedTools` / `deniedTools` | No | CSV allow/deny lists narrowing the agent's tools. |
| `prompt`, `promptFile`, `autofac:prompt` | No | Task instructions with run-context interpolation. |
| `includeAgentOutput` / `outputFrom` | No | Include prior output in a pull request body. |
| `maxRetries`, `retryBackoffSeconds` | No | Retry policy. |

## Deterministic tool actions

These actions dispatch to the Tool Gateway without a model call:

- `github.read_issue`
- `github.create_branch`
- `github.create_pull_request`
- `github.create_pr`
- `github.request_review`
- `github.post_review`
- `cicd.trigger_deploy`
- registered `mcp.*` tools

## `autofac:approvalTask`

Use `autofac:approvalTask` on a BPMN `userTask`.

```xml
<bpmn:userTask id="Review" name="Code Review">
  <bpmn:extensionElements>
    <autofac:approvalTask purposeType="code_review" policyTag="human-code-review" />
  </bpmn:extensionElements>
</bpmn:userTask>
```

| Attribute | Required | Notes |
| --- | --- | --- |
| `purposeType` | Yes | Shown on approval cards and used for risk display. |
| `policyTag` | Yes | Policy bucket for the approval boundary. |

The run pauses until a decision is posted to `POST /api/approvals/{id}/decision`.

## `autofac:externalEvent`

Use `autofac:externalEvent` on a `receiveTask` or message `intermediateCatchEvent`.

```xml
<bpmn:intermediateCatchEvent id="WaitForMerge">
  <bpmn:extensionElements>
    <autofac:externalEvent
      messageName="github.pull_request.merged"
      correlationKeyTemplate="{{input.branch_name}}" />
  </bpmn:extensionElements>
  <bpmn:messageEventDefinition />
</bpmn:intermediateCatchEvent>
```

| Attribute | Required | Notes |
| --- | --- | --- |
| `messageName` | Yes | Event type to wait for. |
| `correlationKeyTemplate` | Yes | Templated key matched against the inbound event. |

## Timers

An intermediate catch event with a BPMN timer event definition pauses the run for the configured duration.

## Run-context variables

| Variable | Description |
| --- | --- |
| <code v-pre>{{input.*}}</code> | Values seeded at run start. |
| <code v-pre>{{output.&lt;NodeId&gt;}}</code> | Output of a prior step. |
| <code v-pre>{{event.*}}</code> | Payload from a resumed external event. |
| <code v-pre>{{run_id}}</code> | Current run id. |
| <code v-pre>{{step_id}}</code> | Current step id. |
| <code v-pre>{{node_name}}</code> | Current BPMN node name. |
| <code v-pre>{{agent_name}}</code> | Current agent name. |
| <code v-pre>{{action}}</code> | Current task action. |
