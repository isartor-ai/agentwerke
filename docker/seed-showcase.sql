-- =============================================================================
-- Agentwerke Showcase Seed Data (Deterministic Dataset for Screenshots & Video)
-- Usage:
--   docker exec -i docker-postgres-1 psql -U agentwerke -d agentwerke < docker/seed-showcase.sql
-- =============================================================================

BEGIN;

-- Clear existing runtime data for a clean, deterministic showcase state
TRUNCATE TABLE
  agentwerke.audit_records,
  agentwerke.agent_interactions,
  agentwerke.approval_requests,
  agentwerke.workflow_events,
  agentwerke.workflow_run_steps,
  agentwerke.workflow_runs,
  agentwerke.workflows
CASCADE;

-- =============================================================================
-- 1. Showcase Workflows
-- =============================================================================
INSERT INTO agentwerke.workflows (
  "Id", "Name", "Description", "Version", "Status", "Owner",
  "CreatedAt", "LastEditedAt", "ValidationState", "Tags", "BpmnXml"
)
VALUES
(
  'wf-vmodel-pilot',
  'V-Model Autonomous SDLC Pipeline',
  'End-to-end V-Model verification workflow with requirements decomposition, architecture ADR generation, sandboxed implementation, automated JUnit verification, and full bilateral traceability gates.',
  '2.4.0',
  'published',
  'platform-architecture@isartor.ai',
  NOW() - INTERVAL '14 days',
  NOW() - INTERVAL '25 minutes',
  'valid',
  '["v-model", "sdlc", "traceability", "opensandbox", "production"]'::jsonb,
  $bpmn$<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                  xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  xmlns:agentwerke="https://agentwerke.de/bpmn/extensions/v1"
                  id="Definitions_VModelShowcase"
                  targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="Process_VModelShowcase" name="V-Model Autonomous SDLC Pipeline" isExecutable="true">
    <bpmn:startEvent id="StartEvent_ChangeRequest" name="Change Request Created">
      <bpmn:outgoing>Flow_Start_To_Req</bpmn:outgoing>
    </bpmn:startEvent>

    <bpmn:serviceTask id="RequirementsSpecification" name="Requirements Specification">
      <bpmn:extensionElements>
        <agentwerke:agentTask
          agent="planner"
          action="vmodel.requirements.analyze"
          environment="github"
          purposeType="requirements_analysis"
          policyTag="vmodel-requirements"
          executionMode="local"
          permissionLevel="read-only"
          allowedTools="github.read_issue,artifact.read">
          <agentwerke:metadata key="phase" value="requirements" />
          <agentwerke:metadata key="traceability.produces" value="requirements_baseline" />
          <agentwerke:prompt><![CDATA[Analyze CHG-204 and extract atomic functional & security requirements (REQ-204-1..4) with verifiable acceptance criteria.]]></agentwerke:prompt>
        </agentwerke:agentTask>
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_Start_To_Req</bpmn:incoming>
      <bpmn:outgoing>Flow_Req_To_Arch</bpmn:outgoing>
    </bpmn:serviceTask>

    <bpmn:serviceTask id="SystemArchitecture" name="System Architecture">
      <bpmn:extensionElements>
        <agentwerke:agentTask
          agent="architect"
          action="vmodel.architecture.design"
          environment="github"
          purposeType="system_architecture"
          policyTag="vmodel-architecture"
          executionMode="local"
          permissionLevel="read-only"
          allowedTools="artifact.read"
          requiresEvidence="requirements_baseline">
          <agentwerke:metadata key="phase" value="system_architecture" />
          <agentwerke:metadata key="traceability.consumes" value="requirements_baseline" />
          <agentwerke:metadata key="traceability.produces" value="architecture_baseline" />
          <agentwerke:prompt><![CDATA[Design the OAuth2 PKCE + Token Rotation architecture (ADR-004) satisfying REQ-204-1..4.]]></agentwerke:prompt>
        </agentwerke:agentTask>
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_Req_To_Arch</bpmn:incoming>
      <bpmn:outgoing>Flow_Arch_To_Comp</bpmn:outgoing>
    </bpmn:serviceTask>

    <bpmn:serviceTask id="ComponentDesign" name="Component Design">
      <bpmn:extensionElements>
        <agentwerke:agentTask
          agent="architect"
          action="vmodel.design.specify"
          environment="github"
          purposeType="component_design"
          policyTag="vmodel-component-design"
          executionMode="local"
          permissionLevel="read-only"
          allowedTools="artifact.read"
          requiresEvidence="architecture_baseline">
          <agentwerke:metadata key="phase" value="component_design" />
          <agentwerke:metadata key="traceability.consumes" value="architecture_baseline" />
          <agentwerke:metadata key="traceability.produces" value="component_design_baseline" />
          <agentwerke:prompt><![CDATA[Decompose ADR-004 into component design obligations CD-01..CD-04 for TokenRefreshService and PkceCodeVerifier.]]></agentwerke:prompt>
        </agentwerke:agentTask>
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_Arch_To_Comp</bpmn:incoming>
      <bpmn:outgoing>Flow_Comp_To_Impl</bpmn:outgoing>
    </bpmn:serviceTask>

    <bpmn:serviceTask id="ImplementComponents" name="Implementation">
      <bpmn:extensionElements>
        <agentwerke:agentTask
          agent="developer"
          action="vmodel.implementation.build"
          environment="sandbox"
          purposeType="implementation"
          policyTag="vmodel-implementation"
          executionMode="agent_sandboxed"
          sandboxProfile="repo-write"
          permissionLevel="read-write"
          allowedTools="sandbox.git,sandbox.file_read,sandbox.file_edit,sandbox.shell"
          requiresEvidence="requirements_baseline,architecture_baseline,component_design_baseline">
          <agentwerke:metadata key="phase" value="implementation" />
          <agentwerke:metadata key="traceability.consumes" value="component_design_baseline" />
          <agentwerke:metadata key="traceability.produces" value="implementation_diff" />
          <agentwerke:prompt><![CDATA[Implement component obligations CD-01..CD-04 inside an isolated OpenSandbox container and verify unit tests compile cleanly.]]></agentwerke:prompt>
        </agentwerke:agentTask>
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_Comp_To_Impl</bpmn:incoming>
      <bpmn:incoming>Flow_UnitTest_Rework</bpmn:incoming>
      <bpmn:outgoing>Flow_Impl_To_Unit</bpmn:outgoing>
    </bpmn:serviceTask>

    <bpmn:serviceTask id="UnitVerification" name="Unit Verification">
      <bpmn:extensionElements>
        <agentwerke:agentTask
          agent="verifier"
          action="verification.ingest_test_results"
          environment="sandbox"
          purposeType="unit_verification"
          policyTag="vmodel-unit-tests"
          executionMode="agent_sandboxed"
          sandboxProfile="repo-read"
          permissionLevel="read-only"
          allowedTools="sandbox.shell,verification.ingest_test_results"
          requiresEvidence="component_design_baseline,implementation_diff">
          <agentwerke:metadata key="phase" value="unit_test" />
          <agentwerke:metadata key="traceability.verifies" value="component_design_baseline" />
          <agentwerke:metadata key="traceability.produces" value="unit_test_results" />
          <agentwerke:prompt><![CDATA[Execute xUnit verification suite against CD-01..CD-04 and ingest JUnit XML results.]]></agentwerke:prompt>
        </agentwerke:agentTask>
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_Impl_To_Unit</bpmn:incoming>
      <bpmn:outgoing>Flow_Unit_To_Gateway</bpmn:outgoing>
    </bpmn:serviceTask>

    <bpmn:exclusiveGateway id="UnitTestOutcomeGateway" name="Unit Tests Passed?" default="Flow_UnitTest_To_Integration">
      <bpmn:incoming>Flow_Unit_To_Gateway</bpmn:incoming>
      <bpmn:outgoing>Flow_UnitTest_To_Integration</bpmn:outgoing>
      <bpmn:outgoing>Flow_UnitTest_Rework</bpmn:outgoing>
    </bpmn:exclusiveGateway>

    <bpmn:serviceTask id="IntegrationVerification" name="Integration Verification">
      <bpmn:extensionElements>
        <agentwerke:agentTask
          agent="verifier"
          action="vmodel.verification.integration"
          environment="sandbox"
          purposeType="integration_verification"
          policyTag="vmodel-integration-tests"
          executionMode="agent_sandboxed"
          sandboxProfile="repo-read"
          permissionLevel="read-only"
          allowedTools="sandbox.shell,verification.ingest_test_results"
          requiresEvidence="architecture_baseline,unit_test_results">
          <agentwerke:metadata key="phase" value="integration_test" />
          <agentwerke:metadata key="traceability.verifies" value="architecture_baseline" />
          <agentwerke:metadata key="traceability.produces" value="integration_test_results" />
          <agentwerke:prompt><![CDATA[Execute integration test suite against OAuth2 PKCE + Redis token store contracts.]]></agentwerke:prompt>
        </agentwerke:agentTask>
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_UnitTest_To_Integration</bpmn:incoming>
      <bpmn:outgoing>Flow_Int_To_Sys</bpmn:outgoing>
    </bpmn:serviceTask>

    <bpmn:serviceTask id="SystemAcceptance" name="System Acceptance &amp; Traceability">
      <bpmn:extensionElements>
        <agentwerke:agentTask
          agent="compliance"
          action="vmodel.verification.system"
          environment="github"
          purposeType="traceability_gate"
          policyTag="vmodel-system-acceptance"
          executionMode="local"
          permissionLevel="read-only"
          allowedTools="artifact.read"
          requiresEvidence="requirements_baseline,architecture_baseline,component_design_baseline,unit_test_results,integration_test_results">
          <agentwerke:metadata key="phase" value="system_acceptance" />
          <agentwerke:metadata key="traceability.verifies" value="requirements_baseline" />
          <agentwerke:metadata key="traceability.produces" value="system_test_results" />
          <agentwerke:prompt><![CDATA[Verify 100% bilateral traceability across REQ-204-1..4, ADR-004, CD-01..CD-04, and ingested CI test runs.]]></agentwerke:prompt>
        </agentwerke:agentTask>
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_Int_To_Sys</bpmn:incoming>
      <bpmn:outgoing>Flow_Sys_To_Approval</bpmn:outgoing>
    </bpmn:serviceTask>

    <bpmn:userTask id="ReleaseApprovalGate" name="Release Sign-Off Gate">
      <bpmn:extensionElements>
        <agentwerke:approvalTask
          purposeType="release_signoff"
          policyTag="vmodel-release-approval"
          requiresEvidence="requirements_baseline,unit_test_results,integration_test_results,system_test_results">
          <agentwerke:metadata key="phase" value="acceptance_signoff" />
          <agentwerke:metadata key="traceability.verifies" value="requirements_baseline,system_test_results" />
          <agentwerke:metadata key="traceability.produces" value="acceptance_signoff" />
        </agentwerke:approvalTask>
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_Sys_To_Approval</bpmn:incoming>
      <bpmn:outgoing>Flow_Approval_To_End</bpmn:outgoing>
    </bpmn:userTask>

    <bpmn:endEvent id="EndEvent_Released" name="Release Approved">
      <bpmn:incoming>Flow_Approval_To_End</bpmn:incoming>
    </bpmn:endEvent>

    <bpmn:sequenceFlow id="Flow_Start_To_Req" sourceRef="StartEvent_ChangeRequest" targetRef="RequirementsSpecification" />
    <bpmn:sequenceFlow id="Flow_Req_To_Arch" sourceRef="RequirementsSpecification" targetRef="SystemArchitecture" />
    <bpmn:sequenceFlow id="Flow_Arch_To_Comp" sourceRef="SystemArchitecture" targetRef="ComponentDesign" />
    <bpmn:sequenceFlow id="Flow_Comp_To_Impl" sourceRef="ComponentDesign" targetRef="ImplementComponents" />
    <bpmn:sequenceFlow id="Flow_Impl_To_Unit" sourceRef="ImplementComponents" targetRef="UnitVerification" />
    <bpmn:sequenceFlow id="Flow_Unit_To_Gateway" sourceRef="UnitVerification" targetRef="UnitTestOutcomeGateway" />
    <bpmn:sequenceFlow id="Flow_UnitTest_To_Integration" name="All Assertions Green" sourceRef="UnitTestOutcomeGateway" targetRef="IntegrationVerification" />
    <bpmn:sequenceFlow id="Flow_UnitTest_Rework" name="Rework Required" sourceRef="UnitTestOutcomeGateway" targetRef="ImplementComponents">
      <bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">{{event.conclusion}} == "failure"</bpmn:conditionExpression>
    </bpmn:sequenceFlow>
    <bpmn:sequenceFlow id="Flow_Int_To_Sys" sourceRef="IntegrationVerification" targetRef="SystemAcceptance" />
    <bpmn:sequenceFlow id="Flow_Sys_To_Approval" sourceRef="SystemAcceptance" targetRef="ReleaseApprovalGate" />
    <bpmn:sequenceFlow id="Flow_Approval_To_End" sourceRef="ReleaseApprovalGate" targetRef="EndEvent_Released" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_VModelShowcase">
    <bpmndi:BPMNPlane id="BPMNPlane_VModelShowcase" bpmnElement="Process_VModelShowcase">
      <bpmndi:BPMNShape id="StartEvent_ChangeRequest_di" bpmnElement="StartEvent_ChangeRequest">
        <dc:Bounds x="110" y="92" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="RequirementsSpecification_di" bpmnElement="RequirementsSpecification">
        <dc:Bounds x="190" y="70" width="140" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="SystemArchitecture_di" bpmnElement="SystemArchitecture">
        <dc:Bounds x="290" y="180" width="140" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="ComponentDesign_di" bpmnElement="ComponentDesign">
        <dc:Bounds x="390" y="290" width="140" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="ImplementComponents_di" bpmnElement="ImplementComponents">
        <dc:Bounds x="530" y="400" width="150" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="UnitVerification_di" bpmnElement="UnitVerification">
        <dc:Bounds x="720" y="290" width="140" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="UnitTestOutcomeGateway_di" bpmnElement="UnitTestOutcomeGateway" isMarkerVisible="true">
        <dc:Bounds x="895" y="305" width="50" height="50" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="IntegrationVerification_di" bpmnElement="IntegrationVerification">
        <dc:Bounds x="830" y="180" width="140" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="SystemAcceptance_di" bpmnElement="SystemAcceptance">
        <dc:Bounds x="940" y="70" width="150" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="ReleaseApprovalGate_di" bpmnElement="ReleaseApprovalGate">
        <dc:Bounds x="1130" y="70" width="140" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="EndEvent_Released_di" bpmnElement="EndEvent_Released">
        <dc:Bounds x="1312" y="92" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow_Start_To_Req_di" bpmnElement="Flow_Start_To_Req">
        <di:waypoint x="146" y="110" />
        <di:waypoint x="190" y="110" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_Req_To_Arch_di" bpmnElement="Flow_Req_To_Arch">
        <di:waypoint x="260" y="150" />
        <di:waypoint x="260" y="220" />
        <di:waypoint x="290" y="220" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_Arch_To_Comp_di" bpmnElement="Flow_Arch_To_Comp">
        <di:waypoint x="360" y="260" />
        <di:waypoint x="360" y="330" />
        <di:waypoint x="390" y="330" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_Comp_To_Impl_di" bpmnElement="Flow_Comp_To_Impl">
        <di:waypoint x="460" y="370" />
        <di:waypoint x="460" y="440" />
        <di:waypoint x="530" y="440" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_Impl_To_Unit_di" bpmnElement="Flow_Impl_To_Unit">
        <di:waypoint x="680" y="440" />
        <di:waypoint x="790" y="440" />
        <di:waypoint x="790" y="370" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_Unit_To_Gateway_di" bpmnElement="Flow_Unit_To_Gateway">
        <di:waypoint x="860" y="330" />
        <di:waypoint x="895" y="330" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_UnitTest_To_Integration_di" bpmnElement="Flow_UnitTest_To_Integration">
        <di:waypoint x="920" y="305" />
        <di:waypoint x="920" y="260" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_UnitTest_Rework_di" bpmnElement="Flow_UnitTest_Rework">
        <di:waypoint x="920" y="355" />
        <di:waypoint x="920" y="465" />
        <di:waypoint x="680" y="465" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_Int_To_Sys_di" bpmnElement="Flow_Int_To_Sys">
        <di:waypoint x="970" y="220" />
        <di:waypoint x="1015" y="220" />
        <di:waypoint x="1015" y="150" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_Sys_To_Approval_di" bpmnElement="Flow_Sys_To_Approval">
        <di:waypoint x="1090" y="110" />
        <di:waypoint x="1130" y="110" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow_Approval_To_End_di" bpmnElement="Flow_Approval_To_End">
        <di:waypoint x="1270" y="110" />
        <di:waypoint x="1312" y="110" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>$bpmn$
),
(
  'wf-autonomous-issue-to-pr',
  'Autonomous Issue-to-PR Delivery',
  'Triages incoming GitHub issues, synthesizes an implementation plan, executes code changes in an ephemeral OpenSandbox container, and opens a verified pull request.',
  '1.8.0',
  'published',
  'devtools@isartor.ai',
  NOW() - INTERVAL '10 days',
  NOW() - INTERVAL '2 hours',
  'valid',
  '["github", "autonomous-coding", "opensandbox", "ci-verified"]'::jsonb,
  $bpmn2$<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions_IssueToPr" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="Process_IssueToPr" name="Autonomous Issue-to-PR Delivery" isExecutable="true">
    <bpmn:startEvent id="Start_Issue" name="Issue Triaged" />
    <bpmn:serviceTask id="AnalyzeIssue" name="Analyze Issue Context" />
    <bpmn:serviceTask id="SandboxPatch" name="Implement Patch in Sandbox" />
    <bpmn:serviceTask id="OpenPullRequest" name="Open Pull Request" />
    <bpmn:endEvent id="End_PR" name="PR Merged" />
    <bpmn:sequenceFlow id="f1" sourceRef="Start_Issue" targetRef="AnalyzeIssue" />
    <bpmn:sequenceFlow id="f2" sourceRef="AnalyzeIssue" targetRef="SandboxPatch" />
    <bpmn:sequenceFlow id="f3" sourceRef="SandboxPatch" targetRef="OpenPullRequest" />
    <bpmn:sequenceFlow id="f4" sourceRef="OpenPullRequest" targetRef="End_PR" />
  </bpmn:process>
</bpmn:definitions>$bpmn2$
),
(
  'wf-secops-cve-remediation',
  'SecOps Critical CVE Auto-Remediation',
  'Detects high-severity dependency vulnerabilities, validates binary compatibility in sandboxed builds, and routes production hotfixes through policy-governed approval gates.',
  '1.3.2',
  'published',
  'secops@isartor.ai',
  NOW() - INTERVAL '7 days',
  NOW() - INTERVAL '4 hours',
  'valid',
  '["security", "cve", "compliance", "zero-trust"]'::jsonb,
  $bpmn3$<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions_SecOps" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="Process_SecOps" name="SecOps Critical CVE Auto-Remediation" isExecutable="true">
    <bpmn:startEvent id="Start_CVE" name="CVE Advisory" />
    <bpmn:serviceTask id="AssessImpact" name="Assess Blast Radius" />
    <bpmn:serviceTask id="PatchDependency" name="Upgrade &amp; Verify Package" />
    <bpmn:userTask id="SecOpsSignoff" name="SecOps Hotfix Approval" />
    <bpmn:endEvent id="End_Patched" name="Hotfix Deployed" />
    <bpmn:sequenceFlow id="s1" sourceRef="Start_CVE" targetRef="AssessImpact" />
    <bpmn:sequenceFlow id="s2" sourceRef="AssessImpact" targetRef="PatchDependency" />
    <bpmn:sequenceFlow id="s3" sourceRef="PatchDependency" targetRef="SecOpsSignoff" />
    <bpmn:sequenceFlow id="s4" sourceRef="SecOpsSignoff" targetRef="End_Patched" />
  </bpmn:process>
</bpmn:definitions>$bpmn3$
);

