import type {
  AgentDetail,
  AgentSkillBinding,
  AgentSummary,
  ApprovalRequest,
  AuthConfig,
  AuthRole,
  AuditEntry,
  AuthUser,
  ConnectorStatus,
  DevTokenResponse,
  EvidencePack,
  PolicyRule,
  PolicyRuleSet,
  PolicySimulationReport,
  PolicySimulationRequestInput,
  RunEvent,
  RunInteraction,
  RuntimeMode,
  SettingsSnapshot,
  SettingsTestResponse,
  ToolAccessRequest,
  SettingsUpdateRequest,
  SettingsUpdateResponse,
  SkillSummary,
  TemplateDetail,
  TemplateSummary,
  WorkflowPublishResult,
  WorkflowValidationResult,
  Workflow,
  WorkflowRun,
} from '../types';
import { avatarInitials, normalizeRoles, primaryRole } from '../auth/permissions';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL as string | undefined;
const WORKFLOW_API_BASE_URL = API_BASE_URL ?? '';

// Injected at build time for the manual test stack (docker-compose.manual.yml).
// In production builds this variable is unset and auth is handled externally.
const DEV_ADMIN_TOKEN = import.meta.env.VITE_DEV_ADMIN_TOKEN as string | undefined;
let runtimeAuthToken: string | undefined;

function authHeaders(): Record<string, string> {
  const token = runtimeAuthToken ?? DEV_ADMIN_TOKEN;
  return token ? { Authorization: `Bearer ${token}` } : {};
}

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

interface CurrentUserResponse {
  id: string;
  name: string;
  email?: string | null;
  roles: string[];
}

function toAuthUser(response: CurrentUserResponse): AuthUser {
  const roles = normalizeRoles(response.roles);

  return {
    id: response.id,
    name: response.name,
    email: response.email,
    role: primaryRole(roles),
    roles,
    avatarInitials: avatarInitials(response.name),
  };
}

