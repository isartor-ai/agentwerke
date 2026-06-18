-- Autofac pilot test seed data
-- Covers 4 scenarios: GitHub-only, approval gate, policy block, LLM agent.
-- Idempotent: all inserts use ON CONFLICT DO NOTHING / DO UPDATE.
-- Run after EF Core migrations have created the schema.

-- ── Scenario A: GitHub Branch + PR (wf-pilot-001) ──────────────────────────
-- Two sequential GitHub service tasks; both route to WireMock without an API key.

INSERT INTO autofac.workflows (
    "Id", "Name", "Description", "Version", "Status", "Owner",
    "CreatedAt", "LastEditedAt", "ValidationState", "Tags", "BpmnXml"
) VALUES (
    'wf-pilot-001',
    'GitHub Branch + PR',
    'Scenario A: creates a branch then opens a PR via WireMock. No API key needed.',
    'v1.0.0',
    'active',
    'platform-eng',
    '2026-06-18T09:00:00.000Z',
    '2026-06-18T09:00:00.000Z',
    'valid',
    '["pilot","github"]',
    '<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions
    xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
    xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
    xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
    xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
    xmlns:autofac="https://autofac.ai/bpmn"
    id="pilot-github-defs"
    targetNamespace="https://autofac.ai/bpmn">
  <bpmn:process id="pilot-github" name="GitHub Branch + PR" isExecutable="true">
    <bpmn:startEvent id="Start" name="Start">
      <bpmn:outgoing>Flow1</bpmn:outgoing>
    </bpmn:startEvent>
    <bpmn:serviceTask id="CreateBranch" name="Create Feature Branch">
      <bpmn:extensionElements>
        <autofac:agentTask agent="platform-agent" action="github.create_branch" environment="ci" purposeType="delivery" policyTag="standard" requiresEvidence="" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow1</bpmn:incoming>
      <bpmn:outgoing>Flow2</bpmn:outgoing>
    </bpmn:serviceTask>
    <bpmn:serviceTask id="CreatePR" name="Open Pull Request">
      <bpmn:extensionElements>
        <autofac:agentTask agent="platform-agent" action="github.create_pull_request" environment="ci" purposeType="delivery" policyTag="standard" requiresEvidence="" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow2</bpmn:incoming>
      <bpmn:outgoing>Flow3</bpmn:outgoing>
    </bpmn:serviceTask>
    <bpmn:endEvent id="End" name="End">
      <bpmn:incoming>Flow3</bpmn:incoming>
    </bpmn:endEvent>
    <bpmn:sequenceFlow id="Flow1" sourceRef="Start" targetRef="CreateBranch" />
    <bpmn:sequenceFlow id="Flow2" sourceRef="CreateBranch" targetRef="CreatePR" />
    <bpmn:sequenceFlow id="Flow3" sourceRef="CreatePR" targetRef="End" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_A">
    <bpmndi:BPMNPlane id="BPMNPlane_A" bpmnElement="pilot-github">
      <bpmndi:BPMNShape id="Start_di" bpmnElement="Start">
        <dc:Bounds x="152" y="142" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="155" y="185" width="30" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="CreateBranch_di" bpmnElement="CreateBranch">
        <dc:Bounds x="240" y="120" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="CreatePR_di" bpmnElement="CreatePR">
        <dc:Bounds x="400" y="120" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="End_di" bpmnElement="End">
        <dc:Bounds x="562" y="142" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="569" y="185" width="22" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow1_di" bpmnElement="Flow1">
        <di:waypoint x="188" y="160" /><di:waypoint x="240" y="160" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow2_di" bpmnElement="Flow2">
        <di:waypoint x="340" y="160" /><di:waypoint x="400" y="160" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow3_di" bpmnElement="Flow3">
        <di:waypoint x="500" y="160" /><di:waypoint x="562" y="160" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>'
) ON CONFLICT ("Id") DO UPDATE SET "BpmnXml" = EXCLUDED."BpmnXml";

-- ── Scenario B: Approval Gate (wf-pilot-002) ────────────────────────────────
-- Branch creation (WireMock) → human userTask approval gate.

