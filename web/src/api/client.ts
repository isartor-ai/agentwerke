import type {
  AgentSummary,
  ApprovalRequest,
  TemplateDetail,
  TemplateSummary,
  WorkflowPublishResult,
  WorkflowValidationResult,
  Workflow,
  WorkflowRun,
} from '../types';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL as string | undefined;
const WORKFLOW_API_BASE_URL = API_BASE_URL ?? '';

// Injected at build time for the manual test stack (docker-compose.manual.yml).
// In production builds this variable is unset and auth is handled externally.
const DEV_ADMIN_TOKEN = import.meta.env.VITE_DEV_ADMIN_TOKEN as string | undefined;

function authHeaders(): Record<string, string> {
  return DEV_ADMIN_TOKEN ? { Authorization: `Bearer ${DEV_ADMIN_TOKEN}` } : {};
}

const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

async function requestJson<T>(path: string, init?: RequestInit, baseUrl: string = API_BASE_URL ?? ''): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    headers: { 'Content-Type': 'application/json', ...authHeaders(), ...(init?.headers ?? {}) },
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
  getRunArtifactDownloadUrl(runId: string, artifactName: string): string {
    return `${API_BASE_URL ?? ''}/api/runs/${encodeURIComponent(runId)}/artifacts/${encodeURIComponent(artifactName)}`;
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

  async cancelRun(runId: string): Promise<void> {
    await requestJson<void>(`/api/runs/${encodeURIComponent(runId)}/cancel`, {
      method: 'POST',
      body: JSON.stringify({}),
    });
  },

  async startRun(workflowId: string): Promise<{ runId: string }> {
    return requestJson<{ runId: string }>('/api/runs', {
      method: 'POST',
      body: JSON.stringify({ workflowId }),
    });
  },

  async getApprovals(): Promise<ApprovalRequest[]> {
    return requestJson<ApprovalRequest[]>('/api/approvals');
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
};

// kept for tests that need a controlled delay
export { delay };