async function requestJson<T>(
  path: string,
  init?: RequestInit,
  baseUrl: string = API_BASE_URL ?? '',
  includeAuth = true,
): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    headers: { 'Content-Type': 'application/json', ...(includeAuth ? authHeaders() : {}), ...(init?.headers ?? {}) },
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
  setAuthToken(token: string): void {
    runtimeAuthToken = token;
  },

  clearAuthToken(): void {
    runtimeAuthToken = undefined;
  },

  async getAuthConfig(): Promise<AuthConfig> {
    return requestJson<AuthConfig>('/api/auth/config', undefined, API_BASE_URL ?? '', false);
  },

  async getCurrentUser(): Promise<AuthUser> {
    const response = await requestJson<CurrentUserResponse>('/api/auth/me');
    return toAuthUser(response);
  },

  async issueDevToken(payload: {
    role: AuthRole;
    subject?: string;
    expiryHours?: number;
  }): Promise<DevTokenResponse> {
    return requestJson<DevTokenResponse>(
      '/api/auth/token',
      {
        method: 'POST',
        body: JSON.stringify({
          role: payload.role,
          subject: payload.subject,
          expiryHours: payload.expiryHours,
        }),
      },
      API_BASE_URL ?? '',
      false,
    );
  },

  async getRuntimeMode(): Promise<RuntimeMode> {
    return requestJson<RuntimeMode>('/api/health/runtime');
  },

  async getSettings(): Promise<SettingsSnapshot> {
    return requestJson<SettingsSnapshot>('/api/settings');
  },

  async updateSettings(payload: SettingsUpdateRequest): Promise<SettingsUpdateResponse> {
    return requestJson<SettingsUpdateResponse>('/api/settings', {
      method: 'PATCH',
      body: JSON.stringify(payload),
    });
  },

  async testSettingsTarget(target: string): Promise<SettingsTestResponse> {
    return requestJson<SettingsTestResponse>(`/api/settings/tests/${encodeURIComponent(target)}`, {
      method: 'POST',
      body: JSON.stringify({}),
    });
  },

  async getConnectors(): Promise<ConnectorStatus[]> {
    return requestJson<ConnectorStatus[]>('/api/connectors');
  },

  async getAuditEntries(params: { runId?: string; limit?: number } = {}): Promise<AuditEntry[]> {
    const query = new URLSearchParams();
    if (params.runId) query.set('runId', params.runId);
    if (params.limit) query.set('limit', String(params.limit));
    const suffix = query.toString();
    return requestJson<AuditEntry[]>(`/api/audit${suffix ? `?${suffix}` : ''}`);
  },

  // Absolute (or API-base-relative) URL to register on the external side for an inbound webhook.
  webhookUrl(path: string): string {
    return `${API_BASE_URL ?? ''}${path}`;
  },

  getRunArtifactDownloadUrl(runId: string, artifactName: string): string {
    return `${API_BASE_URL ?? ''}/api/runs/${encodeURIComponent(runId)}/artifacts/${encodeURIComponent(artifactName)}`;
  },

  async getRunArtifactContent(runId: string, artifactName: string): Promise<string> {
    const response = await fetch(this.getRunArtifactDownloadUrl(runId, artifactName), {
      headers: authHeaders(),
    });

    if (!response.ok) {
      const errorText = await response.text();
      const errorMessage = extractErrorMessage(errorText) ?? `Artifact fetch failed: ${response.status}`;
      throw new Error(errorMessage);
    }

    return response.text();
  },

  getRunEvidencePackDownloadUrl(runId: string): string {
    return `${API_BASE_URL ?? ''}/api/runs/${encodeURIComponent(runId)}/evidence-pack/download`;
  },

  async getRunEvidencePack(runId: string): Promise<EvidencePack> {
    return requestJson<EvidencePack>(`/api/runs/${encodeURIComponent(runId)}/evidence-pack`);
  },

  async getPolicies(): Promise<PolicyRuleSet> {
    return requestJson<PolicyRuleSet>('/api/policies');
  },

  async publishPolicy(policyId: string): Promise<PolicyRule> {
    return requestJson<PolicyRule>(`/api/policies/${encodeURIComponent(policyId)}/publish`, { method: 'POST' });
  },

  async unpublishPolicy(policyId: string): Promise<PolicyRule> {
    return requestJson<PolicyRule>(`/api/policies/${encodeURIComponent(policyId)}/unpublish`, { method: 'POST' });
  },

  async simulatePolicies(request: PolicySimulationRequestInput): Promise<PolicySimulationReport> {
    return requestJson<PolicySimulationReport>('/api/policies/simulate', {
      method: 'POST',
      body: JSON.stringify(request),
    });
  },

  async downloadRunEvidencePack(runId: string): Promise<void> {
    const response = await fetch(this.getRunEvidencePackDownloadUrl(runId), {
      headers: authHeaders(),
    });

    if (!response.ok) {
      const errorText = await response.text();
      const errorMessage = extractErrorMessage(errorText) ?? `Evidence export failed: ${response.status}`;
      throw new Error(errorMessage);
    }

    const blob = await response.blob();
    const url = window.URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `${runId}-evidence-pack.json`;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    window.URL.revokeObjectURL(url);
  },

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

  async getAgents(): Promise<AgentSummary[]> {
    return requestJson<AgentSummary[]>('/api/agents', undefined, WORKFLOW_API_BASE_URL);
  },

  async getAgent(agentId: string): Promise<AgentDetail | undefined> {
    return requestJson<AgentDetail>(`/api/agents/${encodeURIComponent(agentId)}`, undefined, WORKFLOW_API_BASE_URL);
  },

  async updateAgent(agent: {
    agentId: string;
    name: string;
    description: string;
    category: string;
    runner: string;
    model?: string;
    dockerImage?: string;
    network?: string;
    tools: string[];
    deniedTools: string[];
    supportedActions: string[];
    skills: AgentSkillBinding[];
    supportedEnvironments: string[];
    supportedPolicyTags: string[];
    secrets: string[];
    sandboxProfiles?: string[];
    identityColor?: string;
    avatarStyle?: string;
    avatarSeed?: string;
    identityIconKey?: string;
    identityIcon?: string;
    systemPrompt?: string;
    reasoningEffort?: string;
  }): Promise<AgentDetail> {
    return requestJson<AgentDetail>(`/api/agents/${encodeURIComponent(agent.agentId)}`, {
      method: 'PUT',
      body: JSON.stringify(agent),
    }, WORKFLOW_API_BASE_URL);
  },

  async uploadAgent(file: File): Promise<AgentDetail> {
    return requestJson<AgentDetail>('/api/agents/upload', {
      method: 'POST',
      body: JSON.stringify({
        fileName: file.name,
        content: await file.text(),
      }),
    }, WORKFLOW_API_BASE_URL);
  },

  async getSkills(): Promise<SkillSummary[]> {
    return requestJson<SkillSummary[]>('/api/skills', undefined, WORKFLOW_API_BASE_URL);
  },

  async getTemplates(): Promise<TemplateSummary[]> {
    return requestJson<TemplateSummary[]>('/api/templates', undefined, WORKFLOW_API_BASE_URL);
  },

  async getTemplate(id: string): Promise<TemplateDetail | undefined> {
    return requestJson<TemplateDetail>(`/api/templates/${id}`, undefined, WORKFLOW_API_BASE_URL);
  },

  async cloneTemplate(payload: {
    templateId: string;
    name?: string;
    description?: string;
    owner?: string;
  }): Promise<{ workflowId: string; name: string }> {
    return requestJson<{ workflowId: string; name: string }>(
      `/api/templates/${encodeURIComponent(payload.templateId)}/clone`,
      {
        method: 'POST',
        body: JSON.stringify({
          name: payload.name,
          description: payload.description,
          owner: payload.owner,
        }),
      },
      WORKFLOW_API_BASE_URL,
    );
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
    return requestJson(`/api/workflows/${workflowId}/policy-simulation`, {
      method: 'POST',
      body: JSON.stringify({}),
    }, WORKFLOW_API_BASE_URL);
  },

  async getRuns(): Promise<WorkflowRun[]> {
    return requestJson<WorkflowRun[]>('/api/runs');
  },

  async getRun(id: string): Promise<WorkflowRun | undefined> {
    return requestJson<WorkflowRun>(`/api/runs/${id}`);
  },

  async getRunInteractions(runId: string): Promise<RunInteraction[]> {
    return requestJson<RunInteraction[]>(`/api/runs/${encodeURIComponent(runId)}/interactions`);
  },

  async answerInteraction(runId: string, interactionId: string, answer: string): Promise<void> {
    await requestJson<void>(
      `/api/runs/${encodeURIComponent(runId)}/interactions/${encodeURIComponent(interactionId)}/answer`,
      { method: 'POST', body: JSON.stringify({ answer }) },
    );
  },

  async cancelRun(runId: string): Promise<void> {
    await requestJson<void>(`/api/runs/${encodeURIComponent(runId)}/cancel`, {
      method: 'POST',
      body: JSON.stringify({}),
    });
  },

  async startRun(workflowId: string, inputs?: Record<string, string>): Promise<{ runId: string }> {
    return requestJson<{ runId: string }>('/api/runs', {
      method: 'POST',
      body: JSON.stringify({ workflowId, ...(inputs ? { inputs } : {}) }),
    });
  },

  async getApprovals(): Promise<ApprovalRequest[]> {
    return requestJson<ApprovalRequest[]>('/api/approvals');
  },

  async getToolAccessRequests(): Promise<ToolAccessRequest[]> {
    return requestJson<ToolAccessRequest[]>('/api/approvals/tool-access');
  },

  async getApproval(id: string): Promise<ApprovalRequest | undefined> {
    return requestJson<ApprovalRequest>(`/api/approvals/${id}`);
  },

  async decideApproval(
    id: string,
    decision: 'approve' | 'reject' | 'escalate',
    comment?: string,
  ): Promise<void> {
    await requestJson<void>(`/api/approvals/${id}/decision`, {
      method: 'POST',
      body: JSON.stringify({ decision, comment }),
    });
  },

  streamRunEvents(
    runId: string,
    onEvent: (event: RunEvent) => void,
    onDone: () => void,
    signal?: AbortSignal,
  ): void {
    void (async () => {
      try {
        const response = await fetch(
          `${WORKFLOW_API_BASE_URL}/api/runs/${encodeURIComponent(runId)}/events/stream`,
          { headers: authHeaders(), signal },
        );
        if (!response.ok || !response.body) { onDone(); return; }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        let eventType = '';
        let dataLine = '';
        let streamOpen = true;

        while (streamOpen) {
          const { value, done } = await reader.read();
          if (done) {
            streamOpen = false;
            continue;
          }
          buffer += decoder.decode(value, { stream: true });
          const parts = buffer.split('\n');
          buffer = parts.pop() ?? '';

          for (const line of parts) {
            if (line.startsWith(':')) {
              // SSE comment / heartbeat — ignore
            } else if (line.startsWith('event: ')) {
              eventType = line.slice(7).trim();
            } else if (line.startsWith('data: ')) {
              dataLine = line.slice(6);
            } else if (line === '') {
              if (eventType === 'done') { onDone(); return; }
              if (dataLine) {
                try {
                  onEvent(JSON.parse(dataLine) as RunEvent);
                } catch { /* ignore malformed */ }
              }
              eventType = '';
              dataLine = '';
            }
          }
        }
      } catch {
        // AbortError or network error — stream ended
      }
      onDone();
    })();
  },
};

// kept for tests that need a controlled delay
export { delay };
