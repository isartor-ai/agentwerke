import type {
  ApprovalRequest,
  PolicyDecision,
  WorkflowPublishResult,
  WorkflowValidationResult,
  Workflow,
  WorkflowRun,
} from '../types';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL as string | undefined;
const WORKFLOW_API_BASE_URL = API_BASE_URL ?? '';

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

const now = Date.now();

const policyEscalate: PolicyDecision = {
  kind: 'escalate',
  policyId: 'pol-prod-merge',
  policyName: 'Production Merge Protection',
  rationale: 'Merging to main in production scope requires human approval.',
  riskScore: 72,
  riskLevel: 'high',
  riskFactors: ['production/api', 'schema-change'],
  decidedAt: new Date(now - 6 * 60_000).toISOString(),
  constraints: ['Require approval from release-manager role'],
};

const mockRuns: WorkflowRun[] = [
  {
    id: 'run-0421',
    workflowId: 'wf-001',
    workflowName: 'GitHub PR Review',
    workflowVersion: 'v2.3.1',
    status: 'awaiting_approval',
    riskLevel: 'high',
    currentStep: 'Merge to main',
    requestedBy: 'alice@example.com',
    startedAt: new Date(now - 8 * 60_000).toISOString(),
    durationMs: 8 * 60_000,
    pendingApprovals: 1,
    tags: ['production', 'api'],
    steps: [
      {
        id: 'step-1',
        name: 'Trigger',
        type: 'start_event',
        status: 'completed',
        startedAt: new Date(now - 8 * 60_000).toISOString(),
        completedAt: new Date(now - 7.8 * 60_000).toISOString(),
      },
      {
        id: 'step-2',
        name: 'Clone Repository',
        type: 'service_task',
        status: 'completed',
        agentName: 'GitAgent',
      },
      {
        id: 'step-3',
        name: 'Run Security Scan',
        type: 'service_task',
        status: 'completed',
        output: 'Retry policy configured: maxRetries=2, backoff=5s',
      },
      {
        id: 'step-4',
        name: 'Code Review',
        type: 'service_task',
        status: 'completed',
        agentName: 'ReviewAgent',
      },
      {
        id: 'step-5',
        name: 'Merge to main',
        type: 'user_task',
        status: 'awaiting_approval',
        policyDecision: policyEscalate,
      },
    ],
    events: [
      {
        id: 'evt-1',
        type: 'node_entered',
        message: 'Entered start event.',
        createdAt: new Date(now - 8 * 60_000).toISOString(),
      },
      {
        id: 'evt-2',
        type: 'retry_scheduled',
        message: 'Security scan failed on attempt 1. Retry scheduled in 5s.',
        createdAt: new Date(now - 7 * 60_000).toISOString(),
      },
      {
        id: 'evt-3',
        type: 'timeout_triggered',
        message: 'Static analysis exceeded timeout threshold; boundary timer triggered.',
        createdAt: new Date(now - 6 * 60_000).toISOString(),
      },
      {
        id: 'evt-4',
        type: 'boundary_event_triggered',
        message: 'Boundary event redirected flow to fallback scan path.',
        createdAt: new Date(now - 5 * 60_000).toISOString(),
      },
      {
        id: 'evt-5',
        type: 'user_task_waiting',
        message: 'Waiting for human approval before merge.',
        createdAt: new Date(now - 3 * 60_000).toISOString(),
      },
    ],
  },
  {
    id: 'run-0420',
    workflowId: 'wf-002',
    workflowName: 'Dependency Vulnerability Patch',
    workflowVersion: 'v1.0.4',
    status: 'running',
    riskLevel: 'medium',
    currentStep: 'Patch dependencies',
    requestedBy: 'bot@autofac',
    startedAt: new Date(now - 22 * 60_000).toISOString(),
    durationMs: 22 * 60_000,
    pendingApprovals: 0,
    tags: ['security'],
    events: [
      {
        id: 'evt-6',
        type: 'parallel_forked',
        message: 'Parallel gateway forked to patch + verify branches.',
        createdAt: new Date(now - 20 * 60_000).toISOString(),
      },
      {
        id: 'evt-7',
        type: 'parallel_joined',
        message: 'Parallel branches rejoined at consolidation gateway.',
        createdAt: new Date(now - 8 * 60_000).toISOString(),
      },
    ],
  },
  {
    id: 'run-0419',
    workflowId: 'wf-003',
    workflowName: 'Incident Response Runbook',
    workflowVersion: 'v3.1.0',
    status: 'failed',
    riskLevel: 'critical',
    currentStep: 'Apply Hotfix',
    requestedBy: 'pagerduty@integrations',
    startedAt: new Date(now - 3 * 3_600_000).toISOString(),
    completedAt: new Date(now - 2.3 * 3_600_000).toISOString(),
    durationMs: 40 * 60_000,
    pendingApprovals: 0,
    tags: ['critical', 'incident'],
    events: [
      {
        id: 'evt-8',
        type: 'timeout_triggered',
        message: 'Hotfix deployment exceeded timeout and failed boundary recovery.',
        createdAt: new Date(now - 2.7 * 3_600_000).toISOString(),
      },
    ],
  },
  {
    id: 'run-0418',
    workflowId: 'wf-004',
    workflowName: 'Infra Sandbox Provisioning',
    workflowVersion: 'v1.2.0',
    status: 'completed',
    riskLevel: 'low',
    requestedBy: 'platform-eng@example.com',
    startedAt: new Date(now - 4 * 3_600_000).toISOString(),
    completedAt: new Date(now - 3.6 * 3_600_000).toISOString(),
    durationMs: 24 * 60_000,
    pendingApprovals: 0,
    tags: ['sandbox'],
    events: [
      {
        id: 'evt-9',
        type: 'run_completed',
        message: 'Workflow completed successfully.',
        createdAt: new Date(now - 3.6 * 3_600_000).toISOString(),
      },
    ],
  },
  {
    id: 'run-0417',
    workflowId: 'wf-002',
    workflowName: 'Dependency Vulnerability Patch',
    workflowVersion: 'v1.0.4',
    status: 'blocked',
    riskLevel: 'high',
    currentStep: 'Publish package',
    requestedBy: 'secops@example.com',
    startedAt: new Date(now - 6 * 3_600_000).toISOString(),
    durationMs: 120 * 60_000,
    pendingApprovals: 0,
    tags: ['compliance'],
    events: [
      {
        id: 'evt-10',
        type: 'retry_scheduled',
        message: 'Publish step retry exhausted due registry lock contention.',
        createdAt: new Date(now - 5 * 3_600_000).toISOString(),
      },
    ],
  },
];

