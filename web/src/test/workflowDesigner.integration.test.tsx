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
    getRuntimeMode: vi.fn(),
    getTemplates: vi.fn(),
    getTemplate: vi.fn(),
    getAgents: vi.fn(),
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

const templateSummaries = [
  {
    id: 'issue-to-pr',
    name: 'Issue to Pull Request',
    description: 'Full specification, planning, implementation, and code-review cycle.',
    trigger: 'manual',
    policyLevel: 'standard',
    tags: ['sdlc', 'github'],
    agentRoles: ['specification-agent', 'implementation-agent', 'github-agent'],
    approvalRoles: ['developer'],
  },
];

const issueToPrTemplate = {
  ...templateSummaries[0],
  requiredInputs: ['issue_url', 'repository'],
  evidenceExpectations: ['spec_document', 'code_changes'],
  bpmnXml:
    '<?xml version="1.0" encoding="UTF-8"?><bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:autofac="https://autofac.de/bpmn/extensions/v1"><bpmn:process id="IssueToPr" name="Issue to Pull Request"><bpmn:serviceTask id="Specify" name="Specify"><bpmn:extensionElements><autofac:agentTask agent="specification-agent" action="spec.generate" purposeType="specification" policyTag="sdlc-spec" /></bpmn:extensionElements></bpmn:serviceTask><bpmn:userTask id="CodeReview" name="Code Review Approval"><bpmn:extensionElements><autofac:approvalTask purposeType="code_review" policyTag="human-code-review" /></bpmn:extensionElements></bpmn:userTask></bpmn:process></bpmn:definitions>',
};

