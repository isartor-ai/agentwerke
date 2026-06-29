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

export interface RunStepSkillUsage {
  skillId: string;
  name?: string;
  selected: boolean;
  fingerprint?: string;
  invoked?: boolean;
  source?: string;
}

export interface RunStepToolInfo {
  name: string;
  category: string;
}

export interface RunStepToolInvocation {
  toolName: string;
  category: string;
  status: string;
  policyDecisionId?: string;
  policyDecisionKind?: string;
  inputSummary?: string;
  outputSummary?: string;
  errorMessage?: string;
  artifactNames: string[];
  durationMs?: number;
}

export interface RunStepPromptSnapshot {
  finalPrompt: string;
  renderedAt: string;
  sections: { name: string; content: string; source: string }[];
  variables: Record<string, string>;
  sourceFiles: string[];
  missingVariables: string[];
}

export interface RunStepHookExecution {
  event: string;
  type: string;
  decision: string;
  durationMs?: number;
}

export interface RunStepPermissionDecision {
  level: string;
  allowed: boolean;
  rationale?: string;
}

export interface RunStepArtifactRef {
  name: string;
  uri?: string;
  contentType?: string;
}

export interface RunStepSandboxLogEntry {
  stream: string;
  message: string;
  timestamp: string;
}

export interface RunStepSandboxExecution {
  provider: string;
  sandboxId?: string;
  commandState: string;
  exitCode?: number;
  durationMs?: number;
  logs: RunStepSandboxLogEntry[];
  diagnostics: Record<string, string>;
}

export interface RunStepTokenUsage {
  inputTokens: number;
  outputTokens: number;
  modelId?: string;
  elapsedMs?: number;
}

export interface RunStepRuntimeSnapshot {
  agentName?: string;
  action?: string;
  executionMode: string;
  promptInline?: string;
  prompt?: RunStepPromptSnapshot;
  skills: RunStepSkillUsage[];
  tools: RunStepToolInfo[];
  toolInvocations: RunStepToolInvocation[];
  mcpServers: string[];
  hooks: RunStepHookExecution[];
  permissionLevel: string;
  allowedTools: string[];
  deniedTools: string[];
  subAgentsEnabled: boolean;
  permissionDecision?: RunStepPermissionDecision;
  stepArtifacts: RunStepArtifactRef[];
  sandboxExecution?: RunStepSandboxExecution;
  tokenUsage?: RunStepTokenUsage;
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
  promptSnapshot?: RunStepPromptSnapshot;
  skills?: RunStepSkillUsage[];
  toolInvocations?: RunStepToolInvocation[];
  hookExecutions?: RunStepHookExecution[];
  runtimeSnapshot?: RunStepRuntimeSnapshot;
}

export interface RunEvent {
  id: string;
  type: string;
  message: string;
  createdAt: string;
}

export interface RunArtifact {
  name: string;
  sizeBytes: number;
  lastModifiedAt: string;
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
  artifacts?: RunArtifact[];
  approvals?: ApprovalRequest[];
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

export interface TemplateSummary {
  id: string;
  name: string;
  description: string;
  trigger: string;
  policyLevel: string;
  tags: string[];
  agentRoles: string[];
  approvalRoles: string[];
}

export interface TemplateDetail extends TemplateSummary {
  requiredInputs: string[];
  evidenceExpectations: string[];
  bpmnXml: string;
}

export interface TemplateFactoryConfiguration {
  name: string;
  description: string;
  owner: string;
  requiredInputs: Record<string, string>;
  agentAssignments: Record<string, string>;
  approvalAssignments: Record<string, string>;
  connectors: Record<string, boolean>;
  policyLevel: string;
  evidence: Record<string, boolean>;
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
  artifactName?: string;
}

export interface AgentSummary {
  agentId: string;
  name: string;
  description: string;
  category: string;
  runner: string;
  model?: string;
  dockerImage?: string;
  network: string;
  tools: string[];
  deniedTools: string[];
  supportedActions: string[];
  skills: AgentSkillBinding[];
  supportedEnvironments: string[];
  supportedPolicyTags: string[];
  secrets: string[];
  source: string;
  fingerprint?: string;
}

export interface AgentSkillBinding {
  skillId: string;
  name: string;
  description: string;
  supportedActions: string[];
  skillManifestId?: string;
}

export interface AgentDetail extends AgentSummary {
  systemPrompt?: string;
  rawMarkdown: string;
  effectiveFilePath: string;
  sourceFilePath?: string;
}

export interface SkillSummary {
  skillId: string;
  name: string;
  description: string;
  version?: string;
  invocationRules: string[];
  requiredFiles: string[];
  optionalTools: string[];
  fingerprint: string;
  filePath: string;
}

export interface SkillDetail extends SkillSummary {
  content: string;
}

export interface RuntimeMode {
  mode: 'Autofac' | 'Camunda';
  camundaEnabled: boolean;
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
