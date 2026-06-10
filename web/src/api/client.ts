import type {
  ApprovalRequest,
  PolicyDecision,
  Workflow,
  WorkflowRun,
} from '../types';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL as string | undefined;

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

const mockWorkflows: Workflow[] = [
  {
    id: 'wf-001',
    name: 'GitHub PR Review',
    description: 'Automated code review and guarded merge workflow.',
    version: 'v2.3.1',
    status: 'active',
    owner: 'platform-eng',
    createdAt: new Date(now - 30 * 86_400_000).toISOString(),
    lastEditedAt: new Date(now - 24 * 3_600_000).toISOString(),
    validationState: 'valid',
    tags: ['github', 'production'],
  },
  {
    id: 'wf-002',
    name: 'Dependency Vulnerability Patch',
    description: 'Patch vulnerable dependencies and open remediation PRs.',
    version: 'v1.0.4',
    status: 'active',
    owner: 'secops',
    createdAt: new Date(now - 15 * 86_400_000).toISOString(),
    lastEditedAt: new Date(now - 12 * 3_600_000).toISOString(),
    validationState: 'valid',
    tags: ['security'],
  },
  {
    id: 'wf-003',
    name: 'Incident Response Runbook',
    description: 'Production incident triage and remediation workflow.',
    version: 'v3.1.0',
    status: 'active',
    owner: 'sre',
    createdAt: new Date(now - 60 * 86_400_000).toISOString(),
    lastEditedAt: new Date(now - 48 * 3_600_000).toISOString(),
    validationState: 'valid',
    tags: ['incident', 'critical'],
  },
  {
    id: 'wf-004',
    name: 'Infra Sandbox Provisioning',
    description: 'Provision ephemeral sandboxes for validation runs.',
    version: 'v1.2.0',
    status: 'draft',
    owner: 'platform-eng',
    createdAt: new Date(now - 10 * 86_400_000).toISOString(),
    lastEditedAt: new Date(now - 6 * 3_600_000).toISOString(),
    validationState: 'pending',
    tags: ['sandbox'],
  },
  {
    id: 'wf-005',
    name: 'DB Schema Migration Audit',
    description: 'Validate and audit schema migration plans.',
    version: 'v0.9.0',
    status: 'archived',
    owner: 'data-eng',
    createdAt: new Date(now - 120 * 86_400_000).toISOString(),
    lastEditedAt: new Date(now - 96 * 3_600_000).toISOString(),
    validationState: 'invalid',
    tags: ['database', 'compliance'],
  },
];

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

async function requestJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...init,
  });

  if (!response.ok) {
    throw new Error(`Request failed for ${path}: ${response.status}`);
  }

  return (await response.json()) as T;
}

export const apiClient = {
  async getWorkflows(): Promise<Workflow[]> {
    if (isRemoteEnabled()) {
      return requestJson<Workflow[]>('/api/workflows');
    }

    await delay(120);
    return mockWorkflows;
  },

  async getWorkflow(id: string): Promise<Workflow | undefined> {
    if (isRemoteEnabled()) {
      return requestJson<Workflow>(`/api/workflows/${id}`);
    }

    await delay(120);
    return mockWorkflows.find((workflow) => workflow.id === id);
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
