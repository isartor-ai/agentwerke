import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { apiClient } from '../api/client';
import { RunDetail } from '../views/RunDetail';
import { runsFixture, workflowsFixture } from './fixtures';

// BpmnViewer needs SVG layout unavailable in jsdom; use the test stub
vi.mock('../components/BpmnViewer');

vi.mock('../api/client', () => ({
  apiClient: {
    getRun: vi.fn(),
    getWorkflow: vi.fn(),
    streamRunEvents: vi.fn(),
    getRunArtifactDownloadUrl: vi.fn(),
    getRunEvidencePackDownloadUrl: vi.fn(),
    getRunEvidencePack: vi.fn(),
    downloadRunEvidencePack: vi.fn(),
    decideApproval: vi.fn(),
    cancelRun: vi.fn(),
  },
}));

const WORKFLOW_WITH_BPMN = {
  ...workflowsFixture[0],
  bpmnXml: '<?xml version="1.0"?><bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"><bpmn:process id="P1"><bpmn:serviceTask id="T1" name="Merge to main" /></bpmn:process></bpmn:definitions>',
};

describe('RunDetail integration', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getRun).mockResolvedValue(runsFixture[0]);
    vi.mocked(apiClient.getWorkflow).mockResolvedValue(WORKFLOW_WITH_BPMN);
    vi.mocked(apiClient.getRunArtifactDownloadUrl).mockImplementation(
      (runId, artifactName) => `/api/runs/${runId}/artifacts/${artifactName}`,
    );
    vi.mocked(apiClient.streamRunEvents).mockImplementation(() => undefined);
    vi.mocked(apiClient.getRunEvidencePackDownloadUrl).mockImplementation(
      (runId) => `/api/runs/${runId}/evidence-pack/download`,
    );
    vi.mocked(apiClient.downloadRunEvidencePack).mockResolvedValue(undefined);
    vi.mocked(apiClient.getRunEvidencePack).mockResolvedValue({
      schemaVersion: 'autofac.evidence-pack.v1',
      generatedAt: '2026-06-26T00:00:00Z',
      workflow: { name: 'Demo', version: 'v1', bpmnSha256: 'abc123' },
      modelUsage: [
        { stepName: 'Analyze', agentName: 'analyst', modelId: 'claude-haiku-4-5', inputTokens: 1200, outputTokens: 300, elapsedMs: 1500 },
      ],
      policyDecisions: [{ action: 'spec.generate', kind: 'allow' }],
      sandboxExecutions: [{ provider: 'docker', commandState: 'Completed', exitCode: 0, durationMs: 4200 }],
      approvals: [{ status: 'approved', decidedBy: 'dev:admin' }],
      toolCalls: [],
      connectorCalls: [],
      auditLog: [{}],
      runEvents: [{}, {}],
    });
    vi.mocked(apiClient.decideApproval).mockResolvedValue(undefined);
    vi.mocked(apiClient.cancelRun).mockResolvedValue(undefined);
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  function renderDetail(id = 'run-0421') {
    return render(
      <MemoryRouter initialEntries={[`/runs/${id}`]}>
        <Routes>
          <Route path="/runs/:runId" element={<RunDetail />} />
          <Route path="/runs" element={<div>runs list</div>} />
        </Routes>
      </MemoryRouter>,
    );
  }

  it('renders live run detail with tabs, events, artifacts and approvals', async () => {
    renderDetail();

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Summary' })).toBeInTheDocument();
    expect(screen.getByText('Evidence Pack')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Export Evidence Pack' }));
    await waitFor(() => {
      expect(vi.mocked(apiClient.downloadRunEvidencePack)).toHaveBeenCalledWith('run-0421');
    });
    expect(screen.getByText('Runtime Events')).toBeInTheDocument();
    expect(screen.getByText('Retry scheduled after transient failure.')).toBeInTheDocument();
    expect(screen.getByText('Timeout boundary triggered on security scan.')).toBeInTheDocument();

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

  it('loads and renders the evidence pack on the Evidence tab', async () => {
    renderDetail();
    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Evidence' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.getRunEvidencePack)).toHaveBeenCalledWith('run-0421');
    });
    // Model usage row + token totals render from the fetched pack.
    expect(await screen.findByText('claude-haiku-4-5')).toBeInTheDocument();
    expect(screen.getByText(/Model usage/)).toBeInTheDocument();
    expect(screen.getByText('abc123')).toBeInTheDocument();
  });
});