-- =============================================================================
-- 2. Showcase Workflow Runs (5 Runs Across Key Operational States)
-- =============================================================================
INSERT INTO agentwerke.workflow_runs (
  "Id", "WorkflowId", "WorkflowName", "WorkflowVersion",
  "Status", "RiskLevel", "CurrentStep", "RequestedBy",
  "StartedAt", "CompletedAt", "DurationMs", "PendingApprovals",
  "Tags", "CorrelationId"
)
VALUES
(
  'run-showcase-vmodel-001',
  'wf-vmodel-pilot',
  'V-Model Autonomous SDLC Pipeline',
  '2.4.0',
  'completed',
  'medium',
  'Release Approved',
  'elena.rostova@isartor.ai',
  NOW() - INTERVAL '42 minutes',
  NOW() - INTERVAL '38 minutes',
  248400,
  0,
  '["CHG-204", "oauth2-pkce", "v-model-verified", "production-ready"]'::jsonb,
  'corr-vmodel-chg-204'
),
(
  'run-showcase-approval-002',
  'wf-vmodel-pilot',
  'V-Model Autonomous SDLC Pipeline',
  '2.4.0',
  'waiting_user',
  'high',
  'Release Sign-Off Gate',
  'marcus.vance@isartor.ai',
  NOW() - INTERVAL '18 minutes',
  NULL,
  112600,
  2,
  '["CHG-219", "payment-ledger", "awaiting-signoff", "tier-1"]'::jsonb,
  'corr-vmodel-chg-219'
),
(
  'run-showcase-rework-003',
  'wf-vmodel-pilot',
  'V-Model Autonomous SDLC Pipeline',
  '2.4.0',
  'running',
  'medium',
  'Implementation',
  'github-webhook[bot]',
  NOW() - INTERVAL '9 minutes',
  NULL,
  54200,
  1,
  '["CHG-225", "self-healing-rework", "iteration-2", "opensandbox"]'::jsonb,
  'corr-vmodel-chg-225'
),
(
  'run-showcase-budget-004',
  'wf-secops-cve-remediation',
  'SecOps Critical CVE Auto-Remediation',
  '1.3.2',
  'blocked',
  'critical',
  'Upgrade & Verify Package',
  'secops-scanner@isartor.ai',
  NOW() - INTERVAL '31 minutes',
  NOW() - INTERVAL '27 minutes',
  219000,
  0,
  '["CVE-2026-4108", "policy-blocked", "egress-denied"]'::jsonb,
  'corr-secops-cve-4108'
),
(
  'run-showcase-completed-005',
  'wf-autonomous-issue-to-pr',
  'Autonomous Issue-to-PR Delivery',
  '1.8.0',
  'completed',
  'low',
  'Open Pull Request',
  'dev-experience@isartor.ai',
  NOW() - INTERVAL '65 minutes',
  NOW() - INTERVAL '62 minutes',
  184300,
  0,
  '["GH-198", "pr-merged", "fast-track"]'::jsonb,
  'corr-issue-pr-198'
);