INSERT INTO autofac.workflows (
    "Id", "Name", "Description", "Version", "Status", "Owner",
    "CreatedAt", "LastEditedAt", "ValidationState", "Tags", "BpmnXml"
) VALUES (
    'wf-pilot-002',
    'Branch + Approval Gate',
    'Scenario B: creates a branch then waits for human approval before completing.',
    'v1.0.0',
    'active',
    'platform-eng',
    '2026-06-18T09:00:00.000Z',
    '2026-06-18T09:00:00.000Z',
    'valid',
    '["pilot","approval"]',
    '<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions
    xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
    xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
    xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
    xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
    xmlns:autofac="https://autofac.ai/bpmn"
    id="pilot-approval-defs"
    targetNamespace="https://autofac.ai/bpmn">
  <bpmn:process id="pilot-approval" name="Branch + Approval Gate" isExecutable="true">
    <bpmn:startEvent id="Start" name="Start">
      <bpmn:outgoing>Flow1</bpmn:outgoing>
    </bpmn:startEvent>
    <bpmn:serviceTask id="CreateBranch" name="Create Feature Branch">
      <bpmn:extensionElements>
        <autofac:agentTask agent="platform-agent" action="github.create_branch" environment="ci" purposeType="delivery" policyTag="standard" requiresEvidence="" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow1</bpmn:incoming>
      <bpmn:outgoing>Flow2</bpmn:outgoing>
    </bpmn:serviceTask>
    <bpmn:userTask id="ReviewAndApprove" name="Review and Approve">
      <bpmn:extensionElements>
        <autofac:approvalTask purposeType="human-approval" policyTag="standard-deploy" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow2</bpmn:incoming>
      <bpmn:outgoing>Flow3</bpmn:outgoing>
    </bpmn:userTask>
    <bpmn:endEvent id="End" name="End">
      <bpmn:incoming>Flow3</bpmn:incoming>
    </bpmn:endEvent>
    <bpmn:sequenceFlow id="Flow1" sourceRef="Start" targetRef="CreateBranch" />
    <bpmn:sequenceFlow id="Flow2" sourceRef="CreateBranch" targetRef="ReviewAndApprove" />
    <bpmn:sequenceFlow id="Flow3" sourceRef="ReviewAndApprove" targetRef="End" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_B">
    <bpmndi:BPMNPlane id="BPMNPlane_B" bpmnElement="pilot-approval">
      <bpmndi:BPMNShape id="Start_di" bpmnElement="Start">
        <dc:Bounds x="152" y="142" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="155" y="185" width="30" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="CreateBranch_di" bpmnElement="CreateBranch">
        <dc:Bounds x="240" y="120" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="ReviewAndApprove_di" bpmnElement="ReviewAndApprove">
        <dc:Bounds x="400" y="120" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="End_di" bpmnElement="End">
        <dc:Bounds x="562" y="142" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="569" y="185" width="22" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow1_di" bpmnElement="Flow1">
        <di:waypoint x="188" y="160" /><di:waypoint x="240" y="160" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow2_di" bpmnElement="Flow2">
        <di:waypoint x="340" y="160" /><di:waypoint x="400" y="160" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow3_di" bpmnElement="Flow3">
        <di:waypoint x="500" y="160" /><di:waypoint x="562" y="160" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>'
) ON CONFLICT ("Id") DO UPDATE SET "BpmnXml" = EXCLUDED."BpmnXml";

-- ── Scenario C: Policy Block (wf-pilot-003) ─────────────────────────────────
-- Action "access.credential_store" matches the "Block secret and credential
-- material access" rule (action contains "access", text contains "credential").
-- The step fails immediately with PolicyDecision.Kind = "reject"; no LLM call.

