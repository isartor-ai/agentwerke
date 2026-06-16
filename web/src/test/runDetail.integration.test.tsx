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
    getRunArtifactDownloadUrl: vi.fn(),
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
    expect(screen.getByText('Runtime Events')).toBeInTheDocument();
    expect(screen.getByText('Retry scheduled after transient failure.')).toBeInTheDocument();
    expect(screen.getByText('Timeout boundary triggered on security scan.')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Policy' }));
    expect(screen.getAllByText('Requires human approval.').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole('tab', { name: 'Artifacts' }));
    expect(screen.getByText('scan-report.json')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download' })).toHaveAttribute(
      'href',
      '/api/runs/run-0421/artifacts/scan-report.json',
    );

    fireEvent.click(screen.getByRole('tab', { name: 'Approvals' }));
    expect(screen.getByText('Merge branch feature/auth-refactor to main')).toBeInTheDocument();

    fireEvent.click(
      screen.getByRole('button', { name: 'Cancel run and stop further execution' }),
    );
    expect(screen.getByRole('dialog', { name: 'Cancel this run?' })).toBeInTheDocument();
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
});