-- =============================================================================
-- 3. Workflow Run Steps for Primary V-Model Run (run-showcase-vmodel-001)
-- =============================================================================
INSERT INTO agentwerke.workflow_run_steps (
  "Id", "RunId", "Name", "Type", "Status",
  "StartedAt", "CompletedAt", "AgentName", "Output", "Error",
  "RuntimeSnapshot",
  "PolicyDecision_Kind", "PolicyDecision_PolicyId", "PolicyDecision_PolicyName",
  "PolicyDecision_Rationale", "PolicyDecision_RiskScore", "PolicyDecision_RiskLevel",
  "PolicyDecision_RiskFactors", "PolicyDecision_PurposeConfidence",
  "PolicyDecision_PurposeRationale", "PolicyDecision_DecidedAt", "PolicyDecision_Constraints"
)
VALUES
(
  'step-v001-1',
  'run-showcase-vmodel-001',
  'Change Request Created',
  'startEvent',
  'completed',
  NOW() - INTERVAL '42 minutes',
  NOW() - INTERVAL '41 minutes 58 seconds',
  'system',
  '{"changeId":"CHG-204","issueId":204,"repository":"isartor-ai/agentwerke","title":"Enforce OAuth2 PKCE S256 & Refresh Token Rotation"}',
  NULL,
  NULL,
  NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL
),
(
  'step-v001-2',
  'run-showcase-vmodel-001',
  'Requirements Specification',
  'serviceTask',
  'completed',
  NOW() - INTERVAL '41 minutes 55 seconds',
  NOW() - INTERVAL '41 minutes 20 seconds',
  'planner',
  '{"artifact":"requirements-spec.md","obligations":["REQ-204-1: Enforce PKCE S256 code challenge","REQ-204-2: Rotate refresh tokens on every exchange","REQ-204-3: Revoke token family on replay detection","REQ-204-4: Emit structured audit record on token anomaly"],"coverageTarget":"100%"}',
  NULL,
  $snap1${
    "runId": "run-showcase-vmodel-001",
    "stepId": "step-v001-2",
    "nodeId": "RequirementsSpecification",
    "agentName": "planner",
    "action": "vmodel.requirements.analyze",
    "executionMode": "agent_assisted",
    "prompt": {
      "finalPrompt": "Decompose GitHub Issue #204 (CHG-204) into testable V-Model functional and security requirements.",
      "renderedAt": "2026-10-23T09:00:05.000Z",
      "sections": [
        {"name": "System", "content": "You are the V-Model Requirements Planner agent.", "source": "agent_profile"},
        {"name": "Task", "content": "Extract atomic requirements REQ-204-1..N with acceptance criteria.", "source": "bpmn"}
      ],
      "variables": {"change_id": "CHG-204", "issue_number": "204"},
      "sourceFiles": ["AGENTS.md", "docs/security/oauth-baseline.md"],
      "missingVariables": []
    },
    "contract": {
      "permissions": {"level": "read-only", "allowedTools": ["github.read_issue", "workspace.read_file"], "deniedTools": ["sandbox.shell"]},
      "tools": [{"name": "github.read_issue", "category": "read", "required": true}],
      "mcpServers": [{"name": "github-mcp", "transport": "stdio", "enabled": true}]
    },
    "skills": [
      {"skillId": "vmodel-requirements-elicitation", "name": "V-Model Requirements Elicitation", "version": "2.1.0", "fingerprint": "sha256:4a8b91c", "source": "workspace", "available": true, "selected": true, "invoked": true}
    ],
    "toolInvocations": [
      {"toolName": "github.read_issue", "category": "read", "status": "completed", "policyDecisionId": "pol-gh-read", "policyDecisionKind": "allow", "inputSummary": "isartor-ai/agentwerke#204", "outputSummary": "Fetched issue #204 with 4 security acceptance criteria", "artifactNames": ["requirements-spec.md"], "durationMs": 310}
    ],
    "hookExecutions": [
      {"hookName": "pii-redactor", "event": "post_tool_use", "type": "compliance", "decision": "proceed", "blocking": false, "outputSummary": "Verified zero customer PII in requirement specification", "durationMs": 14}
    ],
    "artifacts": [
      {"name": "requirements-spec.md", "uri": "/api/runs/run-showcase-vmodel-001/artifacts/requirements-spec.md", "contentType": "text/markdown"}
    ],
    "permissionDecision": {"level": "read-only", "allowed": true, "rationale": "Planner agent restricted to read-only repository & issue inspection"},
    "tokenUsage": {"inputTokens": 6420, "outputTokens": 1480, "modelId": "claude-3-7-sonnet-20250219", "elapsedMs": 3420},
    "modelTraces": [
      {
        "status": "completed",
        "modelId": "claude-3-7-sonnet-20250219",
        "startedAt": "2026-10-23T09:00:06.000Z",
        "completedAt": "2026-10-23T09:00:09.420Z",
        "elapsedMs": 3420,
        "inputTokens": 6420,
        "outputTokens": 1480,
        "reasoningSummary": "Mapped RFC 7636 (PKCE) and RFC 6819 token replay countermeasures into 4 deterministic requirements with bilateral verification hooks.",
        "output": "Generated requirements-spec.md with 4 traceable obligations (REQ-204-1 through REQ-204-4).",
        "toolCalls": [{"id": "tc-req-1", "name": "github.read_issue", "inputSummary": "issue #204"}]
      }
    ]
  }$snap1$::jsonb,
  'allow', 'pol-vmodel-planning', 'V-Model Read-Only Discovery Policy',
  'Read-only GitHub issue inspection and requirement synthesis approved automatically.',
  12, 'low', '["read_only_context", "verified_agent_profile"]'::jsonb,
  98, 'Agent prompt and tool invocation strictly match requirements specification stage.',
  NOW() - INTERVAL '41 minutes 55 seconds', '[]'::jsonb
),
(
  'step-v001-3',
  'run-showcase-vmodel-001',
  'System Architecture',
  'serviceTask',
  'completed',
  NOW() - INTERVAL '41 minutes 18 seconds',
  NOW() - INTERVAL '40 minutes 40 seconds',
  'architect',
  '{"artifact":"architecture-adr-004.md","decisions":["ADR-004-A: Family-chained refresh token lineage in PostgreSQL","ADR-004-B: Constant-time SHA-256 PKCE verifier comparison"],"tracesTo":["REQ-204-1","REQ-204-2","REQ-204-3","REQ-204-4"]}',
  NULL,
  NULL,
  'allow', 'pol-vmodel-architecture', 'Architecture Boundary Governance Policy',
  'Architecture specification modifies auth domain model without altering external network egress.',
  24, 'low', '["auth_domain_scope", "no_infra_mutation"]'::jsonb,
  96, 'Architectural ADR directly traces to REQ-204-1..4.',
  NOW() - INTERVAL '41 minutes 18 seconds', '[]'::jsonb
),
(
  'step-v001-4',
  'run-showcase-vmodel-001',
  'Component Design',
  'serviceTask',
  'completed',
  NOW() - INTERVAL '40 minutes 38 seconds',
  NOW() - INTERVAL '40 minutes 02 seconds',
  'architect',
  '{"artifact":"component-design.json","components":["TokenRefreshService","PkceChallengeValidator","TokenFamilyStore"],"obligations":["CD-01","CD-02","CD-03","CD-04"]}',
  NULL,
  NULL,
  'allow', 'pol-vmodel-design', 'Component Contract Policy',
  'Component interfaces satisfy clean architecture dependency inversion rules.',
  18, 'low', '["clean_architecture_verified"]'::jsonb,
  97, 'Component design specifies exact unit test contracts for TokenRefreshService.',
  NOW() - INTERVAL '40 minutes 38 seconds', '[]'::jsonb
),
(
  'step-v001-5',
  'run-showcase-vmodel-001',
  'Implementation',
  'serviceTask',
  'completed',
  NOW() - INTERVAL '40 minutes',
  NOW() - INTERVAL '39 minutes 10 seconds',
  'developer',
  '{"artifact":"implementation-diff.patch","branch":"feat/chg-204-token-rotation","filesChanged":4,"insertions":148,"deletions":19,"commitSha":"8f4e2a9c1d"}',
  NULL,
  $snap5${
    "runId": "run-showcase-vmodel-001",
    "stepId": "step-v001-5",
    "nodeId": "ImplementComponents",
    "agentName": "developer",
    "action": "vmodel.implementation.build",
    "executionMode": "agent_sandboxed",
    "prompt": {
      "finalPrompt": "Implement ComponentDesign obligations CD-01..CD-04 for CHG-204 inside the isolated OpenSandbox container and run unit verification.",
      "renderedAt": "2026-10-23T09:02:00.000Z",
      "sections": [
        {"name": "System", "content": "You are the Sandboxed Senior .NET Developer agent operating inside an ephemeral OpenSandbox container.", "source": "agent_profile"},
        {"name": "Task", "content": "Implement PKCE S256 verification and refresh token family rotation in src/Auth/TokenRefreshService.cs.", "source": "bpmn"},
        {"name": "Constraints", "content": "Zero network egress. All unit tests in TokenRefreshServiceTests must pass before artifact export.", "source": "policy"}
      ],
      "variables": {"change_id": "CHG-204", "target_branch": "feat/chg-204-token-rotation", "sandbox_profile": "dotnet-8-isolated"},
      "sourceFiles": ["AGENTS.md", "src/Auth/TokenRefreshService.cs", "tests/Auth.Tests/TokenRefreshServiceTests.cs"],
      "missingVariables": []
    },
    "contract": {
      "permissions": {
        "level": "sandbox-write",
        "allowedTools": ["sandbox.file_read", "sandbox.file_edit", "sandbox.shell", "sandbox.git_diff"],
        "deniedTools": ["network.http_request", "github.merge_pull_request", "host.filesystem"]
      },
      "tools": [
        {"name": "sandbox.file_read", "category": "read", "required": true},
        {"name": "sandbox.file_edit", "category": "write", "required": true},
        {"name": "sandbox.shell", "category": "shell", "required": true}
      ],
      "mcpServers": [
        {"name": "opensandbox-runtime", "transport": "stdio", "enabled": true},
        {"name": "github-mcp", "transport": "stdio", "enabled": true}
      ]
    },
    "skills": [
      {"skillId": "csharp-clean-architecture", "name": "C# Clean Architecture & xUnit", "version": "1.4.0", "fingerprint": "sha256:9f86d08", "source": "workspace", "available": true, "selected": true, "invoked": true},
      {"skillId": "oauth2-security-hardening", "name": "OAuth 2.1 & PKCE Hardening", "version": "2.0.1", "fingerprint": "sha256:3c71e2a", "source": "registry", "available": true, "selected": true, "invoked": true}
    ],
    "toolInvocations": [
      {
        "toolName": "sandbox.file_read",
        "category": "read",
        "status": "completed",
        "policyDecisionId": "pol-sbx-read-01",
        "policyDecisionKind": "allow",
        "inputSummary": "src/Auth/TokenRefreshService.cs (lines 1-160)",
        "outputSummary": "Read existing token exchange implementation and repository interface",
        "artifactNames": [],
        "durationMs": 85
      },
      {
        "toolName": "sandbox.file_edit",
        "category": "write",
        "status": "completed",
        "policyDecisionId": "pol-sbx-write-02",
        "policyDecisionKind": "allow_with_constraints",
        "inputSummary": "src/Auth/TokenRefreshService.cs (+112 -19), tests/Auth.Tests/TokenRefreshServiceTests.cs (+36 -0)",
        "outputSummary": "Added SHA-256 PKCE verifier check, token family lineage tracking, and replay revocation handler",
        "artifactNames": ["implementation-diff.patch"],
        "durationMs": 420
      },
      {
        "toolName": "sandbox.shell",
        "category": "shell",
        "status": "completed",
        "policyDecisionId": "pol-sbx-shell-03",
        "policyDecisionKind": "allow_with_constraints",
        "inputSummary": "dotnet test tests/Auth.Tests/Auth.Tests.csproj --logger \"junit;LogFileName=junit-results.xml\"",
        "outputSummary": "Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 3.84s",
        "artifactNames": ["junit-results.xml"],
        "durationMs": 3840
      }
    ],
    "hookExecutions": [
      {
        "hookName": "secret-and-key-scanner",
        "event": "pre_tool_use",
        "type": "guardrail",
        "decision": "proceed",
        "blocking": true,
        "outputSummary": "Scanned 148 added lines: 0 hardcoded secrets or private keys found",
        "durationMs": 38
      },
      {
        "hookName": "sandbox-egress-firewall",
        "event": "runtime_audit",
        "type": "security",
        "decision": "proceed",
        "blocking": true,
        "outputSummary": "Network namespace netns-sbx-7f9a2c verified 0 outbound packets",
        "durationMs": 19
      }
    ],
    "artifacts": [
      {"name": "implementation-diff.patch", "uri": "/api/runs/run-showcase-vmodel-001/artifacts/implementation-diff.patch", "contentType": "text/x-diff"},
      {"name": "junit-results.xml", "uri": "/api/runs/run-showcase-vmodel-001/artifacts/junit-results.xml", "contentType": "application/xml"}
    ],
    "permissionDecision": {
      "level": "sandbox-write",
      "allowed": true,
      "rationale": "Write and shell execution confined to ephemeral OpenSandbox container sbx-vmodel-001-impl-7f9a2c with --network=none"
    },
    "tokenUsage": {
      "inputTokens": 18420,
      "outputTokens": 3290,
      "modelId": "claude-3-7-sonnet-20250219",
      "elapsedMs": 6840
    },
    "modelTraces": [
      {
        "status": "completed",
        "modelId": "claude-3-7-sonnet-20250219",
        "startedAt": "2026-10-23T09:02:02.000Z",
        "completedAt": "2026-10-23T09:02:08.840Z",
        "elapsedMs": 6840,
        "inputTokens": 18420,
        "outputTokens": 3290,
        "reasoningSummary": "Evaluated ComponentDesign obligations CD-01..CD-04. Patched TokenRefreshService.ExchangeAsync to enforce CryptographicOperations.FixedTimeEquals on the SHA-256 PKCE code_verifier, rotated refresh token family IDs atomically, and revoked entire token families when a consumed token is re-presented.",
        "output": "Completed implementation on branch feat/chg-204-token-rotation and verified all 4 xUnit security test suites inside OpenSandbox.",
        "toolCalls": [
          {"id": "tc-impl-01", "name": "sandbox.file_read", "inputSummary": "src/Auth/TokenRefreshService.cs"},
          {"id": "tc-impl-02", "name": "sandbox.file_edit", "inputSummary": "src/Auth/TokenRefreshService.cs (+112 -19)"},
          {"id": "tc-impl-03", "name": "sandbox.shell", "inputSummary": "dotnet test --logger junit"}
        ]
      }
    ],
    "sandboxExecution": {
      "provider": "docker",
      "sandboxId": "sbx-vmodel-001-impl-7f9a2c",
      "commandState": "completed",
      "exitCode": 0,
      "durationMs": 5420,
      "logs": [
        {"stream": "stdout", "message": "[opensandbox] container sbx-vmodel-001-impl-7f9a2c started (image: ghcr.io/isartor-ai/dotnet-sandbox:8.0, network: none)", "timestamp": "2026-10-23T09:02:01.100Z"},
        {"stream": "stdout", "message": "[workspace] Mounted repo snapshot @ commit 4d91a0e (read-write overlayfs)", "timestamp": "2026-10-23T09:02:01.420Z"},
        {"stream": "stdout", "message": "[patch] Applied diff to src/Auth/TokenRefreshService.cs and tests/Auth.Tests/TokenRefreshServiceTests.cs", "timestamp": "2026-10-23T09:02:03.890Z"},
        {"stream": "stdout", "message": "[dotnet test] Starting test execution, please wait... (4 test cases matched)", "timestamp": "2026-10-23T09:02:04.510Z"},
        {"stream": "stdout", "message": "[dotnet test] Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 3.84 s - Auth.Tests.dll (net8.0)", "timestamp": "2026-10-23T09:02:08.350Z"},
        {"stream": "stdout", "message": "[opensandbox] Exported artifacts implementation-diff.patch (6.4 KB) & junit-results.xml (3.1 KB); container destroyed.", "timestamp": "2026-10-23T09:02:08.920Z"}
      ],
      "diagnostics": {
        "cpuQuota": "2.0 vCPU",
        "image": "ghcr.io/isartor-ai/dotnet-sandbox:8.0",
        "memoryLimit": "2048 MB",
        "networkPolicy": "isolated (--network=none)"
      }
    }
  }$snap5$::jsonb,
  'allow_with_constraints', 'pol-sandbox-code-exec', 'OpenSandbox Isolated Execution Policy',
  'Code modification and test execution permitted inside air-gapped OpenSandbox container with secret scanning enabled.',
  42, 'medium', '["source_code_mutation", "shell_execution_in_sandbox", "auth_module_touched"]'::jsonb,
  95, 'Tool calls match declared V-Model Implementation action and target only Auth module files.',
  NOW() - INTERVAL '40 minutes', '["require_airgapped_sandbox", "enforce_pre_commit_secret_scan", "cap_wall_clock_60s"]'::jsonb
),
(
  'step-v001-6',
  'run-showcase-vmodel-001',
  'Unit Verification',
  'serviceTask',
  'completed',
  NOW() - INTERVAL '39 minutes 08 seconds',
  NOW() - INTERVAL '38 minutes 45 seconds',
  'verifier',
  $junit_out${
    "provider": "verification",
    "action": "ingest_test_results",
    "format": "junit",
    "status": "passed",
    "total": 4,
    "passed": 4,
    "failed": 0,
    "errors": 0,
    "skipped": 0,
    "duration_ms": 3840,
    "incomplete": false,
    "declared_total": 4,
    "ci_run_url": "https://github.com/isartor-ai/agentwerke/actions/runs/114829104",
    "report_url": "https://github.com/isartor-ai/agentwerke/actions/runs/114829104/artifacts/1",
    "report_artifact": "junit-results.xml",
    "cases": [
      {"name": "ExchangeAsync_Enforces_PKCE_S256_CodeVerifier", "classname": "Agentwerke.Auth.Tests.TokenRefreshServiceTests", "status": "passed", "duration_ms": 920},
      {"name": "ExchangeAsync_Rotates_RefreshToken_Lineage_Atomically", "classname": "Agentwerke.Auth.Tests.TokenRefreshServiceTests", "status": "passed", "duration_ms": 1140},
      {"name": "ExchangeAsync_Revokes_Entire_TokenFamily_On_Replay", "classname": "Agentwerke.Auth.Tests.TokenRefreshServiceTests", "status": "passed", "duration_ms": 1060},
      {"name": "ExchangeAsync_Emits_AuditRecord_On_Replay_Anomaly", "classname": "Agentwerke.Auth.Tests.TokenRefreshServiceTests", "status": "passed", "duration_ms": 720}
    ]
  }$junit_out$,
  NULL,
  NULL,
  'allow', 'pol-vmodel-verification', 'Automated Verification Gate Policy',
  'All 4 JUnit assertions passed with zero skipped or incomplete test cases.',
  10, 'low', '["deterministic_junit_report"]'::jsonb,
  99, 'JUnit XML report verified against ComponentDesign obligations CD-01..CD-04.',
  NOW() - INTERVAL '39 minutes 08 seconds', '[]'::jsonb
),
(
  'step-v001-7',
  'run-showcase-vmodel-001',
  'Integration Verification',
  'serviceTask',
  'completed',
  NOW() - INTERVAL '38 minutes 42 seconds',
  NOW() - INTERVAL '38 minutes 22 seconds',
  'verifier',
  '{"status":"passed","suite":"OAuth2IntegrationSuite","containers":["postgres:16-alpine","agentwerke-api"],"assertionsPassed":18,"durationMs":19400}',
  NULL,
  NULL,
  'allow', 'pol-vmodel-verification', 'Automated Verification Gate Policy',
  'Integration test suite verified SystemArchitecture ADR-004-A and ADR-004-B.',
  15, 'low', '["ephemeral_test_db"]'::jsonb,
  98, 'Integration verification confirms PostgreSQL token family revocation behaviour.',
  NOW() - INTERVAL '38 minutes 42 seconds', '[]'::jsonb
),
(
  'step-v001-8',
  'run-showcase-vmodel-001',
  'System Acceptance & Traceability',
  'serviceTask',
  'completed',
  NOW() - INTERVAL '38 minutes 20 seconds',
  NOW() - INTERVAL '38 minutes 08 seconds',
  'compliance',
  '{"artifact":"vmodel-traceability-matrix.json","coveragePercent":100,"verifiedObligations":7,"unverifiedObligations":0,"missingEvidence":0}',
  NULL,
  NULL,
  'allow', 'pol-vmodel-traceability', 'Bilateral V-Model Traceability Gate',
  '100% bilateral traceability verified across Requirements, Architecture, Component Design, and Test Evidence.',
  10, 'low', '["full_evidence_chain"]'::jsonb,
  100, 'Every V-Model design obligation has a verified downstream test artifact.',
  NOW() - INTERVAL '38 minutes 20 seconds', '[]'::jsonb
),
(
  'step-v001-9',
  'run-showcase-vmodel-001',
  'Release Sign-Off Gate',
  'userTask',
  'completed',
  NOW() - INTERVAL '38 minutes 06 seconds',
  NOW() - INTERVAL '38 minutes',
  'release-manager',
  '{"approvedBy":"elena.rostova@isartor.ai","decision":"approved","checklistArtifact":"release-readiness-checklist.md","comment":"Full V-Model traceability matrix green (4/4 JUnit suites, 18/18 integration assertions). Approved for production rollout."}',
  NULL,
  NULL,
  'allow', 'pol-prod-release-gate', 'Production Release Human Sign-Off Policy',
  'Human release manager sign-off recorded with verified evidence bundle.',
  35, 'medium', '["production_release_gate", "human_signoff_recorded"]'::jsonb,
  100, 'Explicit human approval recorded by authorized Release Manager.',
  NOW() - INTERVAL '38 minutes', '["require_human_approver"]'::jsonb
);

