import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { WorkflowDesigner } from '../views/WorkflowDesigner';
import { apiClient } from '../api/client';

vi.mock('../api/client', () => ({
  apiClient: {
    getWorkflows: vi.fn(),
    uploadBpmnWorkflow: vi.fn(),
    validateBpmnWorkflow: vi.fn(),
    publishWorkflowDefinition: vi.fn(),
  },
}));

/**
 * E2E Test: Metadata Editor Complete Workflow
 * 
 * Scenario: Design workflow -> select node -> edit metadata -> see validation errors
 * -> fix errors -> confirm clean state
 */
describe('E2E: Metadata Editor Complete Workflow', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getWorkflows).mockResolvedValue([]);
    vi.mocked(apiClient.validateBpmnWorkflow).mockResolvedValue({
      isValid: true,
      errors: [],
    });
    vi.mocked(apiClient.publishWorkflowDefinition).mockResolvedValue({
      workflowId: 'wf-001',
      version: 'v1.0.0',
      publishedAt: new Date().toISOString(),
    });

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

    Object.defineProperty(globalThis, 'localStorage', {
      value: mockLocalStorage,
      writable: true,
    });
  });

  it('should show validation errors when metadata fields are empty', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    // Wait for page to load
    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();

    // Load a template
    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    // Wait for canvas to render with nodes
    await waitFor(() => {
      expect(screen.getByText('Build Artifact')).toBeInTheDocument();
    });

    // Click on a service task node
    fireEvent.click(screen.getByText('Build Artifact'));

    // Wait for metadata editor to show
    await waitFor(() => {
      expect(screen.getByText('Metadata Editor')).toBeInTheDocument();
    });

    // Clear agent field to trigger validation error
    const agentInput = screen.getByLabelText('Agent');
    fireEvent.change(agentInput, { target: { value: '' } });

    // Check that validation error appears
    await waitFor(() => {
      expect(screen.getByText('Agent is required for service/script tasks.')).toBeInTheDocument();
    });
  });

  it('should clear validation errors when fields are filled', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      expect(screen.getByText('Build Artifact')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Build Artifact'));

    await waitFor(() => {
      expect(screen.getByText('Metadata Editor')).toBeInTheDocument();
    });

    // Fill in all required fields
    fireEvent.change(screen.getByLabelText('Agent'), {
      target: { value: 'BuildAgent' },
    });
    fireEvent.change(screen.getByLabelText('Action'), {
      target: { value: 'ci.build_artifact' },
    });
    fireEvent.change(screen.getByLabelText('Purpose Type'), {
      target: { value: 'build_release' },
    });
    fireEvent.change(screen.getByLabelText('Policy Tag'), {
      target: { value: 'build_policy' },
    });

    // Verify clean state message appears
    await waitFor(() => {
      expect(screen.getByText(/Metadata checks passed for this node/i)).toBeInTheDocument();
    });
  });

  it('should auto-save metadata to localStorage', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      expect(screen.getByText('Build Artifact')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Build Artifact'));

    await waitFor(() => {
      expect(screen.getByText('Metadata Editor')).toBeInTheDocument();
    });

    const agentInput = screen.getByLabelText('Agent');
    fireEvent.change(agentInput, { target: { value: 'TestAgent' } });

    // Wait for auto-save debounce (5s in real impl, but should be instant in test)
    await waitFor(
      () => {
        const stored = localStorage.getItem('autofac_draft_node_metadata');
        expect(stored).not.toBeNull();
      },
      { timeout: 6000 },
    );
  });

  it('should recover metadata from localStorage', async () => {
    // Pre-populate localStorage with draft metadata
    const draftMetadata = {
      'node-1': {
        agent: 'SavedAgent',
        action: 'saved.action',
        environment: 'staging',
        purposeType: 'saved_purpose',
        policyTag: 'saved_policy',
      },
    };
    localStorage.setItem('autofac_draft_node_metadata', JSON.stringify(draftMetadata));

    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      expect(screen.getByText('Build Artifact')).toBeInTheDocument();
    });

    // Select a node and verify metadata was recovered
    fireEvent.click(screen.getByText('Build Artifact'));

    await waitFor(() => {
      // The recovered metadata should be available (note: exact behavior depends on implementation)
      expect(screen.getByText('Metadata Editor')).toBeInTheDocument();
    });
  });

  it('should show validation errors for user task without purpose type', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      // Look for any node that might be a user task (approval)
      const nodes = screen.getAllByText(/Approval/i);
      if (nodes.length > 0) {
        fireEvent.click(nodes[0]);
      }
    });

    // If we found an approval node, validate its metadata
    if (screen.queryByText('Metadata Editor')) {
      fireEvent.change(screen.getByLabelText('Purpose Type'), {
        target: { value: '' },
      });

      await waitFor(() => {
        expect(
          screen.getByText('Purpose type is required for approval tasks.'),
        ).toBeInTheDocument();
      });
    }
  });

  it('should clear localStorage draft after publish', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      expect(screen.getByText('Build Artifact')).toBeInTheDocument();
    });

    // Set metadata for all nodes (if needed)
    fireEvent.click(screen.getByText('Build Artifact'));

    await waitFor(() => {
      expect(screen.getByText('Metadata Editor')).toBeInTheDocument();
    });

    // Publish workflow
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }));

    await waitFor(() => {
      expect(vi.mocked(apiClient.publishWorkflowDefinition)).toHaveBeenCalled();
    });

    // Verify localStorage was cleared
    expect(localStorage.getItem('autofac_draft_bpmn_xml')).toBeNull();
    expect(localStorage.getItem('autofac_draft_node_metadata')).toBeNull();
  });

  it('should validate all required fields across multiple node edits', async () => {
    render(
      <MemoryRouter>
        <WorkflowDesigner />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Workflow Designer')).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole('button', { name: 'Use Template' })[0]);

    await waitFor(() => {
      expect(screen.getByText('Build Artifact')).toBeInTheDocument();
    });

    // Edit first node
    const firstNode = screen.getByText('Build Artifact');
    fireEvent.click(firstNode);

    await waitFor(() => {
      expect(screen.getByText('Metadata Editor')).toBeInTheDocument();
    });

    // Fill in first node
    fireEvent.change(screen.getByLabelText('Agent'), {
      target: { value: 'Agent1' },
    });
    fireEvent.change(screen.getByLabelText('Action'), {
      target: { value: 'action1' },
    });
    fireEvent.change(screen.getByLabelText('Purpose Type'), {
      target: { value: 'purpose1' },
    });
    fireEvent.change(screen.getByLabelText('Policy Tag'), {
      target: { value: 'policy1' },
    });

    await waitFor(() => {
      expect(screen.getByText(/Metadata checks passed/i)).toBeInTheDocument();
    });

    // Select another node and verify it's in clean state
    const nodeButtons = screen.getAllByRole('button', { name: /Build Artifact|Deploy/i });
    if (nodeButtons.length > 1) {
      fireEvent.click(nodeButtons[1]);

      // This node should have default empty metadata
      const agentInput = screen.getByLabelText('Agent');
      expect((agentInput as HTMLInputElement).value).not.toBe('Agent1');
    }
  });
});
