export type RunStatus =
  | 'running'
  | 'completed'
  | 'failed'
  | 'pending'
  | 'cancelled'
  | 'blocked'
  | 'awaiting_approval';

export type RiskLevel = 'critical' | 'high' | 'medium' | 'low' | 'none';

export type WorkflowStatus = 'draft' | 'active' | 'archived' | 'deprecated';

export type PolicyDecisionKind =
  | 'allow'
  | 'deny'
  | 'escalate'
  | 'allow_with_constraints';

export interface PolicyDecision {
  kind: PolicyDecisionKind;
  policyId: string;
  policyName: string;
  rationale: string;
  riskScore: number;
  riskLevel: RiskLevel;
  riskFactors: string[];
  decidedAt: string;
  constraints?: string[];
}

export interface Workflow {
  id: string;
  name: string;
  description: string;
  version: string;
  status: WorkflowStatus;
  owner: string;
  lastEditedAt: string;
  createdAt: string;
  validationState: 'valid' | 'invalid' | 'pending';
  tags: string[];
  bpmnXml?: string;
}

export interface RunStep {
  id: string;
  name: string;
  type: string;
  status: RunStatus;
  startedAt?: string;
  completedAt?: string;
  durationMs?: number;
  agentName?: string;
  output?: string;
  error?: string;
  policyDecision?: PolicyDecision;
}

export interface RunEvent {
  id: string;
  type:
    | 'run_started'
    | 'checkpoint_saved'
    | 'node_entered'
    | 'node_completed'
    | 'user_task_completed'
    | 'user_task_waiting'
    | 'gateway_evaluated'
    | 'parallel_forked'
    | 'parallel_branch_entered'
    | 'parallel_joined'
    | 'boundary_event_registered'
    | 'service_task_attempted'
    | 'service_task_failed'
    | 'service_task_retry_exhausted'
    | 'retry_scheduled'
    | 'timer_scheduled'
    | 'timer_fired'
    | 'timeout_triggered'
    | 'boundary_event_triggered'
    | 'run_completed'
    | 'info';
  message: string;
  createdAt: string;
}

export interface WorkflowRun {
  id: string;
  workflowId: string;
  workflowName: string;
  workflowVersion: string;
  status: RunStatus;
  riskLevel: RiskLevel;
  currentStep?: string;
  requestedBy: string;
  startedAt: string;
  completedAt?: string;
  durationMs?: number;
  pendingApprovals: number;
  steps?: RunStep[];
  events?: RunEvent[];
  tags: string[];
}

export interface BpmnValidationError {
  message: string;
  elementId?: string | null;
  elementName?: string | null;
  lineNumber?: number | null;
  linePosition?: number | null;
}

export interface BpmnValidationWarning {
  message: string;
  elementId?: string | null;
  elementName?: string | null;
  lineNumber?: number | null;
  linePosition?: number | null;
}

export interface WorkflowValidationResult {
  isValid: boolean;
  processId?: string;
  processName?: string;
  errors: BpmnValidationError[];
  warnings: BpmnValidationWarning[];
}

export interface WorkflowPublishResult {
  workflowId: string;
  version: string;
  publishedAt: string;
}

export interface ApprovalRequest {
  id: string;
  runId: string;
  workflowName: string;
  actionRequested: string;
  requester: string;
  agentName: string;
  policyRationale: string;
  riskScore: number;
  riskLevel: RiskLevel;
  riskFactors: string[];
  affectedSystems: string[];
  slaDeadline: string;
  createdAt: string;
  status: 'pending' | 'approved' | 'rejected' | 'escalated';
  priority: 'urgent' | 'high' | 'normal';
  decisionComment?: string;
  decidedBy?: string;
  decidedAt?: string;
}

export interface AuthUser {
  id: string;
  name: string;
  email: string;
  role: string;
  avatarInitials: string;
}

export interface AuthState {
  status: 'loading' | 'authenticated' | 'unauthenticated';
  user?: AuthUser;
}