-- =============================================================================
-- 4. Workflow Run Steps for Secondary Runs (002 Awaiting Approval, 003 Rework, 004 Blocked)
-- =============================================================================
INSERT INTO agentwerke.workflow_run_steps (
  "Id", "RunId", "Name", "Type", "Status",
  "StartedAt", "CompletedAt", "AgentName", "Output", "Error",
  "RuntimeSnapshot",
  "PolicyDecision_Kind", "PolicyDecision_PolicyId", "PolicyDecision_PolicyName",
  "PolicyDecision_Rationale", "PolicyDecision_RiskScore", "PolicyDecision_RiskLevel",
  "PolicyDecision_RiskFactors", "PolicyDecision_PurposeConfidence",
  "PolicyDecision_PurposeRationale", "PolicyDecision_DecidedAt", "PolicyDecision_Constraints"
)
VALUES
(
  'step-v002-1',
  'run-showcase-approval-002',
  'Requirements Specification',
  'serviceTask',
  'completed',
  NOW() - INTERVAL '17 minutes',
  NOW() - INTERVAL '15 minutes',
  'planner',
  '{"artifact":"requirements-spec.md","changeId":"CHG-219","obligations":["REQ-219-1: Idempotent ledger settlement keys"]}',
  NULL, NULL,
  'allow', 'pol-vmodel-planning', 'V-Model Read-Only Discovery Policy',
  'Requirements analysis approved.', 14, 'low', '["read_only"]'::jsonb, 97, 'Matches planner role.', NOW() - INTERVAL '17 minutes', '[]'::jsonb
),
(
  'step-v002-2',
  'run-showcase-approval-002',
  'Implementation',
  'serviceTask',
  'completed',
  NOW() - INTERVAL '14 minutes',
  NOW() - INTERVAL '10 minutes',
  'developer',
  '{"artifact":"implementation-diff.patch","filesChanged":6,"insertions":210,"deletions":34}',
  NULL, NULL,
  'allow_with_constraints', 'pol-sandbox-code-exec', 'OpenSandbox Isolated Execution Policy',
  'Executed in isolated sandbox.', 48, 'medium', '["payment_ledger_touched"]'::jsonb, 94, 'Matches implementation step.', NOW() - INTERVAL '14 minutes', '["require_airgapped_sandbox"]'::jsonb
),
(
  'step-v002-3',
  'run-showcase-approval-002',
  'Release Sign-Off Gate',
  'userTask',
  'waiting',
  NOW() - INTERVAL '8 minutes',
  NULL,
  'release-manager',
  NULL,
  NULL, NULL,
  'require_approval', 'pol-prod-release-gate', 'Production Release Human Sign-Off Policy',
  'High-risk settlement ledger modification requires human release manager approval before production deployment.',
  78, 'high', '["tier_1_payment_service", "schema_migration_included", "production_target"]'::jsonb,
  96, 'Release gate triggered mandatory HITL review.', NOW() - INTERVAL '8 minutes', '["require_dual_control_or_release_manager"]'::jsonb
),
-- Run 003 (Self-Healing Rework Loop)
(
  'step-v003-1',
  'run-showcase-rework-003',
  'Requirements Specification',
  'serviceTask',
  'completed',
  NOW() - INTERVAL '9 minutes',
  NOW() - INTERVAL '7 minutes',
  'planner',
  '{"changeId":"CHG-225","summary":"Webhook signature timestamp tolerance window"}',
  NULL, NULL,
  'allow', 'pol-vmodel-planning', 'V-Model Read-Only Discovery Policy',
  'Approved.', 12, 'low', '[]'::jsonb, 98, 'Planner step.', NOW() - INTERVAL '9 minutes', '[]'::jsonb
),
(
  'step-v003-2',
  'run-showcase-rework-003',
  'Implementation',
  'serviceTask',
  'running',
  NOW() - INTERVAL '4 minutes',
  NULL,
  'developer',
  '{"iteration":2,"reworkReason":"Fixing clock-skew boundary assertion in WebhookSignatureValidatorTests"}',
  NULL, NULL,
  'allow_with_constraints', 'pol-sandbox-code-exec', 'OpenSandbox Isolated Execution Policy',
  'Rework iteration #2 executing inside isolated OpenSandbox container.',
  45, 'medium', '["automated_rework_loop"]'::jsonb, 93, 'Self-healing rework loop triggered by UnitTestOutcomeGateway.', NOW() - INTERVAL '4 minutes', '["max_rework_iterations_3"]'::jsonb
),
-- Run 004 (Blocked by Policy)
(
  'step-v004-1',
  'run-showcase-budget-004',
  'Upgrade & Verify Package',
  'serviceTask',
  'failed',
  NOW() - INTERVAL '30 minutes',
  NOW() - INTERVAL '27 minutes',
  'developer',
  NULL,
  'PolicyViolationException: Tool call network.http_request to external registry mirror was denied by policy pol-zero-trust-egress (Risk Score: 92 / Critical).',
  NULL,
  'deny', 'pol-zero-trust-egress', 'Zero-Trust Sandbox Egress Policy',
  'Blocked outbound HTTP connection attempt from sandbox container to unverified external host.',
  92, 'critical', '["unauthorized_network_egress", "unverified_package_mirror"]'::jsonb,
  41, 'Agent attempted external network fetch outside pre-cached internal artifact mirror.',
  NOW() - INTERVAL '27 minutes', '["deny_all_external_egress"]'::jsonb
);