INSERT INTO autofac.workflows (
    "Id", "Name", "Description", "Version", "Status", "Owner",
    "CreatedAt", "LastEditedAt", "ValidationState", "Tags", "BpmnXml"
) VALUES (
    'wf-pilot-003',
    'Policy Block Demo',
    'Scenario C: action "access.credential_store" triggers the built-in secret-block policy. Run fails immediately.',
    'v1.0.0',
    'active',
    'platform-eng',
    '2026-06-18T09:00:00.000Z',
    '2026-06-18T09:00:00.000Z',
    'valid',
    '["pilot","policy"]',
    '<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions
    xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
    xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
    xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
    xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
    xmlns:autofac="https://autofac.ai/bpmn"
    id="pilot-policy-defs"
    targetNamespace="https://autofac.ai/bpmn">
  <bpmn:process id="pilot-policy" name="Policy Block Demo" isExecutable="true">
    <bpmn:startEvent id="Start" name="Start">
      <bpmn:outgoing>Flow1</bpmn:outgoing>
    </bpmn:startEvent>
    <bpmn:serviceTask id="AccessSecrets" name="Access Credential Store">
      <bpmn:extensionElements>
        <autofac:agentTask agent="security-analyst" action="access.credential_store" environment="ci" purposeType="credential-access" policyTag="standard" requiresEvidence="" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow1</bpmn:incoming>
      <bpmn:outgoing>Flow2</bpmn:outgoing>
    </bpmn:serviceTask>
    <bpmn:endEvent id="End" name="End">
      <bpmn:incoming>Flow2</bpmn:incoming>
    </bpmn:endEvent>
    <bpmn:sequenceFlow id="Flow1" sourceRef="Start" targetRef="AccessSecrets" />
    <bpmn:sequenceFlow id="Flow2" sourceRef="AccessSecrets" targetRef="End" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_C">
    <bpmndi:BPMNPlane id="BPMNPlane_C" bpmnElement="pilot-policy">
      <bpmndi:BPMNShape id="Start_di" bpmnElement="Start">
        <dc:Bounds x="152" y="142" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="155" y="185" width="30" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="AccessSecrets_di" bpmnElement="AccessSecrets">
        <dc:Bounds x="240" y="120" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="End_di" bpmnElement="End">
        <dc:Bounds x="400" y="142" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="407" y="185" width="22" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow1_di" bpmnElement="Flow1">
        <di:waypoint x="188" y="160" /><di:waypoint x="240" y="160" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow2_di" bpmnElement="Flow2">
        <di:waypoint x="340" y="160" /><di:waypoint x="400" y="160" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>'
) ON CONFLICT ("Id") DO UPDATE SET "BpmnXml" = EXCLUDED."BpmnXml";

-- ── Scenario D: LLM Agent (wf-pilot-004) ────────────────────────────────────
-- Requires ANTHROPIC__APIKEY. Without it, NullLanguageModelClient returns a
-- clear failure message. With it, the real Anthropic API is called.

INSERT INTO autofac.workflows (
    "Id", "Name", "Description", "Version", "Status", "Owner",
    "CreatedAt", "LastEditedAt", "ValidationState", "Tags", "BpmnXml"
) VALUES (
    'wf-pilot-004',
    'LLM Agent Task',
    'Scenario D: calls the Anthropic API to generate a spec. Fails gracefully without ANTHROPIC__APIKEY.',
    'v1.0.0',
    'active',
    'platform-eng',
    '2026-06-18T09:00:00.000Z',
    '2026-06-18T09:00:00.000Z',
    'valid',
    '["pilot","llm"]',
    '<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions
    xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
    xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
    xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
    xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
    xmlns:autofac="https://autofac.ai/bpmn"
    id="pilot-llm-defs"
    targetNamespace="https://autofac.ai/bpmn">
  <bpmn:process id="pilot-llm" name="LLM Agent Task" isExecutable="true">
    <bpmn:startEvent id="Start" name="Start">
      <bpmn:outgoing>Flow1</bpmn:outgoing>
    </bpmn:startEvent>
    <bpmn:serviceTask id="GenerateSpec" name="Generate Specification">
      <bpmn:extensionElements>
        <autofac:agentTask agent="spec-writer" action="spec.generate" environment="ci" purposeType="specification" policyTag="sdlc-spec" requiresEvidence="" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow1</bpmn:incoming>
      <bpmn:outgoing>Flow2</bpmn:outgoing>
    </bpmn:serviceTask>
    <bpmn:endEvent id="End" name="End">
      <bpmn:incoming>Flow2</bpmn:incoming>
    </bpmn:endEvent>
    <bpmn:sequenceFlow id="Flow1" sourceRef="Start" targetRef="GenerateSpec" />
    <bpmn:sequenceFlow id="Flow2" sourceRef="GenerateSpec" targetRef="End" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_D">
    <bpmndi:BPMNPlane id="BPMNPlane_D" bpmnElement="pilot-llm">
      <bpmndi:BPMNShape id="Start_di" bpmnElement="Start">
        <dc:Bounds x="152" y="142" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="155" y="185" width="30" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="GenerateSpec_di" bpmnElement="GenerateSpec">
        <dc:Bounds x="240" y="120" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="End_di" bpmnElement="End">
        <dc:Bounds x="400" y="142" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="407" y="185" width="22" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow1_di" bpmnElement="Flow1">
        <di:waypoint x="188" y="160" /><di:waypoint x="240" y="160" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow2_di" bpmnElement="Flow2">
        <di:waypoint x="340" y="160" /><di:waypoint x="400" y="160" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>'
) ON CONFLICT ("Id") DO UPDATE SET "BpmnXml" = EXCLUDED."BpmnXml";

