import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { apiClient } from '../api/client';
import type { AuthState, RunEvent } from '../types';
import { RunDetail } from '../views/RunDetail';
import { adminAuthFixture, evidencePackFixture, runsFixture, viewerAuthFixture, workflowsFixture } from './fixtures';

// BpmnViewer needs SVG layout unavailable in jsdom; use the test stub
vi.mock('../components/BpmnViewer');

vi.mock('../api/client', () => ({
  apiClient: {
    getRun: vi.fn(),
    getAgents: vi.fn(),
    getRunInteractions: vi.fn(),
    answerInteraction: vi.fn(),
    getWorkflow: vi.fn(),
    streamRunEvents: vi.fn(),
    getRunEvidencePack: vi.fn(),
    getRunArtifactDownloadUrl: vi.fn(),
    getRunEvidencePackDownloadUrl: vi.fn(),
    downloadRunEvidencePack: vi.fn(),
    decideApproval: vi.fn(),
    cancelRun: vi.fn(),
  },
}));

const WORKFLOW_WITH_BPMN = {
  ...workflowsFixture[0],
  bpmnXml: '<?xml version="1.0"?><bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"><bpmn:process id="P1"><bpmn:serviceTask id="T1" name="Merge to main" /></bpmn:process></bpmn:definitions>',
};

const agentsFixture = [
  {
    agentId: 'spec-writer',
    name: 'Spec Writer',
    description: 'Writes specifications.',
    category: 'analysis',
    runner: 'agent-model',
    network: 'none',
    tools: [],
    deniedTools: [],
    supportedActions: ['spec.generate'],
    skills: [],
    supportedEnvironments: ['local'],
    supportedPolicyTags: ['standard'],
    secrets: [],
    source: 'file',
    sandboxProfiles: [],
    identityColor: '#378ADD',
    identityIcon: '◫',
  },
  {
    agentId: 'GitAgent',
    name: 'GitAgent',
    description: 'Coordinates git actions.',
    category: 'integration',
    runner: 'agent-model',
    network: 'none',
    tools: [],
    deniedTools: [],
    supportedActions: ['review.pull_request'],
    skills: [],
    supportedEnvironments: ['github'],
    supportedPolicyTags: ['repo-change'],
    secrets: [],
    source: 'builtin',
    sandboxProfiles: [],
    identityColor: '#7F77DD',
    identityIcon: '⌘',
  },
];