describe('WorkflowDesigner integration', () => {
  let createObjectUrlSpy: { mockRestore: () => void };
  let revokeObjectUrlSpy: { mockRestore: () => void };

  beforeEach(() => {
    vi.mocked(apiClient.getRuntimeMode).mockResolvedValue({ mode: 'Autofac', camundaEnabled: false });
    vi.mocked(apiClient.getTemplates).mockResolvedValue(templateSummaries);
    vi.mocked(apiClient.getTemplate).mockResolvedValue(issueToPrTemplate);
    vi.mocked(apiClient.getAgents).mockResolvedValue([]);
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

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    expect(screen.getAllByText('GitHub PR Review').length).toBeGreaterThan(0);

    fireEvent.change(screen.getByPlaceholderText('Search workflows'), {
      target: { value: 'dependency' },
    });

    await waitFor(() => {
      expect(screen.getByText('Dependency Patch')).toBeInTheDocument();
      expect(screen.getAllByRole('button', { name: /Dependency Patch/i }).length).toBe(1);
    });
  });

  it('creates an issue-to-PR draft from template settings without opening the BPMN canvas', async () => {
    renderDesigner();

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    expect(screen.queryByTestId('bpmn-modeler-mock')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Configure Issue to Pull Request/i }));

    await waitFor(() => {
      expect(apiClient.getTemplate).toHaveBeenCalledWith('issue-to-pr');
      expect(screen.getByLabelText('Workflow name')).toHaveValue('Issue to Pull Request');
    });

    fireEvent.change(screen.getByLabelText('Workflow name'), {
      target: { value: 'Payments Issue to PR' },
    });
    fireEvent.change(screen.getByLabelText('issue_url'), {
      target: { value: 'https://github.com/acme/payments/issues/42' },
    });
    fireEvent.change(screen.getByLabelText('repository'), {
      target: { value: 'acme/payments' },
    });
    fireEvent.change(screen.getByLabelText('specification-agent'), {
      target: { value: 'ba-agent' },
    });
    fireEvent.change(screen.getByLabelText('developer'), {
      target: { value: 'payments-maintainer' },
    });

    fireEvent.click(screen.getByRole('button', { name: 'Create Draft' }));

    await waitFor(() => {
      expect(apiClient.importWorkflowDefinition).toHaveBeenCalledWith(
        expect.objectContaining({
          fileName: 'payments-issue-to-pr.bpmn',
          bpmnXml: expect.stringContaining('Payments Issue to PR'),
        }),
      );
      expect(screen.getByText(/Draft created from Issue to Pull Request/i)).toBeInTheDocument();
      expect(screen.getByText('Valid')).toBeInTheDocument();
    });
  });

  it('loads persisted BPMN xml for the selected workflow into the canvas', async () => {
    renderDesigner();

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Advanced BPMN' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.getWorkflow)).toHaveBeenCalledWith('wf-001');
      expect(screen.getByTestId('bpmn-modeler-mock')).toHaveTextContent('PersistedFlow');
    });
  });

  it('validates the current canvas and shows actionable errors', async () => {
    renderDesigner();

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Advanced BPMN' }));
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

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Advanced BPMN' }));

    fireEvent.click(screen.getByRole('button', { name: 'Validate' }));

    await waitFor(() => {
      expect(screen.getByText('Valid')).toBeInTheDocument();
      expect(screen.getByText('Compatibility Warnings')).toBeInTheDocument();
      expect(screen.getByText(/human-readable 'name' attribute/i)).toBeInTheDocument();
    });
  });

  it('opens the selected catalog template in advanced BPMN mode', async () => {
    renderDesigner();

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Configure Issue to Pull Request/i }));
    fireEvent.click(await screen.findByRole('button', { name: 'Open Advanced BPMN' }));

    await waitFor(() => {
      expect(screen.getByTestId('bpmn-modeler-mock')).toHaveTextContent('IssueToPr');
    });
  });

  it('exports BPMN as a downloadable file', async () => {
    renderDesigner();

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Advanced BPMN' }));

    const appendSpy = vi.spyOn(document.body, 'appendChild');
    const removeSpy = vi.spyOn(document.body, 'removeChild');

    try {
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

  it('publishes the current advanced workflow', async () => {
    renderDesigner();

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Advanced BPMN' }));

    fireEvent.click(screen.getByRole('button', { name: 'Publish' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.publishWorkflowDefinition)).toHaveBeenCalledWith(
        expect.objectContaining({ workflowId: 'wf-001' }),
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
    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();

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

  it('shows the Autofac runtime mode badge in the advanced BPMN toolbar', async () => {
    renderDesigner();

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Advanced BPMN' }));

    await waitFor(() => {
      expect(screen.getByLabelText('Active runtime mode')).toHaveTextContent('Autofac Runtime');
    });
  });

  it('shows Camunda runtime mode badge when Camunda is active', async () => {
    vi.mocked(apiClient.getRuntimeMode).mockResolvedValueOnce({ mode: 'Camunda', camundaEnabled: true });

    renderDesigner();

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Advanced BPMN' }));

    await waitFor(() => {
      expect(screen.getByLabelText('Active runtime mode')).toHaveTextContent('Camunda Runtime');
    });
  });

  it('validation panel labels errors as runtime errors and warnings as compatibility warnings', async () => {
    vi.mocked(apiClient.validateBpmnWorkflow).mockResolvedValueOnce({
      isValid: false,
      processId: 'TestFlow',
      processName: 'Test Flow',
      errors: [
        {
          message: 'Service task missing agentTask metadata.',
          elementName: 'serviceTask',
          elementId: 'T1',
          lineNumber: 5,
          linePosition: 3,
        },
      ],
      warnings: [
        {
          message: 'eventBasedGateway is not supported by the default runtime.',
          elementName: 'eventBasedGateway',
          elementId: 'GW1',
          lineNumber: 10,
          linePosition: 1,
        },
      ],
    });

    renderDesigner();

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Advanced BPMN' }));
    fireEvent.click(screen.getByRole('button', { name: 'Validate' }));

    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'Runtime errors' })).toBeInTheDocument();
      expect(screen.getByText(/Service task missing agentTask metadata/i)).toBeInTheDocument();
      expect(screen.getByRole('region', { name: 'Compatibility warnings' })).toBeInTheDocument();
      expect(screen.getByText(/eventBasedGateway is not supported/i)).toBeInTheDocument();
    });
  });

  it('shows Edit in BPMN editor button after template draft is created', async () => {
    renderDesigner();

    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Configure Issue to Pull Request/i }));

    await waitFor(() => {
      expect(screen.getByLabelText('Workflow name')).toHaveValue('Issue to Pull Request');
    });

    fireEvent.click(screen.getByRole('button', { name: 'Create Draft' }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Edit in BPMN editor' })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Edit in BPMN editor' }));

    await waitFor(() => {
      expect(screen.getByTestId('bpmn-modeler-mock')).toBeInTheDocument();
    });
  });

  it('imports a BPMN file and surfaces returned validation', async () => {
    vi.mocked(apiClient.importWorkflowDefinition).mockResolvedValueOnce({
      workflowId: 'wf-import-1',
      validation: INVALID_VALIDATION,
    });

    renderDesigner();
    expect(await screen.findByText('SDLC Factory')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: 'Advanced BPMN' }));
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