-- ── Pre-baked runs ─────────────────────────────────────────────────────────
-- These static runs let you browse history immediately without waiting for
-- live execution. Trigger new runs from the UI to see live behaviour.

-- Scenario A completed run
INSERT INTO autofac.workflow_runs (
    "Id", "WorkflowId", "WorkflowName", "WorkflowVersion", "Status",
    "RiskLevel", "CurrentStep", "RequestedBy", "StartedAt", "CompletedAt",
    "DurationMs", "PendingApprovals", "Tags", "CorrelationId"
) VALUES (
    'run-pilot-001',
    'wf-pilot-001',
    'GitHub Branch + PR',
    'v1.0.0',
    'completed',
    'medium',
    '',
    'dev:admin',
    '2026-06-18T09:10:00.000Z',
    '2026-06-18T09:10:08.000Z',
    8200,
    0,
    '["pilot","github"]',
    NULL
) ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_run_steps (
    "Id", "RunId", "Name", "Type", "Status", "StartedAt", "CompletedAt", "AgentName", "Output", "Error"
) VALUES
(
    'step-p001-1', 'run-pilot-001', 'Start', 'start_event', 'completed',
    '2026-06-18T09:10:00.000Z', '2026-06-18T09:10:00.200Z', NULL, NULL, NULL
),
(
    'step-p001-2', 'run-pilot-001', 'Create Feature Branch', 'service_task', 'completed',
    '2026-06-18T09:10:00.200Z', '2026-06-18T09:10:04.100Z', 'platform-agent',
    '{"branch":"autofac/run-run-pilot-001","html_url":"https://github.com/mock/repo/tree/autofac/run-run-pilot-001","state":"open"}',
    NULL
),
(
    'step-p001-3', 'run-pilot-001', 'Open Pull Request', 'service_task', 'completed',
    '2026-06-18T09:10:04.100Z', '2026-06-18T09:10:07.800Z', 'platform-agent',
    '{"number":1,"html_url":"https://github.com/mock/repo/pull/1","state":"open"}',
    NULL
),
(
    'step-p001-4', 'run-pilot-001', 'End', 'end_event', 'completed',
    '2026-06-18T09:10:07.800Z', '2026-06-18T09:10:08.000Z', NULL, NULL, NULL
)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_events (
    "Id", "RunId", "Type", "Message", "CreatedAt"
) VALUES
(
    'evt-p001-1', 'run-pilot-001', 'task_started',
    'Create Feature Branch started (github.create_branch → WireMock).',
    '2026-06-18T09:10:00.200Z'
),
(
    'evt-p001-2', 'run-pilot-001', 'task_completed',
    'Branch autofac/run-run-pilot-001 created successfully via WireMock stub.',
    '2026-06-18T09:10:04.100Z'
),
(
    'evt-p001-3', 'run-pilot-001', 'task_started',
    'Open Pull Request started (github.create_pull_request → WireMock).',
    '2026-06-18T09:10:04.100Z'
),
(
    'evt-p001-4', 'run-pilot-001', 'task_completed',
    'Pull request #1 opened successfully via WireMock stub.',
    '2026-06-18T09:10:07.800Z'
),
(
    'evt-p001-5', 'run-pilot-001', 'workflow_completed',
    'GitHub Branch + PR workflow completed successfully.',
    '2026-06-18T09:10:08.000Z'
)
ON CONFLICT ("Id") DO NOTHING;