describe('RunDetail integration', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getRun).mockResolvedValue(runsFixture[0]);
    vi.mocked(apiClient.getAgents).mockResolvedValue(agentsFixture);
    vi.mocked(apiClient.getWorkflow).mockResolvedValue(WORKFLOW_WITH_BPMN);
    vi.mocked(apiClient.getRunEvidencePack).mockResolvedValue(evidencePackFixture);
    vi.mocked(apiClient.getRunArtifactDownloadUrl).mockImplementation(
      (runId, artifactName) => `/api/runs/${runId}/artifacts/${artifactName}`,
    );
    vi.mocked(apiClient.streamRunEvents).mockImplementation(() => undefined);
    vi.mocked(apiClient.getRunEvidencePackDownloadUrl).mockImplementation(
      (runId) => `/api/runs/${runId}/evidence-pack/download`,
    );
    vi.mocked(apiClient.downloadRunEvidencePack).mockResolvedValue(undefined);
    vi.mocked(apiClient.decideApproval).mockResolvedValue(undefined);
    vi.mocked(apiClient.cancelRun).mockResolvedValue(undefined);
    vi.mocked(apiClient.getRunInteractions).mockResolvedValue([]);
    vi.mocked(apiClient.answerInteraction).mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  function renderDetail(id = 'run-0421', auth: AuthState = adminAuthFixture) {
    return render(
      <MemoryRouter initialEntries={[`/runs/${id}`]}>
        <Routes>
          <Route path="/runs/:runId" element={<RunDetail auth={auth} />} />
          <Route path="/runs" element={<div>runs list</div>} />
        </Routes>
      </MemoryRouter>,
    );
  }

  it('renders live run detail with tabs, events, artifacts and approvals', async () => {
    renderDetail();

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Summary' })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Logs' })).not.toBeInTheDocument();
    expect(screen.getByText('Evidence Pack')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Export Evidence Pack' }));
    await waitFor(() => {
      expect(vi.mocked(apiClient.downloadRunEvidencePack)).toHaveBeenCalledWith('run-0421');
    });
    expect(screen.getByText('Runtime Events')).toBeInTheDocument();
    const eventMonitor = screen.getByText('Runtime Events').closest('details') as HTMLDetailsElement | null;
    expect(eventMonitor).not.toBeNull();
    expect(eventMonitor?.open).toBe(false);
    expect(screen.getByText('Retry scheduled after transient failure.')).toBeInTheDocument();
    expect(screen.getByText('Timeout boundary triggered on security scan.')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Runtime Events').closest('summary')!);
    expect(eventMonitor?.open).toBe(true);

    fireEvent.click(screen.getByRole('tab', { name: 'Policy' }));
    expect(screen.getAllByText('Requires human approval.').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole('tab', { name: 'Artifacts' }));
    expect(screen.getByText('scan-report.json')).toBeInTheDocument();
    expect(
      screen.getAllByRole('link', { name: 'Download' }).some(
        (link) => link.getAttribute('href') === '/api/runs/run-0421/artifacts/scan-report.json',
      ),
    ).toBe(true);

    fireEvent.click(screen.getByRole('tab', { name: 'Approvals' }));
    expect(screen.getByText('Merge branch feature/auth-refactor to main')).toBeInTheDocument();

    fireEvent.click(
      screen.getByRole('button', { name: 'Cancel run and stop further execution' }),
    );
    expect(screen.getByRole('dialog', { name: 'Cancel this run?' })).toBeInTheDocument();
  });

  it('renders the evidence pack in-app with model, policy, sandbox, and audit evidence', async () => {
    renderDetail();

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();
    await waitFor(() => {
      expect(vi.mocked(apiClient.getRunEvidencePack)).toHaveBeenCalledWith('run-0421');
    });

    fireEvent.click(screen.getByRole('tab', { name: 'Evidence' }));

    expect(screen.getByText('Evidence Pack Viewer')).toBeInTheDocument();
    expect(screen.getByText('610 tokens')).toBeInTheDocument();
    expect(screen.getByText('Cost not recorded')).toBeInTheDocument();
    expect(screen.getAllByText('Production Merge Protection').length).toBeGreaterThan(0);
    expect(screen.getAllByText('opensandbox').length).toBeGreaterThan(0);
    expect(screen.getByText('workflow.start')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Download JSON' })).toBeInTheDocument();
  });

  it('renders model activity for the selected agent step', async () => {
    renderDetail();

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'I/O' }));

    expect(screen.getAllByText('LLM Activity').length).toBeGreaterThan(0);
    expect(screen.getAllByText('claude-sonnet-4-6').length).toBeGreaterThan(0);
    expect(screen.getAllByText('The agent drafted a technical specification.').length).toBeGreaterThan(0);
    expect(screen.getAllByText('github.read_issue').length).toBeGreaterThan(0);
  });

  it('renders visible agent reasoning events on the selected timeline step', async () => {
    vi.mocked(apiClient.getRun).mockResolvedValue({
      ...runsFixture[0],
      events: [
        ...(runsFixture[0].events ?? []),
        {
          id: 'evt-reasoning',
          type: 'agent_reasoning_started',
          message: JSON.stringify({
            stepId: 'step-2',
            summary: 'Inspecting the issue, loading context, and preparing the model/tool loop.',
          }),
          createdAt: new Date().toISOString(),
        },
      ],
    });

    renderDetail();

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();
    expect(
      screen.getAllByText('Inspecting the issue, loading context, and preparing the model/tool loop.').length,
    ).toBeGreaterThan(0);
  });

  it('shows selected-step visible reasoning in the agent detail panel', async () => {
    vi.mocked(apiClient.getRun).mockResolvedValue({
      ...runsFixture[0],
      events: [
        ...(runsFixture[0].events ?? []),
        {
          id: 'evt-panel-reasoning',
          type: 'agent_reasoning_delta',
          message: JSON.stringify({
            stepId: 'step-2',
            summary: 'Inspecting the approval context before opening github.read_issue.',
          }),
          createdAt: new Date().toISOString(),
        },
      ],
    });

    renderDetail();

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();

    const panel = screen.getByRole('region', { name: 'Step detail: Merge to main' });
    expect(within(panel).getByText('Visible Reasoning')).toBeInTheDocument();
    expect(
      within(panel).getByText('Inspecting the approval context before opening github.read_issue.'),
    ).toBeInTheDocument();
  });

  it('streams reasoning progress events without reloading the full run payload', async () => {
    let emitEvent: ((event: RunEvent) => void) | undefined;
    vi.mocked(apiClient.streamRunEvents).mockImplementation((_runId, onEvent) => {
      emitEvent = onEvent;
      return undefined;
    });

    renderDetail();

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();
    await waitFor(() => {
      expect(vi.mocked(apiClient.getRun)).toHaveBeenCalledTimes(1);
    });

    act(() => {
      emitEvent?.({
        id: 'evt-live-reasoning',
        type: 'agent_reasoning_delta',
        message: JSON.stringify({
          stepId: 'step-2',
          summary: 'Inspecting the repository context before opening a tool call.',
        }),
        createdAt: new Date().toISOString(),
      });
      emitEvent?.({
        id: 'evt-live-tool',
        type: 'agent_tool_call_started',
        message: JSON.stringify({
          stepId: 'step-2',
          toolName: 'repo.inspect_files',
          status: 'started',
          summary: "Calling tool 'repo.inspect_files'.",
        }),
        createdAt: new Date().toISOString(),
      });
    });

    expect(
      (await screen.findAllByText('Inspecting the repository context before opening a tool call.')).length,
    ).toBeGreaterThan(0);
    expect(screen.getAllByText('repo.inspect_files').length).toBeGreaterThan(0);
    expect(screen.getAllByText("Calling tool 'repo.inspect_files'.").length).toBeGreaterThan(0);
    expect(vi.mocked(apiClient.getRun)).toHaveBeenCalledTimes(1);
  });

  it('synthesizes a visible reasoning start summary from legacy service task events', async () => {
    vi.mocked(apiClient.getRun).mockResolvedValue({
      ...runsFixture[0],
      steps: [
        {
          id: 'step-legacy',
          name: 'Draft implementation note',
          type: 'serviceTask',
          status: 'completed',
          agentName: 'first-run-engineer',
        },
      ],
      events: [
        {
          id: 'evt-service-attempted',
          type: 'service_task_attempted',
          message: JSON.stringify({
            stepId: 'step-legacy',
            action: 'first-run.implement',
            attempt: 1,
          }),
          createdAt: new Date().toISOString(),
        },
      ],
    });

    renderDetail();

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();
    expect(
      screen.getAllByText("Starting 'first-run.implement': assembling context, checking runtime constraints, and preparing the model/tool loop (attempt 1).").length,
    ).toBeGreaterThan(0);
  });

  it('does not fetch operator-only evidence or allow cancel for viewers', async () => {
    renderDetail('run-0421', viewerAuthFixture);

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();
    expect(vi.mocked(apiClient.getRunEvidencePack)).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('tab', { name: 'Evidence' }));
    expect(screen.getByText('Operator role required to view and export evidence packs.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Export Evidence Pack' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Cancel run and stop further execution' })).toBeDisabled();
  });

  it('refreshes the run payload when stream events arrive so BPMN statuses update live', async () => {
    const runningRun = {
      ...runsFixture[1],
      steps: [
        {
          id: 'step-patch',
          name: 'Patch dependencies',
          type: 'service_task',
          status: 'running' as const,
        },
      ],
      events: [],
    };
    const completedRun = {
      ...runningRun,
      status: 'completed' as const,
      currentStep: undefined,
      steps: [
        {
          ...runningRun.steps[0],
          status: 'completed' as const,
        },
      ],
      events: [
        {
          id: 'evt-completed',
          type: 'node_completed',
          message: 'Patch dependencies completed.',
          createdAt: new Date().toISOString(),
        },
      ],
    };

    vi.mocked(apiClient.getRun)
      .mockResolvedValueOnce(runningRun)
      .mockResolvedValueOnce(completedRun);

    let emitEvent: ((event: RunEvent) => void) | undefined;
    vi.mocked(apiClient.streamRunEvents).mockImplementation((_runId, onEvent) => {
      emitEvent = onEvent;
      return undefined;
    });

    renderDetail('run-0420');

    expect(await screen.findByText('Run run-0420')).toBeInTheDocument();
    await waitFor(() => {
      expect(
        document.querySelector('[data-step-name="Patch dependencies"][data-step-status="running"]'),
      ).toBeInTheDocument();
    });

    act(() => {
      emitEvent?.({
        id: 'evt-completed',
        type: 'node_completed',
        message: 'Patch dependencies completed.',
        createdAt: new Date().toISOString(),
      });
    });

    await waitFor(() => {
      expect(vi.mocked(apiClient.getRun)).toHaveBeenCalledTimes(2);
      expect(
        document.querySelector('[data-step-name="Patch dependencies"][data-step-status="completed"]'),
      ).toBeInTheDocument();
    });
  });

  it('shows prompt, output, and step artifacts in the I/O tab for a selected step', async () => {
    renderDetail();

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'I/O' }));
    expect(screen.getAllByText('Specification generated successfully.').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Write a concise technical specification.').length).toBeGreaterThan(0);
    expect(screen.getByText('spec.generate')).toBeInTheDocument();
    expect(screen.getAllByText('agent_sandboxed').length).toBeGreaterThan(0);
    expect(screen.getAllByText('yes').length).toBeGreaterThan(0);
    expect(screen.getByText('Step artifacts')).toBeInTheDocument();
    expect(screen.getAllByText('spec.md').length).toBeGreaterThan(0);
    expect(
      screen.getAllByRole('link', { name: 'Download' }).some(
        (link) => link.getAttribute('href') === '/api/runs/run-0421/artifacts/spec.md',
      ),
    ).toBe(true);
    expect(screen.getAllByText('Sandbox Diagnostics').length).toBeGreaterThan(0);
    expect(screen.getAllByText('opensandbox').length).toBeGreaterThan(0);
    expect(screen.getAllByText('sbx-42').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Model Usage').length).toBeGreaterThan(0);
    expect(screen.getAllByText('claude-sonnet-4-6').length).toBeGreaterThan(0);
    expect(screen.getAllByText('412').length).toBeGreaterThan(0);
    expect(screen.getAllByText('198').length).toBeGreaterThan(0);
    expect(
      screen.getAllByText((_, element) => (
        element?.tagName === 'PRE' &&
        (element.textContent?.includes('spec generation running') ?? false)
      )).length,
    ).toBeGreaterThan(0);
    expect(screen.getAllByText('execd.run.request_id').length).toBeGreaterThan(0);
  });

  it('loads workflow BPMN XML and renders the viewer with step statuses', async () => {
    renderDetail();

    await waitFor(() => {
      expect(vi.mocked(apiClient.getWorkflow)).toHaveBeenCalledWith('wf-001');
    });

    // BpmnViewer mock should be visible with xml-loaded indicator
    await waitFor(() => {
      expect(screen.getByTestId('bpmn-viewer-mock')).toBeInTheDocument();
      expect(screen.getByTestId('viewer-xml-loaded')).toBeInTheDocument();
    });

    // Step status markers rendered inside the mock
    expect(
      document.querySelector('[data-step-name="Merge to main"][data-step-status="awaiting_approval"]'),
    ).toBeInTheDocument();
  });

  it('shows Approve / Reject buttons for pending approvals and calls decideApproval', async () => {
    renderDetail();

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Approvals' }));

    const approveBtn = await screen.findByRole('button', {
      name: /Approve Merge branch feature\/auth-refactor/i,
    });
    expect(approveBtn).toBeInTheDocument();

    fireEvent.click(approveBtn);

    await waitFor(() => {
      expect(vi.mocked(apiClient.decideApproval)).toHaveBeenCalledWith(
        'apr-1001',
        'approve',
        undefined,
      );
    });
  });

  it('opens the execution diff modal on "View Execution Diff" click', async () => {
    renderDetail();
    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'View Execution Diff' }));

    expect(await screen.findByRole('dialog', { name: 'Execution diff' })).toBeInTheDocument();

    // Close the modal
    fireEvent.click(screen.getByRole('button', { name: 'Close diff' }));
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'Execution diff' })).not.toBeInTheDocument();
    });
  });

  it('cancels a run and navigates back to the run board', async () => {
    renderDetail();
    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();

    fireEvent.click(
      screen.getByRole('button', { name: 'Cancel run and stop further execution' }),
    );
    const dialog = await screen.findByRole('dialog', { name: 'Cancel this run?' });
    expect(dialog).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Cancel run' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.cancelRun)).toHaveBeenCalledWith('run-0421');
      expect(screen.getByText('runs list')).toBeInTheDocument();
    });
  });

  it('shows the agent conversation and answers a pending human question', async () => {
    vi.mocked(apiClient.getRunInteractions).mockResolvedValue([
      {
        id: 'int-1',
        runId: 'run-0421',
        stepId: 'step-7',
        from: 'reviewer',
        kind: 'choice',
        addresseeType: 'human',
        addressee: null,
        blocking: true,
        prompt: 'Ship as-is, or add tests first?',
        options: ['Ship as-is', 'Add tests first'],
        status: 'pending',
        response: null,
        respondedBy: null,
        respondedAt: null,
        createdAt: new Date().toISOString(),
      },
    ]);

    renderDetail();
    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Conversation' }));
    expect(await screen.findByText('Ship as-is, or add tests first?')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Add tests first' }));
    await waitFor(() => {
      expect(vi.mocked(apiClient.answerInteraction)).toHaveBeenCalledWith(
        'run-0421',
        'int-1',
        'Add tests first',
      );
    });
  });

  it('surfaces conversation load failures instead of hiding identity data problems', async () => {
    vi.mocked(apiClient.getRunInteractions).mockRejectedValue(new Error('Conversation unavailable'));

    renderDetail();
    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Conversation' }));

    expect(await screen.findByText('Conversation unavailable')).toBeInTheDocument();
  });
});
