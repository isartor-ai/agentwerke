# Backend Implementation Plan

This document outlines the plan for implementing the backend for the Autofac application.

## 1. Data Models

Based on the frontend code, the following data models are required.

### Workflow

Represents a workflow definition.

```typescript
interface Workflow {
  id: string; // Primary Key
  name: string;
  description: string;
  version: string;
  status: 'active' | 'draft' | 'archived';
  owner: string;
  createdAt: string; // ISO 8601
  lastEditedAt: string; // ISO 8601
  validationState: 'valid' | 'invalid' | 'pending';
  tags: string[];
  bpmnXml: string; // The raw BPMN XML
}
```

### WorkflowRun

Represents an execution of a workflow.

```typescript
interface WorkflowRun {
  id: string; // Primary Key
  workflowId: string; // Foreign Key to Workflow
  workflowName: string;
  workflowVersion: string;
  status: 'running' | 'completed' | 'failed' | 'pending' | 'cancelled' | 'blocked' | 'awaiting_approval';
  riskLevel: 'low' | 'medium' | 'high' | 'critical';
  currentStep: string;
  requestedBy: string;
  startedAt: string; // ISO 8601
  completedAt?: string; // ISO 8601
  durationMs?: number;
  pendingApprovals: number;
  tags: string[];
  steps: WorkflowRunStep[];
  events: WorkflowEvent[];
}
```

### WorkflowRunStep

Represents a step in a workflow run.

```typescript
interface WorkflowRunStep {
  id: string;
  name: string;
  type: string;
  status: 'completed' | 'running' | 'failed' | 'awaiting_approval';
  startedAt?: string; // ISO 8601
  completedAt?: string; // ISO 8601
  agentName?: string;
  output?: string;
  policyDecision?: PolicyDecision;
}
```

### WorkflowEvent

Represents an event that occurred during a workflow run.

```typescript
interface WorkflowEvent {
  id: string;
  type: string;
  message: string;
  createdAt: string; // ISO 8601
}
```

### ApprovalRequest

Represents a request for human approval.

```typescript
interface ApprovalRequest {
  id: string; // Primary Key
  runId: string; // Foreign Key to WorkflowRun
  workflowName: string;
  actionRequested: string;
  requester: string;
  agentName: string;
  policyRationale: string;
  riskScore: number;
  riskLevel: 'low' | 'medium' | 'high' | 'critical';
  riskFactors: string[];
  affectedSystems: string[];
  slaDeadline: string; // ISO 8601
  createdAt: string; // ISO 8601
  status: 'pending' | 'approved' | 'rejected' | 'escalated';
  priority: 'urgent' | 'high' | 'normal';
  decisionComment?: string;
  decidedAt?: string; // ISO 8601
  decidedBy?: string;
}
```

### PolicyDecision

Represents a decision made by a policy.

```typescript
interface PolicyDecision {
  kind: 'escalate' | 'approve' | 'reject';
  policyId: string;
  policyName: string;
  rationale: string;
  riskScore: number;
  riskLevel: 'low' | 'medium' | 'high' | 'critical';
  riskFactors: string[];
  decidedAt: string; // ISO 8601
  constraints: string[];
}
```

## 2. Database Schema

A relational database is recommended. The following tables can be used to store the data models.

*   **Workflows**
    *   `id` (PK, varchar)
    *   `name` (varchar)
    *   `description` (text)
    *   `version` (varchar)
    *   `status` (varchar)
    *   `owner` (varchar)
    *   `created_at` (timestamp)
    *   `last_edited_at` (timestamp)
    *   `validation_state` (varchar)
    *   `tags` (jsonb or text)
    *   `bpmn_xml` (text)

*   **WorkflowRuns**
    *   `id` (PK, varchar)
    *   `workflow_id` (FK to Workflows.id)
    *   `workflow_name` (varchar)
    *   `workflow_version` (varchar)
    *   `status` (varchar)
    *   `risk_level` (varchar)
    *   `current_step` (varchar)
    *   `requested_by` (varchar)
    *   `started_at` (timestamp)
    *   `completed_at` (timestamp, nullable)
    *   `duration_ms` (integer, nullable)
    *   `pending_approvals` (integer)
    *   `tags` (jsonb or text)

*   **WorkflowRunSteps**
    *   `id` (PK, varchar)
    *   `run_id` (FK to WorkflowRuns.id)
    *   `name` (varchar)
    *   `type` (varchar)
    *   `status` (varchar)
    *   `started_at` (timestamp, nullable)
    *   `completed_at` (timestamp, nullable)
    *   `agent_name` (varchar, nullable)
    *   `output` (text, nullable)
    *   `policy_decision` (jsonb, nullable)

