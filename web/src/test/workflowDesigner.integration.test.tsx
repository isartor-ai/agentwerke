import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { apiClient } from '../api/client';
import { WorkflowDesigner } from '../views/WorkflowDesigner';
import { workflowsFixture } from './fixtures';

vi.mock('../api/client', () => ({
  apiClient: {
    getWorkflows: vi.fn(),
  },
}));

describe('WorkflowDesigner integration', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getWorkflows).mockResolvedValue(workflowsFixture);
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
});