-- =============================================================================
-- 5. Workflow Events (Traceability Links, Gateway Decisions, Visible Reasoning, Rework)
-- =============================================================================
INSERT INTO agentwerke.workflow_events (
  "Id", "RunId", "Type", "Message", "CreatedAt"
)
VALUES
-- External action event linking GitHub Issue #204 so TraceabilityRowBuilder builds Verified Traceability rows!
(
  'evt-v001-gh-issue',
  'run-showcase-vmodel-001',
  'external_action_recorded',
  '{"provider":"github","action":"read_issue","status":"succeeded","resourceId":"204","resourceUrl":"https://github.com/isartor-ai/agentwerke/issues/204"}',
  NOW() - INTERVAL '41 minutes 50 seconds'
),
-- Visible reasoning events for step-v001-5 (Implementation)
(
  'evt-v001-reason-1',
  'run-showcase-vmodel-001',
  'agent_reasoning_started',
  '{"stepId":"step-v001-5","summary":"Analyzing ComponentDesign obligations CD-01..CD-04 and inspecting TokenRefreshService.cs"}',
  NOW() - INTERVAL '39 minutes 55 seconds'
),
(
  'evt-v001-tool-1',
  'run-showcase-vmodel-001',
  'agent_tool_call_finished',
  '{"stepId":"step-v001-5","toolName":"sandbox.file_edit","status":"completed","summary":"Patched src/Auth/TokenRefreshService.cs with PKCE S256 verifier & token family replay revocation","detail":"+112 -19 lines across 2 files"}',
  NOW() - INTERVAL '39 minutes 35 seconds'
),
(
  'evt-v001-sbx-1',
  'run-showcase-vmodel-001',
  'agent_sandbox_log',
  '{"stepId":"step-v001-5","summary":"[opensandbox] dotnet test Auth.Tests.csproj -> Passed: 4, Failed: 0, Skipped: 0 (3.84s)","detail":"Container sbx-vmodel-001-impl-7f9a2c exit code 0"}',
  NOW() - INTERVAL '39 minutes 15 seconds'
),
(
  'evt-v001-reason-2',
  'run-showcase-vmodel-001',
  'agent_reasoning_recorded',
  '{"stepId":"step-v001-5","summary":"All 4 unit verification suites passed in isolated container. Exporting implementation-diff.patch and junit-results.xml."}',
  NOW() - INTERVAL '39 minutes 11 seconds'
),
-- Gateway evaluation event on run-showcase-vmodel-001
(
  'evt-v001-gw-1',
  'run-showcase-vmodel-001',
  'gateway_evaluated',
  '{"gatewayId":"UnitTestOutcomeGateway","chosenFlowId":"Flow_UnitTest_To_Integration","chosenTargetId":"IntegrationVerification","usedDefaultFlow":true,"conditions":[{"flowId":"Flow_UnitTest_Rework","targetRef":"ImplementComponents","expression":"{{event.conclusion}} == \"failure\"","result":false,"detail":"\"passed\" == \"failure\" (0 failed tests)"}]}',
  NOW() - INTERVAL '38 minutes 44 seconds'
),
-- Rework & retry events on run-showcase-rework-003 (also exercises RunDiffModal retry counts)
(
  'evt-v003-retry-1',
  'run-showcase-rework-003',
  'step_retrying',
  'Step Unit Verification reported 1 assertion failure (WebhookSignature_ClockSkew_Boundary); routing back to Implementation (Rework Iteration 2/3).',
  NOW() - INTERVAL '4 minutes 30 seconds'
),
(
  'evt-v003-gw-rework',
  'run-showcase-rework-003',
  'gateway_evaluated',
  '{"gatewayId":"UnitTestOutcomeGateway","chosenFlowId":"Flow_UnitTest_Rework","chosenTargetId":"ImplementComponents","usedDefaultFlow":false,"conditions":[{"flowId":"Flow_UnitTest_Rework","targetRef":"ImplementComponents","expression":"{{event.conclusion}} == \"failure\"","result":true,"detail":"\"failure\" == \"failure\" (1 failed test case)"}]}',
  NOW() - INTERVAL '4 minutes 15 seconds'
),
-- Retry event on run-showcase-vmodel-001 so RunDiffModal shows 1 retry recovery as well
(
  'evt-v001-retry-transient',
  'run-showcase-vmodel-001',
  'step_retrying',
  'Step Integration Verification retried transient container readiness probe (attempt 2/3 succeeded in 410ms).',
  NOW() - INTERVAL '38 minutes 35 seconds'
);

