import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { apiClient } from '../api/client';
import { WorkflowDesigner } from '../views/WorkflowDesigner';
import { workflowsFixture } from './fixtures';

vi.mock('../api/client', () => ({
  apiClient: {
    getWorkflows: vi.fn(),
    uploadBpmnWorkflow: vi.fn(),
    validateBpmnWorkflow: vi.fn(),
    publishWorkflowDefinition: vi.fn(),
  },
}));

describe('WorkflowDesigner integration', () => {
  let createObjectUrlSpy: ReturnType<typeof vi.spyOn>;
  let revokeObjectUrlSpy: ReturnType<typeof vi.spyOn>;
  let hadCreateObjectUrl = false;
  let hadRevokeObjectUrl = false;

  beforeEach(() => {
    vi.mocked(apiClient.getWorkflows).mockResolvedValue(workflowsFixture);
    vi.mocked(apiClient.uploadBpmnWorkflow).mockResolvedValue({
      workflowId: 'wf-import-1',
      validation: {
        isValid: false,
        processId: 'DeployWorkflow',
        processName: 'Deploy Workflow',
        errors: [
          {
            message: 'Service/script task requires autofac:agentTask metadata under extensionElements.',
            elementName: 'serviceTask',
            elementId: 'DeployTask',
            lineNumber: 12,
            linePosition: 5,
          },
        ],
      },
    });
    vi.mocked(apiClient.validateBpmnWorkflow).mockResolvedValue({
      isValid: false,
      processId: 'DeployWorkflow',
      processName: 'Deploy Workflow',
      errors: [
        {
          message: 'Service/script task requires autofac:agentTask metadata under extensionElements.',
          elementName: 'serviceTask',
          elementId: 'DeployTask',
          lineNumber: 12,
          linePosition: 5,
        },
      ],
    });
    vi.mocked(apiClient.publishWorkflowDefinition).mockResolvedValue({
      workflowId: 'wf-001',
      version: 'v2.3.2',
      publishedAt: '2026-06-10T16:00:00.000Z',
    });

    hadCreateObjectUrl = typeof URL.createObjectURL === 'function';
    hadRevokeObjectUrl = typeof URL.revokeObjectURL === 'function';

    if (!hadCreateObjectUrl) {
      Object.defineProperty(URL, 'createObjectURL', {
        configurable: true,
        writable: true,
        value: () => 'blob:polyfill-bpmn-url',
      });
    }

    if (!hadRevokeObjectUrl) {
      Object.defineProperty(URL, 'revokeObjectURL', {
        configurable: true,
        writable: true,
        value: () => undefined,
      });
    }

    createObjectUrlSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-bpmn-url');
    revokeObjectUrlSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
  });

  afterEach(() => {
    createObjectUrlSpy.mockRestore();
    revokeObjectUrlSpy.mockRestore();

    if (!hadCreateObjectUrl) {
      delete (URL as { createObjectURL?: (obj: Blob | MediaSource) => string }).createObjectURL;
    }

    if (!hadRevokeObjectUrl) {
      delete (URL as { revokeObjectURL?: (url: string) => void }).revokeObjectURL;
    }
  });

  it('renders workflows and filters by search query', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

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

  it('imports BPMN and shows actionable validation errors', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();

    const file = new File(
      ['<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"></bpmn:definitions>'],
      'workflow.bpmn',
      { type: 'text/xml' },
    );

    fireEvent.click(screen.getByRole('button', { name: 'Import BPMN' }));
    fireEvent.change(screen.getByLabelText('Import BPMN file'), {
      target: { files: [file] },
    });

    await waitFor(() => {
      expect(screen.getByText('Status:')).toBeInTheDocument();
      expect(screen.getByText('Invalid')).toBeInTheDocument();
      expect(screen.getByText(/requires autofac:agentTask metadata/i)).toBeInTheDocument();
      expect(screen.getByText(/at line 12, col 5/i)).toBeInTheDocument();
    });
  });

  it('loads a template and renders BPMN canvas nodes', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Template Gallery')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      expect(screen.getByLabelText('BPMN canvas')).toBeInTheDocument();
      expect(screen.getByText('Build Artifact')).toBeInTheDocument();
      expect(screen.getByText('Deploy to Production')).toBeInTheDocument();
    });
  });

  it('exports BPMN as a downloadable file', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Template Gallery')).toBeInTheDocument();

    const originalAppendChild = document.body.appendChild;
    const originalRemoveChild = document.body.removeChild;
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
      document.body.appendChild = originalAppendChild;
      document.body.removeChild = originalRemoveChild;
    }
  });

  it('shows metadata editor validation for selected service task node', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Template Gallery')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      expect(screen.getByText('Build Artifact')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Build Artifact'));

    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'BPMN metadata editor' })).toBeInTheDocument();
      expect(screen.getByText(/Metadata checks passed for this node/i)).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText('Agent'), { target: { value: '' } });

    await waitFor(() => {
      expect(screen.getByText('Agent is required for service/script tasks.')).toBeInTheDocument();
    });
  });

  it('publishes workflow and shows diff after edits', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Template Gallery')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      expect(screen.getByText('Build Artifact')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Publish' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.publishWorkflowDefinition)).toHaveBeenCalled();
      expect(screen.getByText(/Published as v2.3.2/i)).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText('BPMN XML'), {
      target: {
        value:
          '<?xml version="1.0" encoding="UTF-8"?>\n<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"><bpmn:process id="P" name="P"><bpmn:startEvent id="S" /><bpmn:endEvent id="E" /></bpmn:process></bpmn:definitions>',
      },
    });

    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'BPMN diff view' })).toBeInTheDocument();
      expect(screen.getAllByText(/bpmn:definitions/i).length).toBeGreaterThan(0);
    });
  });

  it('auto-saves metadata to localStorage when editing', async () => {
    // Mock localStorage
    const mockLocalStorage = (() => {
      let store: Record<string, string> = {};
      return {
        getItem: (key: string) => store[key] || null,
        setItem: (key: string, value: string) => {
          store[key] = value;
        },
        removeItem: (key: string) => {
          delete store[key];
        },
        clear: () => {
          store = {};
        },
      };
    })();

    Object.defineProperty(global, 'localStorage', {
      value: mockLocalStorage,
      writable: true,
    });

    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Template Gallery')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      expect(screen.getByText('Build Artifact')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Build Artifact'));

    await waitFor(() => {
      expect(screen.getByText('Metadata Editor')).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText('Agent'), { target: { value: 'TestAgent' } });

    // After debounce window, metadata should be in localStorage
    await waitFor(
      () => {
        const stored = mockLocalStorage.getItem('autofac_draft_node_metadata');
        expect(stored).not.toBeNull();
      },
      { timeout: 6000 },
    );
  });

  it('clears localStorage after successful publish', async () => {
    const mockLocalStorage = (() => {
      let store: Record<string, string> = {};
      return {
        getItem: (key: string) => store[key] || null,
        setItem: (key: string, value: string) => {
          store[key] = value;
        },
        removeItem: (key: string) => {
          delete store[key];
        },
        clear: () => {
          store = {};
        },
      };
    })();

    Object.defineProperty(global, 'localStorage', {
      value: mockLocalStorage,
      writable: true,
    });

    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Template Gallery')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      expect(screen.getByText('Build Artifact')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Publish' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.publishWorkflowDefinition)).toHaveBeenCalled();
    });

    // Verify draft was cleared from localStorage
    expect(mockLocalStorage.getItem('autofac_draft_bpmn_xml')).toBeNull();
    expect(mockLocalStorage.getItem('autofac_draft_node_metadata')).toBeNull();
  });
});