-- Scenario B — awaiting approval run
INSERT INTO autofac.workflow_runs (
    "Id", "WorkflowId", "WorkflowName", "WorkflowVersion", "Status",
    "RiskLevel", "CurrentStep", "RequestedBy", "StartedAt", "CompletedAt",
    "DurationMs", "PendingApprovals", "Tags", "CorrelationId"
) VALUES (
    'run-pilot-002',
    'wf-pilot-002',
    'Branch + Approval Gate',
    'v1.0.0',
    'waiting_user',
    'medium',
    'Review and Approve',
    'dev:admin',
    to_char(NOW() - INTERVAL '5 minutes', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    NULL,
    NULL,
    1,
    '["pilot","approval"]',
    NULL
) ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_run_steps (
    "Id", "RunId", "Name", "Type", "Status", "StartedAt", "CompletedAt", "AgentName", "Output", "Error",
    "PolicyDecision_Kind", "PolicyDecision_PolicyId", "PolicyDecision_PolicyName",
    "PolicyDecision_Rationale", "PolicyDecision_RiskScore", "PolicyDecision_RiskLevel",
    "PolicyDecision_RiskFactors", "PolicyDecision_Constraints", "PolicyDecision_DecidedAt"
) VALUES
(
    'step-p002-1', 'run-pilot-002', 'Start', 'start_event', 'completed',
    to_char(NOW() - INTERVAL '5 minutes', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    to_char(NOW() - INTERVAL '4 minutes 59 seconds', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
),
(
    'step-p002-2', 'run-pilot-002', 'Create Feature Branch', 'service_task', 'completed',
    to_char(NOW() - INTERVAL '4 minutes 59 seconds', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    to_char(NOW() - INTERVAL '4 minutes 55 seconds', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    'platform-agent',
    '{"branch":"autofac/run-run-pilot-002","html_url":"https://github.com/mock/repo/tree/autofac/run-run-pilot-002","state":"open"}',
    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
),
(
    'step-p002-3', 'run-pilot-002', 'Review and Approve', 'user_task', 'waiting_user',
    to_char(NOW() - INTERVAL '4 minutes 55 seconds', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    NULL, NULL, NULL, NULL,
    'escalate', 'pol-prod-deploy', 'Production Deploy Gate',
    'Deployment to production requires human sign-off.',
    72, 'medium', '["pilot","delivery"]', '[]',
    to_char(NOW() - INTERVAL '4 minutes 55 seconds', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"')
)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_events (
    "Id", "RunId", "Type", "Message", "CreatedAt"
) VALUES
(
    'evt-p002-1', 'run-pilot-002', 'task_completed',
    'Branch autofac/run-run-pilot-002 created via WireMock.',
    to_char(NOW() - INTERVAL '4 minutes 55 seconds', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"')
),
(
    'evt-p002-2', 'run-pilot-002', 'approval_requested',
    'Review and Approve is waiting for human decision (Approve or Reject in the Approvals tab).',
    to_char(NOW() - INTERVAL '4 minutes 55 seconds', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"')
)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.approval_requests (
    "Id", "RunId", "WorkflowName", "ActionRequested", "Requester", "AgentName",
    "PolicyRationale", "RiskScore", "RiskLevel", "RiskFactors", "AffectedSystems",
    "SlaDeadline", "CreatedAt", "Status", "Priority",
    "DecisionComment", "DecidedAt", "DecidedBy"
) VALUES (
    'apr-pilot-001',
    'run-pilot-002',
    'Branch + Approval Gate',
    'Merge branch autofac/run-run-pilot-002 and deploy to production',
    'dev:admin',
    'platform-agent',
    'Deployment to production requires human sign-off.',
    72,
    'medium',
    '["pilot","delivery"]',
    '["production/api"]',
    to_char(NOW() + INTERVAL '2 hours', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    to_char(NOW() - INTERVAL '4 minutes 55 seconds', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    'pending',
    'medium',
    NULL, NULL, NULL
) ON CONFLICT ("Id") DO NOTHING;

-- Scenario C — policy-blocked run
INSERT INTO autofac.workflow_runs (
    "Id", "WorkflowId", "WorkflowName", "WorkflowVersion", "Status",
    "RiskLevel", "CurrentStep", "RequestedBy", "StartedAt", "CompletedAt",
    "DurationMs", "PendingApprovals", "Tags", "CorrelationId"
) VALUES (
    'run-pilot-003',
    'wf-pilot-003',
    'Policy Block Demo',
    'v1.0.0',
    'failed',
    'critical',
    'Access Credential Store',
    'dev:admin',
    '2026-06-18T09:20:00.000Z',
    '2026-06-18T09:20:00.340Z',
    340,
    0,
    '["pilot","policy"]',
    NULL
) ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_run_steps (
    "Id", "RunId", "Name", "Type", "Status", "StartedAt", "CompletedAt", "AgentName", "Output", "Error",
    "PolicyDecision_Kind", "PolicyDecision_PolicyId", "PolicyDecision_PolicyName",
    "PolicyDecision_Rationale", "PolicyDecision_RiskScore", "PolicyDecision_RiskLevel",
    "PolicyDecision_RiskFactors", "PolicyDecision_Constraints", "PolicyDecision_DecidedAt"
) VALUES
(
    'step-p003-1', 'run-pilot-003', 'Start', 'start_event', 'completed',
    '2026-06-18T09:20:00.000Z', '2026-06-18T09:20:00.050Z',
    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
),
(
    'step-p003-2', 'run-pilot-003', 'Access Credential Store', 'service_task', 'failed',
    '2026-06-18T09:20:00.050Z', '2026-06-18T09:20:00.340Z',
    'security-analyst', NULL,
    'Secret and credential access actions are blocked in the MVP policy layer.',
    'reject', 'rule-secret-material-block', 'Block secret and credential material access',
    'Secret and credential access actions are blocked in the MVP policy layer.',
    95, 'critical', '["Secret or credential scope requested"]',
    '["Use a dedicated secret-management workflow with human approval.","Do not expose secret material through agent output or artifacts."]',
    '2026-06-18T09:20:00.340Z'
)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_events (
    "Id", "RunId", "Type", "Message", "CreatedAt"
) VALUES
(
    'evt-p003-1', 'run-pilot-003', 'policy_rejected',
    'Policy "Block secret and credential material access" rejected action "access.credential_store" (risk: critical, score: 95).',
    '2026-06-18T09:20:00.340Z'
),
(
    'evt-p003-2', 'run-pilot-003', 'workflow_failed',
    'Workflow failed: Secret and credential access actions are blocked in the MVP policy layer.',
    '2026-06-18T09:20:00.340Z'
)
ON CONFLICT ("Id") DO NOTHING;

-- Scenario D — LLM agent run (no API key, shows graceful failure)
INSERT INTO autofac.workflow_runs (
    "Id", "WorkflowId", "WorkflowName", "WorkflowVersion", "Status",
    "RiskLevel", "CurrentStep", "RequestedBy", "StartedAt", "CompletedAt",
    "DurationMs", "PendingApprovals", "Tags", "CorrelationId"
) VALUES (
    'run-pilot-004',
    'wf-pilot-004',
    'LLM Agent Task',
    'v1.0.0',
    'failed',
    'low',
    'Generate Specification',
    'dev:admin',
    '2026-06-18T09:25:00.000Z',
    '2026-06-18T09:25:00.180Z',
    180,
    0,
    '["pilot","llm"]',
    NULL
) ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_run_steps (
    "Id", "RunId", "Name", "Type", "Status", "StartedAt", "CompletedAt", "AgentName", "Output", "Error"
) VALUES
(
    'step-p004-1', 'run-pilot-004', 'Start', 'start_event', 'completed',
    '2026-06-18T09:25:00.000Z', '2026-06-18T09:25:00.050Z', NULL, NULL, NULL
),
(
    'step-p004-2', 'run-pilot-004', 'Generate Specification', 'service_task', 'failed',
    '2026-06-18T09:25:00.050Z', '2026-06-18T09:25:00.180Z', 'spec-writer', NULL,
    'Language model is not configured. Set Anthropic__ApiKey (or Anthropic:ApiKey) to enable real LLM calls.'
)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_events (
    "Id", "RunId", "Type", "Message", "CreatedAt"
) VALUES
(
    'evt-p004-1', 'run-pilot-004', 'task_failed',
    'Generate Specification failed: language model client is not configured (no API key).',
    '2026-06-18T09:25:00.180Z'
),
(
    'evt-p004-2', 'run-pilot-004', 'workflow_failed',
    'Set ANTHROPIC__APIKEY in .env.pilot and restart to enable real LLM calls.',
    '2026-06-18T09:25:00.180Z'
)
ON CONFLICT ("Id") DO NOTHING;