-- =============================================================================
-- 6. Human-in-the-Loop Approval Requests (Pending & Decided)
-- =============================================================================
INSERT INTO agentwerke.approval_requests (
  "Id", "RunId", "WorkflowName", "ActionRequested",
  "Requester", "AgentName", "PolicyRationale", "ArtifactName",
  "RiskScore", "RiskLevel", "RiskFactors", "AffectedSystems",
  "SlaDeadline", "CreatedAt", "Status", "Priority",
  "DecisionComment", "DecidedAt", "DecidedBy"
)
VALUES
(
  'apr-showcase-001',
  'run-showcase-approval-002',
  'V-Model Autonomous SDLC Pipeline',
  'Approve Production Release Cut (CHG-219: Idempotent Payment Ledger Settlement)',
  'marcus.vance@isartor.ai',
  'release-manager',
  'Policy pol-prod-release-gate requires human Release Manager sign-off for Tier-1 payment ledger changes with database migration scripts.',
  'release-readiness-checklist.md',
  78,
  'high',
  '["tier_1_payment_service", "database_migration_v24", "production_deployment"]'::jsonb,
  '["payment-ledger-api", "postgres-settlement-cluster", "stripe-webhook-gateway"]'::jsonb,
  NOW() + INTERVAL '35 minutes',
  NOW() - INTERVAL '8 minutes',
  'pending',
  'critical',
  NULL,
  NULL,
  NULL
),
(
  'apr-showcase-002',
  'run-showcase-approval-002',
  'V-Model Autonomous SDLC Pipeline',
  'Execute Online Schema Migration (0024_add_settlement_idempotency_key.sql)',
  'marcus.vance@isartor.ai',
  'developer',
  'DDL migration on high-volume settlement table requires DBA or Platform Lead verification.',
  'architecture-adr-004.md',
  68,
  'high',
  '["ddl_alter_table", "zero_downtime_index_concurrently"]'::jsonb,
  '["postgres-settlement-cluster"]'::jsonb,
  NOW() + INTERVAL '50 minutes',
  NOW() - INTERVAL '11 minutes',
  'pending',
  'high',
  NULL,
  NULL,
  NULL
),
(
  'apr-showcase-003',
  'run-showcase-vmodel-001',
  'V-Model Autonomous SDLC Pipeline',
  'Approve Production Release Sign-Off (CHG-204: OAuth2 PKCE & Token Rotation)',
  'elena.rostova@isartor.ai',
  'release-manager',
  'All V-Model verification gates passed (100% traceability coverage). Release sign-off required to promote artifact bundle.',
  'release-readiness-checklist.md',
  35,
  'medium',
  '["auth_token_service", "production_release_gate"]'::jsonb,
  '["identity-provider-api", "oauth-token-store"]'::jsonb,
  NOW() - INTERVAL '10 minutes',
  NOW() - INTERVAL '39 minutes',
  'approved',
  'high',
  'Verified 100% V-Model traceability matrix and zero-egress OpenSandbox execution logs. Approved for production.',
  NOW() - INTERVAL '38 minutes',
  'elena.rostova@isartor.ai'
);