*   **WorkflowEvents**
    *   `id` (PK, varchar)
    *   `run_id` (FK to WorkflowRuns.id)
    *   `type` (varchar)
    *   `message` (text)
    *   `created_at` (timestamp)

*   **ApprovalRequests**
    *   `id` (PK, varchar)
    *   `run_id` (FK to WorkflowRuns.id)
    *   `workflow_name` (varchar)
    *   `action_requested` (text)
    *   `requester` (varchar)
    *   `agent_name` (varchar)
    *   `policy_rationale` (text)
    *   `risk_score` (integer)
    *   `risk_level` (varchar)
    *   `risk_factors` (jsonb or text)
    *   `affected_systems` (jsonb or text)
    *   `sla_deadline` (timestamp)
    *   `created_at` (timestamp)
    *   `status` (varchar)
    *   `priority` (varchar)
    *   `decision_comment` (text, nullable)
    *   `decided_at` (timestamp, nullable)
    *   `decided_by` (varchar, nullable)

## 3. API Endpoints

The following API endpoints need to be implemented in the `Autofac.Api` project. The controllers are located in `src/Autofac.Api/Controllers`.

### WorkflowsController

*   **`GET /api/workflows`**: Get all workflows.
    *   **Response**: `Workflow[]`

*   **`GET /api/workflows/{id}`**: Get a single workflow.
    *   **Response**: `Workflow`

*   **`POST /api/workflows/import`**: Upload a BPMN workflow from a file.
    *   **Request Body**: `{ fileName: string; bpmnXml: string }`
    *   **Response**: `{ workflowId: string; validation: WorkflowValidationResult }`
    *   **Logic**:
        1.  Create a new `Workflow` record in the database with a `draft` status.
        2.  Validate the BPMN XML.
        3.  Return the new workflow ID and the validation result.

*   **`POST /api/workflows/validate`**: Validate a BPMN workflow.
    *   **Request Body**: `{ workflowId?: string; bpmnXml: string }`
    *   **Response**: `WorkflowValidationResult`
    *   **Logic**:
        1.  Parse and validate the BPMN XML.
        2.  The validation should check for `autofac:` extension elements.
        3.  Return the validation result.

*   **`POST /api/workflows/{id}/publish`**: Publish a workflow definition.
    *   **Request Body**: `{ bpmnXml: string }`
    *   **Response**: `{ workflowId: string; version: string; publishedAt: string }`
    *   **Logic**:
        1.  Update the `Workflow` record with the new BPMN XML.
        2.  Apply the MVP versioning rule: drafts start at `v1.0.0`, and each successful publish bumps the major version to `vN.0.0`.
        3.  Change the status to `active`.
        4.  Return the workflow ID, the new version, and the publish date.

*   **`POST /api/workflows/{id}/policy-simulation`**: Simulate policy evaluation for a workflow.
    *   **Request Body**: `{}`
    *   **Response**: `{ tasks: { nodeId: string; riskLevel: 'Low' | 'Medium' | 'High' | 'Critical'; requiredApprovals: string[]; requiredEvidence: string[]; }[] }`
    *   **Logic**:
        1.  Parse the BPMN XML of the workflow.
        2.  For each `userTask` and `serviceTask`, evaluate the policies.
        3.  Return the simulated policy decisions for each task.

### RunsController

*   **`GET /api/runs`**: Get all workflow runs.
    *   **Response**: `WorkflowRun[]`

*   **`GET /api/runs/{id}`**: Get a single workflow run.
    *   **Response**: `WorkflowRun`

### ApprovalsController

*   **`GET /api/approvals`**: Get all approval requests.
    *   **Response**: `ApprovalRequest[]`

*   **`GET /api/approvals/{id}`**: Get a single approval request.
    *   **Response**: `ApprovalRequest`

*   **`POST /api/approvals/{id}/decision`**: Make a decision on an approval request.
    *   **Request Body**: `{ decision: 'approve' | 'reject' | 'escalate'; comment?: string }`
    *   **Response**: `200 OK`
    *   **Logic**:
        1.  Update the `ApprovalRequest` record with the decision.
        2.  If the decision is 'approve', the workflow run should proceed.
        3.  If the decision is 'reject', the workflow run should be stopped.
        4.  If the decision is 'escalate', the approval request should be reassigned.
