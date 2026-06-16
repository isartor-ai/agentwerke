-- Autofac manual-test seed data
-- Idempotent: all inserts use ON CONFLICT DO NOTHING.
-- Run after EF Core migrations have created the schema.

-- ── Workflow definition ────────────────────────────────────────────────────

INSERT INTO autofac.workflows (
    "Id", "Name", "Description", "Version", "Status", "Owner",
    "CreatedAt", "LastEditedAt", "ValidationState", "Tags", "BpmnXml"
) VALUES (
    'wf-manual-001',
    'E2E Simple Workflow',
    'Demo workflow: security analysis followed by a human approval gate',
    'v1.0.0',
    'active',
    'platform-eng',
    '2026-06-15T10:00:00.000Z',
    '2026-06-15T10:00:00.000Z',
    'valid',
    '["demo"]',
    '<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions
    xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
    xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
    xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
    xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
    xmlns:autofac="https://autofac.ai/bpmn"
    id="e2e-simple-defs"
    targetNamespace="https://autofac.ai/bpmn">
  <bpmn:process id="e2e-simple" name="E2E Simple Workflow" isExecutable="true">
    <bpmn:startEvent id="Start" name="Start">
      <bpmn:outgoing>Flow1</bpmn:outgoing>
    </bpmn:startEvent>
    <bpmn:serviceTask id="RunAnalysis" name="Run Analysis">
      <bpmn:extensionElements>
        <autofac:agentTask agent="security-analyst" action="run-analysis" environment="ci" purposeType="security-scan" policyTag="standard" requiresEvidence="" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow1</bpmn:incoming>
      <bpmn:outgoing>Flow2</bpmn:outgoing>
    </bpmn:serviceTask>
    <bpmn:userTask id="ApproveDeployment" name="Approve Deployment">
      <bpmn:extensionElements>
        <autofac:approvalTask purposeType="human-approval" policyTag="standard-deploy" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow2</bpmn:incoming>
      <bpmn:outgoing>Flow3</bpmn:outgoing>
    </bpmn:userTask>
    <bpmn:endEvent id="End" name="End">
      <bpmn:incoming>Flow3</bpmn:incoming>
    </bpmn:endEvent>
    <bpmn:sequenceFlow id="Flow1" sourceRef="Start" targetRef="RunAnalysis" />
    <bpmn:sequenceFlow id="Flow2" sourceRef="RunAnalysis" targetRef="ApproveDeployment" />
    <bpmn:sequenceFlow id="Flow3" sourceRef="ApproveDeployment" targetRef="End" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="e2e-simple">
      <bpmndi:BPMNShape id="Start_di" bpmnElement="Start">
        <dc:Bounds x="152" y="142" width="36" height="36" />
        <bpmndi:BPMNLabel><dc:Bounds x="155" y="185" width="30" height="14" /></bpmndi:BPMNLabel>
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="RunAnalysis_di" bpmnElement="RunAnalysis">
        <dc:Bounds x="240" y="120" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="ApproveDeployment_di" bpmnElement="ApproveDeployment">
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

-- ── Completed run ──────────────────────────────────────────────────────────

INSERT INTO autofac.workflow_runs (
    "Id", "WorkflowId", "WorkflowName", "WorkflowVersion", "Status",
    "RiskLevel", "CurrentStep", "RequestedBy", "StartedAt", "CompletedAt",
    "DurationMs", "PendingApprovals", "Tags", "CorrelationId"
) VALUES (
    'run-manual-001',
    'wf-manual-001',
    'E2E Simple Workflow',
    'v1.0.0',
    'completed',
    'medium',
    '',
    'dev:admin',
    '2026-06-15T10:00:00.000Z',
    '2026-06-15T10:05:12.000Z',
    312000,
    0,
    '["demo"]',
    NULL
) ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_run_steps (
    "Id", "RunId", "Name", "Type", "Status", "StartedAt", "CompletedAt", "AgentName", "Output", "Error"
) VALUES
(
    'step-m001-1', 'run-manual-001', 'Start',           'start_event',  'completed',
    '2026-06-15T10:00:00.000Z', '2026-06-15T10:00:01.000Z', NULL, NULL, NULL
),
(
    'step-m001-2', 'run-manual-001', 'Run Analysis',    'service_task', 'completed',
    '2026-06-15T10:00:01.000Z', '2026-06-15T10:03:30.000Z', 'security-analyst', 'No vulnerabilities found.', NULL
),
(
    'step-m001-3', 'run-manual-001', 'Approve Deployment', 'user_task', 'completed',
    '2026-06-15T10:03:30.000Z', '2026-06-15T10:05:00.000Z', NULL, NULL, NULL
),
(
    'step-m001-4', 'run-manual-001', 'End',             'end_event',    'completed',
    '2026-06-15T10:05:00.000Z', '2026-06-15T10:05:12.000Z', NULL, NULL, NULL
)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_events (
    "Id", "RunId", "Type", "Message", "CreatedAt"
) VALUES
(
    'evt-m001-1', 'run-manual-001', 'task_started',
    'Run Analysis task started by security-analyst agent.',
    '2026-06-15T10:00:01.000Z'
),
(
    'evt-m001-2', 'run-manual-001', 'retry_scheduled',
    'Retry scheduled after transient connection failure to GitHub API.',
    '2026-06-15T10:01:15.000Z'
),
(
    'evt-m001-3', 'run-manual-001', 'task_completed',
    'Run Analysis completed successfully. No vulnerabilities found.',
    '2026-06-15T10:03:30.000Z'
),
(
    'evt-m001-4', 'run-manual-001', 'approval_granted',
    'Approve Deployment approved by dev:admin.',
    '2026-06-15T10:05:00.000Z'
),
(
    'evt-m001-5', 'run-manual-001', 'workflow_completed',
    'Workflow run completed successfully.',
    '2026-06-15T10:05:12.000Z'
)
ON CONFLICT ("Id") DO NOTHING;

-- ── Awaiting-approval run ──────────────────────────────────────────────────

INSERT INTO autofac.workflow_runs (
    "Id", "WorkflowId", "WorkflowName", "WorkflowVersion", "Status",
    "RiskLevel", "CurrentStep", "RequestedBy", "StartedAt", "CompletedAt",
    "DurationMs", "PendingApprovals", "Tags", "CorrelationId"
) VALUES (
    'run-manual-002',
    'wf-manual-001',
    'E2E Simple Workflow',
    'v1.0.0',
    'waiting_user',
    'high',
    'Approve Deployment',
    'dev:admin',
    to_char(NOW() - INTERVAL '8 minutes', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    NULL,
    NULL,
    1,
    '["demo","production"]',
    NULL
) ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_run_steps (
    "Id", "RunId", "Name", "Type", "Status", "StartedAt", "CompletedAt", "AgentName", "Output", "Error",
    "PolicyDecision_Kind", "PolicyDecision_PolicyId", "PolicyDecision_PolicyName",
    "PolicyDecision_Rationale", "PolicyDecision_RiskScore", "PolicyDecision_RiskLevel",
    "PolicyDecision_RiskFactors", "PolicyDecision_Constraints", "PolicyDecision_DecidedAt"
) VALUES
(
    'step-m002-1', 'run-manual-002', 'Start',           'start_event',  'completed',
    to_char(NOW() - INTERVAL '8 minutes', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    to_char(NOW() - INTERVAL '7 minutes 59 seconds', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
),
(
    'step-m002-2', 'run-manual-002', 'Run Analysis',    'service_task', 'completed',
    to_char(NOW() - INTERVAL '7 minutes 59 seconds', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    to_char(NOW() - INTERVAL '3 minutes', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    'security-analyst', 'High-risk change detected: production deployment.', NULL,
    NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
),
(
    'step-m002-3', 'run-manual-002', 'Approve Deployment', 'user_task', 'waiting_user',
    to_char(NOW() - INTERVAL '3 minutes', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    NULL, NULL, NULL, NULL,
    'escalate', 'pol-prod-deploy', 'Production Deploy Gate',
    'High-risk production deployment requires human approval before proceeding.',
    82, 'high', '["production","first-deploy"]', '[]',
    to_char(NOW() - INTERVAL '3 minutes', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"')
)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.workflow_events (
    "Id", "RunId", "Type", "Message", "CreatedAt"
) VALUES
(
    'evt-m002-1', 'run-manual-002', 'task_started',
    'Run Analysis task started by security-analyst agent.',
    to_char(NOW() - INTERVAL '7 minutes 59 seconds', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"')
),
(
    'evt-m002-2', 'run-manual-002', 'timeout_triggered',
    'Timeout boundary triggered on Run Analysis: agent took longer than expected.',
    to_char(NOW() - INTERVAL '5 minutes', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"')
),
(
    'evt-m002-3', 'run-manual-002', 'task_completed',
    'Run Analysis completed. High-risk production change detected — escalating to human approval.',
    to_char(NOW() - INTERVAL '3 minutes', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"')
),
(
    'evt-m002-4', 'run-manual-002', 'approval_requested',
    'Approve Deployment is waiting for human decision (policy: Production Deploy Gate, risk: high).',
    to_char(NOW() - INTERVAL '3 minutes', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"')
)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO autofac.approval_requests (
    "Id", "RunId", "WorkflowName", "ActionRequested", "Requester", "AgentName",
    "PolicyRationale", "RiskScore", "RiskLevel", "RiskFactors", "AffectedSystems",
    "SlaDeadline", "CreatedAt", "Status", "Priority",
    "DecisionComment", "DecidedAt", "DecidedBy"
) VALUES (
    'apr-manual-001',
    'run-manual-002',
    'E2E Simple Workflow',
    'Deploy build #4821 to production (first deploy of auth-refactor branch)',
    'dev:admin',
    'security-analyst',
    'Production deployment gate — policy requires a human reviewer to confirm risk acceptance before proceeding.',
    82,
    'high',
    '["production","first-deploy"]',
    '["production/api","production/web"]',
    to_char(NOW() + INTERVAL '1 hour', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    to_char(NOW() - INTERVAL '3 minutes', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"'),
    'pending',
    'high',
    NULL, NULL, NULL
) ON CONFLICT ("Id") DO NOTHING;