const mockApprovals: ApprovalRequest[] = [
  {
    id: 'apr-1001',
    runId: 'run-0415',
    workflowName: 'Incident Response Runbook',
    actionRequested: 'Restart service cluster: payment-processor in us-east-1a',
    requester: 'pagerduty@integrations',
    agentName: 'RemediationAgent',
    policyRationale:
      'Restarting production payment services requires dual approval per SOC2 policy.',
    riskScore: 91,
    riskLevel: 'critical',
    riskFactors: ['critical service', 'payment impact', 'production zone'],
    affectedSystems: ['payment-processor/cluster', 'load-balancer/us-east-1a'],
    slaDeadline: new Date(now + 45 * 60_000).toISOString(),
    createdAt: new Date(now - 30 * 60_000).toISOString(),
    status: 'pending',
    priority: 'urgent',
  },
  {
    id: 'apr-1002',
    runId: 'run-0421',
    workflowName: 'GitHub PR Review',
    actionRequested: 'Merge branch feature/auth-refactor to main',
    requester: 'alice@example.com',
    agentName: 'GitAgent',
    policyRationale:
      'Policy pol-prod-merge requires approval due to production scope and schema change.',
    riskScore: 72,
    riskLevel: 'high',
    riskFactors: ['production/api', 'schema-change'],
    affectedSystems: ['production/api', 'postgres/autofac'],
    slaDeadline: new Date(now + 172 * 60_000).toISOString(),
    createdAt: new Date(now - 8 * 60_000).toISOString(),
    status: 'pending',
    priority: 'high',
  },
  {
    id: 'apr-1003',
    runId: 'run-0408',
    workflowName: 'DB Schema Migration Audit',
    actionRequested: 'Apply migration 20260610_initial_persistence to production',
    requester: 'db-admin@example.com',
    agentName: 'DbMigrationAgent',
    policyRationale:
      'Database production migrations require explicit rollback plan review.',
    riskScore: 65,
    riskLevel: 'medium',
    riskFactors: ['database', 'production'],
    affectedSystems: ['postgres/autofac'],
    slaDeadline: new Date(now + 5 * 3_600_000).toISOString(),
    createdAt: new Date(now - 3_600_000).toISOString(),
    status: 'pending',
    priority: 'normal',
  },
];

function isRemoteEnabled(): boolean {
  return Boolean(API_BASE_URL);
}

async function requestJson<T>(path: string, init?: RequestInit, baseUrl: string = API_BASE_URL ?? ''): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
    ...init,
  });

  if (!response.ok) {
    const errorText = await response.text();
    const errorMessage = extractErrorMessage(errorText) ?? `Request failed for ${path}: ${response.status}`;
    throw new Error(errorMessage);
  }

  return (await response.json()) as T;
}

function extractErrorMessage(errorText: string): string | null {
  if (!errorText.trim()) {
    return null;
  }

  try {
    const parsed = JSON.parse(errorText) as { message?: string; title?: string };
    return parsed.message ?? parsed.title ?? errorText;
  } catch {
    return errorText;
  }
}

