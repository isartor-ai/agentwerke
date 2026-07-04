# Approvals And Evidence

Agentwerke uses approval gates to keep humans in the loop where the process or policy requires it. Evidence packs make the completed run reviewable after the fact.

## Approval gates

A workflow author adds an approval gate by placing a BPMN user task with an `autofac:approvalTask` extension. When the run reaches that task, Agentwerke creates an approval request and pauses the run.

Approvers should review:

- The requested action.
- The requester and agent name.
- The policy rationale.
- Risk score and risk factors.
- Affected systems.
- Related step output and artifacts.

## Decide an approval

From the UI, open the approval request and choose the appropriate decision.

From the API:

```bash
curl -sf "$API/api/approvals/<approval-id>/decision" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"decision":"approve","comment":"Reviewed generated output and risk rationale."}'
```

Use a comment that explains the human judgment. The decision comment becomes part of the audit trail.

## Approval outcomes

| Decision | Result |
| --- | --- |
| Approve | The run resumes and continues past the approval task. |
| Reject | The run stops or follows the workflow's rejection path, depending on the modeled process. |
| Request changes | The workflow can route back to an agent or authoring step if modeled. |

## Blocking human questions

Agents can also ask a human a blocking question with `human.ask`. This is different from a formal approval task. The run pauses with status `waiting_user`, the question appears in the run conversation, and the agent step resumes after the answer is recorded.

Use `human.ask` for missing information. Use approval tasks for governed decisions.

## Evidence pack contents

An evidence pack is a schema-versioned JSON record of the run. It can include:

- Workflow identity and BPMN hash.
- Step history.
- Redacted prompts and model usage.
- Tool invocations and policy decisions.
- Sandbox executions.
- Approval requests and decisions.
- Artifacts and artifact references.
- Audit log entries.

## Download evidence

```bash
curl -sf "$API/api/runs/<run-id>/evidence-pack" \
  -H "Authorization: Bearer $TOKEN" | jq
```

For file download flows:

```bash
curl -sf "$API/api/runs/<run-id>/evidence-pack/download" \
  -H "Authorization: Bearer $TOKEN" \
  -o evidence-pack.json
```

## Evidence handling

- Treat evidence packs as audit records. Store them where your organization keeps delivery and compliance records.
- Do not place secrets in prompts, run context, or artifacts. Agentwerke redacts known secret paths, but operators are still responsible for safe input handling.
- Preserve evidence before deleting test stacks or local volumes.
- When investigating an incident, correlate evidence timestamps with API logs, connector logs, and CI/CD logs.
