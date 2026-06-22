import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { apiClient } from '../api/client';
import { ApprovalsDashboard } from '../views/ApprovalsDashboard';
import { approvalsFixture, artifactApprovalFixture } from './fixtures';

vi.mock('../api/client', () => ({
  apiClient: {
    getApprovals: vi.fn(),
    decideApproval: vi.fn(),
    getRunArtifactContent: vi.fn(),
  },
}));

describe('ApprovalsDashboard integration', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getApprovals).mockResolvedValue(approvalsFixture);
    vi.mocked(apiClient.decideApproval).mockResolvedValue(undefined);
  });

  it('opens review drawer and submits approval decision', async () => {
    render(
      <MemoryRouter>
        <ApprovalsDashboard />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Approvals')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Review' }));

    expect(screen.getByRole('dialog', { name: 'Approval Request' })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Reason / comment (required for reject)'), {
      target: { value: 'Reviewed and accepted.' },
    });

    fireEvent.click(screen.getByRole('button', { name: 'Approve' }));

    await waitFor(() => {
      expect(apiClient.decideApproval).toHaveBeenCalledWith(
        'apr-1001',
        'approve',
        'Reviewed and accepted.',
      );
    });
  });

  it('filters to decided approvals and shows their status history', async () => {
    render(
      <MemoryRouter>
        <ApprovalsDashboard />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Approvals')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'approved' }));

    await waitFor(() => {
      expect(screen.getByText('Incident Recovery')).toBeInTheDocument();
      expect(screen.queryByText('GitHub PR Review')).not.toBeInTheDocument();
      expect(screen.getByText('Decision status: approved')).toBeInTheDocument();
    });
  });

  it('renders the generated artifact as markdown when an approval references one', async () => {
    vi.mocked(apiClient.getApprovals).mockResolvedValue([artifactApprovalFixture]);
    vi.mocked(apiClient.getRunArtifactContent).mockResolvedValue('# Requirements\n\nDo the thing.');

    render(
      <MemoryRouter>
        <ApprovalsDashboard />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Approvals')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Review' }));

    expect(screen.getByText('Artifact: requirements.md')).toBeInTheDocument();
    await waitFor(() => {
      expect(apiClient.getRunArtifactContent).toHaveBeenCalledWith('run-0430', 'requirements.md');
    });
    expect(await screen.findByRole('heading', { name: 'Requirements' })).toBeInTheDocument();
    expect(screen.getByText('Do the thing.')).toBeInTheDocument();
  });

  it('does not show an artifact section when the approval has none', async () => {
    vi.mocked(apiClient.getRunArtifactContent).mockClear();

    render(
      <MemoryRouter>
        <ApprovalsDashboard />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Approvals')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Review' }));

    expect(screen.getByRole('dialog', { name: 'Approval Request' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Generated artifact')).not.toBeInTheDocument();
    expect(apiClient.getRunArtifactContent).not.toHaveBeenCalled();
  });
});
