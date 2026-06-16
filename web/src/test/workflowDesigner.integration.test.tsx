import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { apiClient } from '../api/client';
import { WorkflowDesigner } from '../views/WorkflowDesigner';
import { workflowsFixture } from './fixtures';

// The real <BpmnModeler /> wraps bpmn-js, which needs SVG layout and cannot
// render in jsdom. Substitute the stub that mirrors the imperative handle.
vi.mock('../components/BpmnModeler');

vi.mock('../api/client', () => ({
  apiClient: {
    getWorkflows: vi.fn(),
    getWorkflow: vi.fn(),
    importWorkflowDefinition: vi.fn(),
    uploadBpmnWorkflow: vi.fn(),
    validateBpmnWorkflow: vi.fn(),
    publishWorkflowDefinition: vi.fn(),
    getRuns: vi.fn(),
    startRun: vi.fn(),
  },
}));

const PERSISTED_XML =
  '<?xml version="1.0" encoding="UTF-8"?><bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"><bpmn:process id="PersistedFlow" name="Persisted Flow"><bpmn:startEvent id="Start" /><bpmn:endEvent id="End" /></bpmn:process></bpmn:definitions>';

const INVALID_VALIDATION = {
  isValid: false,
  processId: 'DeployWorkflow',
  processName: 'Deploy Workflow',
  warnings: [],
  errors: [
    {
      message: 'Service/script task requires autofac:agentTask metadata under extensionElements.',
      elementName: 'serviceTask',
      elementId: 'DeployTask',
      lineNumber: 12,
      linePosition: 5,
    },
  ],
};

