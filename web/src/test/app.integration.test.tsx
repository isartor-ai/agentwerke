import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { apiClient } from '../api/client';
import App from '../App';
import { approvalsFixture, runsFixture, workflowsFixture } from './fixtures';

vi.mock('../api/client', () => ({
  apiClient: {
    getRuns: vi.fn(),
    getRun: vi.fn(),
    getWorkflows: vi.fn(),
    getApprovals: vi.fn(),
    decideApproval: vi.fn(),
  },
}));

describe('App integration', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getRuns).mockResolvedValue(runsFixture);
    vi.mocked(apiClient.getRun).mockResolvedValue(runsFixture[0]);
    vi.mocked(apiClient.getWorkflows).mockResolvedValue(workflowsFixture);
    vi.mocked(apiClient.getApprovals).mockResolvedValue(approvalsFixture);
    vi.mocked(apiClient.decideApproval).mockResolvedValue(undefined);
    window.history.pushState({}, '', '/runs');
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('boots authenticated shell and navigates between core views', async () => {
    render(<App />);

    expect(await screen.findByText('Runs')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('link', { name: 'Workflows' }));
    await waitFor(() => {
      expect(screen.getByText('Workflow Designer')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('link', { name: 'Approvals' }));
    await waitFor(() => {
      expect(screen.getByText('Approvals')).toBeInTheDocument();
    });
  });
});