-- =============================================================================
-- 7. Agent Interactions (Structured Choice & Just-in-Time Tool Access Escalation)
-- =============================================================================
INSERT INTO agentwerke.agent_interactions (
  "Id", "RunId", "StepId", "FromAgent", "Kind",
  "AddresseeType", "Addressee", "Blocking", "Prompt",
  "ToolName", "Intent", "Options", "RequestedChannels",
  "CorrelationId", "Status", "Response", "RespondedBy",
  "RespondedAt", "RespondedChannel", "TimeoutAt", "ExpiresAction",
  "DefaultAnswer", "CancelledAt", "CancelledBy", "ResumedAt",
  "DelegationDepth", "Version", "CreatedAt"
)
VALUES
(
  'int-showcase-tool-001',
  'run-showcase-approval-002',
  'step-v002-2',
  'developer',
  'tool_access',
  'role',
  'platform-admin',
  true,
  'Requesting one-time execution of `github.create_release_tag` (`v2.4.0-rc1`) on repository `isartor-ai/payment-ledger` after V-Model verification suite passed.',
  'github.create_release_tag',
  'Tag verified release candidate commit 9c41e2b for staging smoke test deployment',
  '[]'::jsonb,
  '["web", "slack"]'::jsonb,
  'corr-vmodel-chg-219',
  'pending',
  NULL, NULL, NULL, NULL,
  NOW() + INTERVAL '25 minutes',
  'fail',
  NULL, NULL, NULL, NULL,
  1, 1,
  NOW() - INTERVAL '7 minutes'
),
(
  'int-showcase-choice-002',
  'run-showcase-rework-003',
  'step-v003-2',
  'architect',
  'choice',
  'human',
  'elena.rostova@isartor.ai',
  true,
  'Webhook signature verification detected a 45-second clock drift in staging replay fixtures. Which tolerance policy should we codify in `WebhookSignatureValidator`?',
  NULL,
  'Select timestamp tolerance window for HMAC-SHA256 webhook replay protection',
  '["Strict 60-second tolerance window + NTP drift alert (Recommended)", "Standard 300-second RFC-compliant window with nonce cache", "Reject all skewed timestamps and trigger dead-letter queue replay"]'::jsonb,
  '["web"]'::jsonb,
  'corr-vmodel-chg-225',
  'pending',
  NULL, NULL, NULL, NULL,
  NOW() + INTERVAL '40 minutes',
  'default_answer',
  'Strict 60-second tolerance window + NTP drift alert (Recommended)',
  NULL, NULL, NULL,
  0, 1,
  NOW() - INTERVAL '3 minutes'
),
(
  'int-showcase-answered-003',
  'run-showcase-vmodel-001',
  'step-v001-3',
  'architect',
  'choice',
  'human',
  'elena.rostova@isartor.ai',
  false,
  'Should refresh token replay detection revoke only the compromised branch or the entire token family root?',
  NULL,
  'Confirm OAuth 2.1 token replay revocation blast radius',
  '["Revoke entire token family root immediately + emit high-severity audit event", "Revoke descendant tokens only"]'::jsonb,
  '["web"]'::jsonb,
  'corr-vmodel-chg-204',
  'answered',
  'Revoke entire token family root immediately + emit high-severity audit event',
  'elena.rostova@isartor.ai',
  NOW() - INTERVAL '40 minutes 50 seconds',
  'web',
  NOW() + INTERVAL '1 hour',
  'fail',
  NULL, NULL, NULL,
  NOW() - INTERVAL '40 minutes 49 seconds',
  0, 2,
  NOW() - INTERVAL '41 minutes 10 seconds'
);

