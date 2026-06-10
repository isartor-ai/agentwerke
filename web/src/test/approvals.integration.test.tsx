import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { apiClient } from '../api/client';
import { ApprovalsDashboard } from '../views/ApprovalsDashboard';
import { approvalsFixture } from './fixtures';

vi.mock('../api/client', () => ({
  apiClient: {
    getApprovals: vi.fn(),
    decideApproval: vi.fn(),
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
});
