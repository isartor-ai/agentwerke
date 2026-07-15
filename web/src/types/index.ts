export type RunStatus =
  | 'running'
  | 'completed'
  | 'failed'
  | 'pending'
  | 'cancelled'
  | 'blocked'
  | 'awaiting_approval'
  | 'waiting_external'
  | 'needs_config';

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

export interface RunStepModelToolCall {
  id: string;
  name: string;
  inputSummary?: string | null;
}

export interface RunStepModelTrace {
  status: string;
  modelId?: string | null;
  startedAt: string;
  completedAt?: string | null;
  elapsedMs?: number | null;
  inputTokens: number;
  outputTokens: number;
  reasoningSummary?: string | null;
  output?: string | null;
  failureReason?: string | null;
  toolCalls: RunStepModelToolCall[];
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
  modelTraces?: RunStepModelTrace[];
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

/** One entry in a run's agent conversation (#192): a coordination post, delegation, or human ask. */
export interface RunInteraction {
  id: string;
  runId: string;
  stepId?: string | null;
  from: string;
  kind: 'post' | 'question' | 'choice' | 'notify' | 'agent_request' | 'approval' | 'tool_access';
  addresseeType: 'human' | 'agent';
  addressee?: string | null;
  blocking: boolean;
  prompt: string;
  options: string[];
  status: 'pending' | 'answered' | 'posted' | 'expired';
  response?: string | null;
  respondedBy?: string | null;
  respondedAt?: string | null;
  createdAt: string;
  /** For tool_access interactions (#202): the tool the agent asked for. */
  toolName?: string | null;
  /** For tool_access interactions (#202): the model's stated intent (truncated tool input). */
  intent?: string | null;
}

/** A pending tool-access escalation (#202) shown on the Approvals dashboard. */
export interface ToolAccessRequest {
  interactionId: string;
  runId: string;
  workflowName?: string | null;
  stepId?: string | null;
  stepName?: string | null;
  agent: string;
  toolName?: string | null;
  intent?: string | null;
  prompt: string;
  options: string[];
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

/**
 * One row of the run's evidence-backed traceability matrix (#210). Distinct from the BPMN-derived
 * matrix, which shows what a workflow declares: every external field here is a real id or URL the
 * run actually recorded, so a reader can click through to the system of record.
 *
 * A row means "the verification build dispatched for this requirement produced this case" — not
 * that the test verifies the requirement. Nothing carries a per-test requirement mapping.
 */
export interface TraceabilityRow {
  requirementProvider: string | null;
  requirementId: string | null;
  requirementUrl: string | null;
  testId: string;
  testName: string;
  ciRunId: string | null;
  ciRunUrl: string | null;
  /** "passed" | "failed" | "error" | "skipped" — the test case's outcome, from the CI report. */
  status: string;
  evidenceArtifact: string | null;
  failureMessage: string | null;
}

export interface RunTraceability {
  runId: string;
  rows: TraceabilityRow[];
}

export interface EvidencePack {
  schemaVersion: string;
  runId: string;
  generatedAt: string;
  workflow: EvidenceWorkflow;
  runtime: EvidenceRuntime;
  run: EvidenceRun;
  agentSnapshots: EvidenceAgentSnapshot[];
  approvals: EvidenceApproval[];
  policyDecisions: EvidencePolicyDecision[];
  toolCalls: EvidenceToolCall[];
  connectorCalls: EvidenceConnectorCall[];
  sandboxExecutions: EvidenceSandboxExecution[];
  modelUsage: EvidenceModelUsage[];
  artifacts: EvidenceArtifact[];
  auditLog: EvidenceAuditEntry[];
  logs: EvidenceLogEntry[];
  runEvents: EvidenceRunEvent[];
  camunda?: EvidenceCamundaMetadata | null;
}

export interface EvidenceWorkflow {
  workflowId: string;
  name: string;
  version: string;
  definitionVersion?: string | null;
  bpmnSha256?: string | null;
  hashAlgorithm: string;
}

export interface EvidenceRuntime {
  mode: string;
  camundaEnabled: boolean;
}

export interface EvidenceRun {
  runId: string;
  status: string;
  riskLevel: string;
  requestedBy: string;
  startedAt: string;
  completedAt?: string | null;
  durationMs?: number | null;
  pendingApprovals: number;
  correlationId?: string | null;
  tags: string[];
}

export interface EvidenceAgentSnapshot {
  stepId: string;
  stepName: string;
  nodeId: string;
  agentName?: string | null;
  action?: string | null;
  snapshot: RunStepRuntimeSnapshot;
}

export interface EvidenceApproval {
  approvalId: string;
  runId: string;
  actionRequested: string;
  requester: string;
  agentName: string;
  status: string;
  riskLevel: string;
  riskScore: number;
  riskFactors: string[];
  affectedSystems: string[];
  policyRationale: string;
  createdAt: string;
  decidedAt?: string | null;
  decidedBy?: string | null;
  decisionComment?: string | null;
}

export interface EvidencePolicyDecision {
  stepId: string;
  stepName: string;
  kind: string;
  policyId?: string | null;
  policyName?: string | null;
  rationale?: string | null;
  riskScore: number;
  riskLevel?: string | null;
  riskFactors: string[];
  decidedAt?: string | null;
  constraints: string[];
}

export interface EvidenceToolCall {
  stepId: string;
  stepName: string;
  agentName?: string | null;
  action?: string | null;
  toolName: string;
  category: string;
  status: string;
  policyDecisionId?: string | null;
  policyDecisionKind?: string | null;
  inputSummary?: string | null;
  outputSummary?: string | null;
  errorMessage?: string | null;
  artifactNames: string[];
  durationMs?: number | null;
}

export interface EvidenceConnectorCall {
  auditId: string;
  connectorId: string;
  operation: string;
  actor: string;
  outcome: string;
  resourceId?: string | null;
  details?: string | null;
  timestamp: string;
  correlationId?: string | null;
}

export interface EvidenceSandboxExecution {
  stepId: string;
  stepName: string;
  agentName?: string | null;
  action?: string | null;
  provider: string;
  sandboxId?: string | null;
  commandState: string;
  exitCode?: number | null;
  durationMs?: number | null;
  logs: RunStepSandboxLogEntry[];
  diagnostics: Record<string, string>;
}

export interface EvidenceModelUsage {
  stepId: string;
  stepName: string;
  agentName?: string | null;
  action?: string | null;
  modelId?: string | null;
  inputTokens: number;
  outputTokens: number;
  elapsedMs?: number | null;
}

export interface EvidenceArtifact {
  source: string;
  stepId?: string | null;
  name: string;
  sizeBytes?: number | null;
  lastModifiedAt?: string | null;
  uri?: string | null;
  contentType?: string | null;
}

export interface EvidenceAuditEntry {
  auditId: string;
  correlationId?: string | null;
  actorType: string;
  actor: string;
  action: string;
  resourceType?: string | null;
  resourceId?: string | null;
  outcome: string;
  details?: string | null;
  timestamp: string;
}

export interface EvidenceLogEntry {
  source: string;
  type: string;
  message: string;
  timestamp: string;
}

export interface EvidenceRunEvent {
  eventId: string;
  type: string;
  message: string;
  createdAt: string;
}

export interface EvidenceCamundaMetadata {
  adapter: string;
  metadata: Record<string, string>;
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
  sandboxProfiles: string[];
  identityColor?: string;
  avatarStyle?: string;
  avatarSeed?: string;
  identityIconKey?: string;
  identityIcon?: string;
}

export interface AgentSkillBinding {
  skillId: string;
  name: string;
  description: string;
  supportedActions: string[];
  skillManifestId?: string;
}

export interface AgentDetail extends AgentSummary {
  reasoningEffort?: string;
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
  mode: 'Agentwerke' | 'Agentwerke' | 'Camunda';
  camundaEnabled: boolean;
}

export type AuthRole = 'Viewer' | 'Operator' | 'Approver' | 'Admin';

export interface AuthConfig {
  authentication: 'oidc' | 'symmetric-jwt' | 'development-identity' | 'unconfigured' | string;
  issuer?: string | null;
  audience?: string | null;
  authority?: string | null;
  devTokensEnabled: boolean;
  devIdentityEnabled: boolean;
  roles: AuthRole[];
}

export interface DevTokenResponse {
  token: string;
  subject: string;
  role: AuthRole;
  expiresAt: string;
}

export interface AuthUser {
  id: string;
  name: string;
  email?: string | null;
  role: AuthRole;
  roles: AuthRole[];
  avatarInitials: string;
}

export interface AuthState {
  status: 'loading' | 'authenticated' | 'unauthenticated';
  user?: AuthUser;
}

// ── Policy lifecycle + simulation (#34/#170) ──────────────────────────────
export interface PolicyRulePredicate {
  field: string;
  match: string;
  values: string[];
}

export interface PolicyRule {
  id: string;
  name: string;
  enabled: boolean;
  priority: number;
  decisionKind: string;
  rationale: string;
  riskScore: number;
  riskLevel: string;
  riskFactors: string[];
  constraints: string[];
  predicates: PolicyRulePredicate[];
}

export interface PolicyRuleSet {
  version: string;
  updatedAt: string;
  rules: PolicyRule[];
}

export interface PolicyDecisionView {
  kind: string;
  policyId: string;
  policyName: string;
  rationale: string;
  riskScore: number;
  riskLevel: string;
  riskFactors: string[];
  decidedAt: string;
  constraints: string[];
}

export interface PolicySimulationOutcome {
  scenarioName: string;
  current: PolicyDecisionView;
  proposed: PolicyDecisionView;
  changed: boolean;
  changes: string[];
}

export interface PolicySimulationReport {
  scenarioCount: number;
  changedCount: number;
  outcomes: PolicySimulationOutcome[];
}

export interface PolicySimulationScenarioInput {
  name?: string;
  agentName?: string;
  action: string;
  environment?: string;
  purposeType?: string;
  policyTag?: string;
  requiresEvidence?: string[];
  attempt?: number;
}

export interface PolicySimulationRequestInput {
  proposedRules?: PolicyRuleSet | null;
  scenarios: PolicySimulationScenarioInput[];
}

export type SettingsFieldValue = string | number | boolean | string[] | Record<string, string[]> | null;

export interface SettingsSecretStatus {
  configured: boolean;
  source: string;
  fingerprint?: string | null;
  canWrite: boolean;
}

export interface SettingsField {
  path: string;
  label: string;
  description: string;
  valueType: 'string' | 'url' | 'boolean' | 'integer' | 'decimal' | 'string-array' | 'string-map' | 'enum' | 'secret' | string;
  value: SettingsFieldValue;
  isSecret: boolean;
  isEditable: boolean;
  requiresRestart: boolean;
  source: string;
  options: string[];
  secret?: SettingsSecretStatus | null;
}

export interface SettingsCategory {
  id: string;
  title: string;
  description: string;
  fields: SettingsField[];
}

export interface SettingsSnapshot {
  generatedAt: string;
  categories: SettingsCategory[];
}

export interface SettingsUpdateRequest {
  values?: Record<string, unknown>;
  secrets?: Record<string, string>;
}

export interface SettingsUpdateResponse {
  snapshot: SettingsSnapshot;
  changedValues: string[];
  rotatedSecrets: string[];
  restartRequired: boolean;
  auditId: string;
}

export interface SettingsTestResponse {
  target: string;
  succeeded: boolean;
  messages: string[];
  testedAt: string;
  auditId: string;
}

// Integrations dashboard (#188)
export interface ConnectorStatus {
  connectorId: string;
  displayName: string;
  enabled: boolean;
  supportedOperations: string[];
}

// Audit / decision-trace explorer (#189)
export interface AuditEntry {
  id: string;
  runId: string;
  timestamp: string;
  actorType: string;
  actor: string;
  action: string;
  resourceType: string | null;
  resourceId: string | null;
  outcome: string;
  details: string | null;
}