-- =============================================================================
-- 8. Immutable Governance & Compliance Audit Trail
-- =============================================================================
INSERT INTO agentwerke.audit_records (
  "Id", "RunId", "CorrelationId", "ActorType", "Actor",
  "Action", "ResourceType", "ResourceId", "Outcome", "Details", "Timestamp"
)
VALUES
(
  'aud-showcase-001',
  'run-showcase-vmodel-001',
  'corr-vmodel-chg-204',
  'user',
  'elena.rostova@isartor.ai',
  'workflow.run.started',
  'workflow_run',
  'run-showcase-vmodel-001',
  'succeeded',
  'Triggered V-Model Autonomous SDLC Pipeline v2.4.0 for change request CHG-204 (GitHub Issue #204).',
  NOW() - INTERVAL '42 minutes'
),
(
  'aud-showcase-002',
  'run-showcase-vmodel-001',
  'corr-vmodel-chg-204',
  'agent',
  'developer',
  'sandbox.container.executed',
  'opensandbox',
  'sbx-vmodel-001-impl-7f9a2c',
  'allowed',
  'Executed isolated OpenSandbox container (image: ghcr.io/isartor-ai/dotnet-sandbox:8.0, network: none, exitCode: 0, duration: 5420ms).',
  NOW() - INTERVAL '39 minutes 10 seconds'
),
(
  'aud-showcase-003',
  'run-showcase-vmodel-001',
  'corr-vmodel-chg-204',
  'system',
  'policy-engine',
  'policy.gateway.evaluated',
  'policy',
  'pol-vmodel-traceability',
  'allowed',
  'Verified 100% bilateral V-Model traceability coverage (7/7 obligations verified, 4/4 JUnit test cases green).',
  NOW() - INTERVAL '38 minutes 15 seconds'
),
(
  'aud-showcase-004',
  'run-showcase-vmodel-001',
  'corr-vmodel-chg-204',
  'user',
  'elena.rostova@isartor.ai',
  'approval.request.approved',
  'approval_request',
  'apr-showcase-003',
  'approved',
  'Approved production release sign-off gate for CHG-204 after inspecting release-readiness-checklist.md and JUnit verification report.',
  NOW() - INTERVAL '38 minutes'
),
(
  'aud-showcase-005',
  'run-showcase-budget-004',
  'corr-secops-cve-4108',
  'system',
  'policy-engine',
  'policy.tool_call.denied',
  'policy',
  'pol-zero-trust-egress',
  'denied',
  'Blocked outbound network.http_request from sandbox container to unverified external host (Risk Score: 92 / Critical).',
  NOW() - INTERVAL '27 minutes'
),
(
  'aud-showcase-006',
  'run-showcase-approval-002',
  'corr-vmodel-chg-219',
  'agent',
  'developer',
  'interaction.tool_access.requested',
  'agent_interaction',
  'int-showcase-tool-001',
  'pending_approval',
  'Escalated one-time tool access request for github.create_release_tag (v2.4.0-rc1) to role platform-admin.',
  NOW() - INTERVAL '7 minutes'
);

COMMIT;