describe('WorkflowDesigner integration', () => {
  let createObjectUrlSpy: { mockRestore: () => void };
  let revokeObjectUrlSpy: { mockRestore: () => void };

  beforeEach(() => {
    vi.mocked(apiClient.getWorkflows).mockResolvedValue(workflowsFixture);
    vi.mocked(apiClient.getWorkflow).mockImplementation(async (id: string) => ({
      ...(workflowsFixture.find((workflow) => workflow.id === id) ?? workflowsFixture[0]),
      id,
      bpmnXml: PERSISTED_XML,
    }));
    vi.mocked(apiClient.importWorkflowDefinition).mockResolvedValue({
      workflowId: 'wf-import-1',
      validation: { isValid: true, processId: 'DeployWorkflow', processName: 'Deploy Workflow', warnings: [], errors: [] },
    });
    vi.mocked(apiClient.validateBpmnWorkflow).mockResolvedValue(INVALID_VALIDATION);
    vi.mocked(apiClient.publishWorkflowDefinition).mockResolvedValue({
      workflowId: 'wf-001',
      version: 'v2.3.2',
      publishedAt: '2026-06-10T16:00:00.000Z',
    });
    vi.mocked(apiClient.getRuns).mockResolvedValue([]);
    vi.mocked(apiClient.startRun).mockResolvedValue({ runId: 'run-new-1' });

    if (typeof URL.createObjectURL !== 'function') {
      Object.defineProperty(URL, 'createObjectURL', { configurable: true, writable: true, value: () => 'blob:x' });
    }
    if (typeof URL.revokeObjectURL !== 'function') {
      Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, writable: true, value: () => undefined });
    }
    createObjectUrlSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-bpmn-url');
    revokeObjectUrlSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
  });

  afterEach(() => {
    createObjectUrlSpy.mockRestore();
    revokeObjectUrlSpy.mockRestore();
    vi.clearAllMocks();
  });

  function renderDesigner() {
    return render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );
  }

  it('renders workflows and filters by search query', async () => {
    renderDesigner();

    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();
    expect(screen.getAllByText('GitHub PR Review').length).toBeGreaterThan(0);

    fireEvent.change(screen.getByPlaceholderText('Search workflows'), {
      target: { value: 'dependency' },
    });

    await waitFor(() => {
      expect(screen.getByText('Dependency Patch')).toBeInTheDocument();
      expect(screen.getAllByRole('button', { name: /Dependency Patch/i }).length).toBe(1);
    });
  });

  it('loads persisted BPMN xml for the selected workflow into the canvas', async () => {
    renderDesigner();

    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();

    await waitFor(() => {
      expect(vi.mocked(apiClient.getWorkflow)).toHaveBeenCalledWith('wf-001');
      expect(screen.getByTestId('bpmn-modeler-mock')).toHaveTextContent('PersistedFlow');
    });
  });

  it('validates the current canvas and shows actionable errors', async () => {
    renderDesigner();

    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByTestId('bpmn-modeler-mock')).toHaveTextContent('PersistedFlow');
    });

    fireEvent.click(screen.getByRole('button', { name: 'Validate' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.validateBpmnWorkflow)).toHaveBeenCalledWith(
        expect.objectContaining({ bpmnXml: expect.stringContaining('PersistedFlow') }),
      );
      expect(screen.getByText('Invalid')).toBeInTheDocument();
      expect(screen.getByText(/requires autofac:agentTask metadata/i)).toBeInTheDocument();
      expect(screen.getByText(/at line 12, col 5/i)).toBeInTheDocument();
    });
  });

  it('shows validation warnings without marking the workflow invalid', async () => {
    vi.mocked(apiClient.validateBpmnWorkflow).mockResolvedValueOnce({
      isValid: true,
      processId: 'WarnFlow',
      processName: 'Warn Flow',
      errors: [],
      warnings: [
        {
          message: "Workflow process is missing a human-readable 'name' attribute.",
          elementName: 'process',
          elementId: 'WarnFlow',
          lineNumber: 2,
          linePosition: 3,
        },
      ],
    });

    renderDesigner();

    expect(await screen.findByText('Templates')).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    fireEvent.click(screen.getByRole('button', { name: 'Validate' }));

    await waitFor(() => {
      expect(screen.getByText('Valid')).toBeInTheDocument();
      expect(screen.getByText('Warnings:')).toBeInTheDocument();
      expect(screen.getByText(/human-readable 'name' attribute/i)).toBeInTheDocument();
    });
  });

  it('loads a template onto the canvas', async () => {
    renderDesigner();

    expect(await screen.findByText('Templates')).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      expect(screen.getByTestId('bpmn-modeler-mock')).toHaveTextContent('ProductionDeploy');
    });
  });

  it('exports BPMN as a downloadable file', async () => {
    renderDesigner();

    expect(await screen.findByText('Templates')).toBeInTheDocument();

    const appendSpy = vi.spyOn(document.body, 'appendChild');
    const removeSpy = vi.spyOn(document.body, 'removeChild');

    try {
      fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);
      fireEvent.click(screen.getByRole('button', { name: 'Export BPMN' }));

      await waitFor(() => {
        expect(createObjectUrlSpy).toHaveBeenCalledTimes(1);
        expect(revokeObjectUrlSpy).toHaveBeenCalledWith('blob:mock-bpmn-url');
        expect(appendSpy).toHaveBeenCalled();
        expect(removeSpy).toHaveBeenCalled();
      });
    } finally {
      appendSpy.mockRestore();
      removeSpy.mockRestore();
    }
  });

  it('publishes a template draft via import then publish', async () => {
    renderDesigner();

    expect(await screen.findByText('Templates')).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    fireEvent.click(screen.getByRole('button', { name: 'Publish' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.importWorkflowDefinition)).toHaveBeenCalled();
      expect(vi.mocked(apiClient.publishWorkflowDefinition)).toHaveBeenCalledWith(
        expect.objectContaining({ workflowId: 'wf-import-1' }),
      );
      expect(screen.getByText(/Published as v2.3.2/i)).toBeInTheDocument();
    });
  });

  it('switches to Monitor tab and shows run list for the selected workflow', async () => {
    const { runsFixture } = await import('./fixtures');
    vi.mocked(apiClient.getRuns).mockResolvedValue(
      runsFixture.filter((r) => r.workflowId === 'wf-001'),
    );

    renderDesigner();
    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();

    // Switch to Monitor tab
    fireEvent.click(screen.getByRole('tab', { name: 'Monitor' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.getRuns)).toHaveBeenCalled();
    });

    // run-0421 belongs to wf-001
    await waitFor(() => {
      expect(screen.getByText('run-0421')).toBeInTheDocument();
    });
  });

  it('imports a BPMN file and surfaces returned validation', async () => {
    vi.mocked(apiClient.importWorkflowDefinition).mockResolvedValueOnce({
      workflowId: 'wf-import-1',
      validation: INVALID_VALIDATION,
    });

    renderDesigner();
    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();
    // Let the initial workflow detail load settle before importing.
    await waitFor(() => {
      expect(screen.getByTestId('bpmn-modeler-mock')).toHaveTextContent('PersistedFlow');
    });

    const file = new File(
      ['<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"></bpmn:definitions>'],
      'workflow.bpmn',
      { type: 'text/xml' },
    );

    fireEvent.click(screen.getByRole('button', { name: 'Import BPMN' }));
    fireEvent.change(screen.getByLabelText('Import BPMN file'), { target: { files: [file] } });

    await waitFor(() => {
      expect(vi.mocked(apiClient.importWorkflowDefinition)).toHaveBeenCalledWith(
        expect.objectContaining({ fileName: 'workflow.bpmn' }),
      );
      expect(screen.getByText('Invalid')).toBeInTheDocument();
      expect(screen.getByText(/requires autofac:agentTask metadata/i)).toBeInTheDocument();
    });
  });
});