export const apiClient = {
  async importWorkflowDefinition(payload: {
    fileName: string;
    bpmnXml: string;
  }): Promise<{ workflowId: string; validation: WorkflowValidationResult }> {
    return requestJson<{ workflowId: string; validation: WorkflowValidationResult }>('/api/workflows/import', {
      method: 'POST',
      body: JSON.stringify(payload),
    }, WORKFLOW_API_BASE_URL);
  },

  async getWorkflows(): Promise<Workflow[]> {
    return requestJson<Workflow[]>('/api/workflows', undefined, WORKFLOW_API_BASE_URL);
  },

  async getWorkflow(id: string): Promise<Workflow | undefined> {
    return requestJson<Workflow>(`/api/workflows/${id}`, undefined, WORKFLOW_API_BASE_URL);
  },

  async uploadBpmnWorkflow(file: File): Promise<{ workflowId: string; validation: WorkflowValidationResult }> {
    const xml = await file.text();
    return this.importWorkflowDefinition({ fileName: file.name, bpmnXml: xml });
  },

  async validateBpmnWorkflow(payload: { workflowId?: string; bpmnXml: string }): Promise<WorkflowValidationResult> {
    return requestJson<WorkflowValidationResult>('/api/workflows/validate', {
      method: 'POST',
      body: JSON.stringify(payload),
    }, WORKFLOW_API_BASE_URL);
  },

  async publishWorkflowDefinition(payload: {
    workflowId?: string;
    bpmnXml: string;
    description?: string;
  }): Promise<WorkflowPublishResult> {
    return requestJson<WorkflowPublishResult>(`/api/workflows/${payload.workflowId}/publish`, {
      method: 'POST',
      body: JSON.stringify(payload),
    }, WORKFLOW_API_BASE_URL);
  },

  async getPolicySimulation(workflowId: string): Promise<{
    tasks: {
      nodeId: string;
      riskLevel: 'Low' | 'Medium' | 'High' | 'Critical';
      requiredApprovals: string[];
      requiredEvidence: string[];
    }[];
  }> {
    if (isRemoteEnabled()) {
      return requestJson(`/api/workflows/${workflowId}/policy-simulation`, {
        method: 'POST',
        body: JSON.stringify({}),
      });
    }

    await delay(1800); // Simulate 1.8s policy evaluation
    return {
      tasks: [
        {
          nodeId: 'deploy-task',
          riskLevel: 'Critical',
          requiredApprovals: ['Release Manager', 'SRE Lead'],
          requiredEvidence: ['ci_passed', 'sast_passed', 'artifact_signed'],
        },
        {
          nodeId: 'merge-task',
          riskLevel: 'High',
          requiredApprovals: ['Code Owner'],
          requiredEvidence: ['ci_passed', 'review_approved'],
        },
        {
          nodeId: 'build-task',
          riskLevel: 'Medium',
          requiredApprovals: [],
          requiredEvidence: ['ci_passed'],
        },
      ],
    };
  },

  async getRuns(): Promise<WorkflowRun[]> {
    if (isRemoteEnabled()) {
      return requestJson<WorkflowRun[]>('/api/runs');
    }

    await delay(120);
    return mockRuns;
  },

  async getRun(id: string): Promise<WorkflowRun | undefined> {
    if (isRemoteEnabled()) {
      return requestJson<WorkflowRun>(`/api/runs/${id}`);
    }

    await delay(120);
    return mockRuns.find((run) => run.id === id);
  },

  async getApprovals(): Promise<ApprovalRequest[]> {
    if (isRemoteEnabled()) {
      return requestJson<ApprovalRequest[]>('/api/approvals');
    }

    await delay(120);
    return mockApprovals;
  },

  async getApproval(id: string): Promise<ApprovalRequest | undefined> {
    if (isRemoteEnabled()) {
      return requestJson<ApprovalRequest>(`/api/approvals/${id}`);
    }

    await delay(120);
    return mockApprovals.find((approval) => approval.id === id);
  },

  async decideApproval(
    id: string,
    decision: 'approve' | 'reject' | 'escalate',
    comment?: string,
  ): Promise<void> {
    if (isRemoteEnabled()) {
      await requestJson<void>(`/api/approvals/${id}/decision`, {
        method: 'POST',
        body: JSON.stringify({ decision, comment }),
      });
      return;
    }

    await delay(100);
    const approval = mockApprovals.find((item) => item.id === id);
    if (!approval) {
      throw new Error('Approval request not found.');
    }

    if (decision === 'approve') {
      approval.status = 'approved';
    } else if (decision === 'reject') {
      approval.status = 'rejected';
    } else {
      approval.status = 'escalated';
    }
    approval.decisionComment = comment;
    approval.decidedAt = new Date().toISOString();
    approval.decidedBy = 'alex.engineer@example.com';
  },
};
