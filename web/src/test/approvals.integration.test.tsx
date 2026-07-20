import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { apiClient } from '../api/client';
import { ApprovalsDashboard } from '../views/ApprovalsDashboard';
import { adminAuthFixture, approvalsFixture, artifactApprovalFixture, viewerAuthFixture } from './fixtures';

vi.mock('../api/client', () => ({
  apiClient: {
    getApprovals: vi.fn(),
    decideApproval: vi.fn(),
    getRunArtifactContent: vi.fn(),
    getToolAccessRequests: vi.fn(),
    answerInteraction: vi.fn(),
    getPendingInteractions: vi.fn(),
    rejectInteraction: vi.fn(),
    cancelInteraction: vi.fn(),
    retryInteractionDelivery: vi.fn(),
  },
}));

const toolAccessFixture = {
  interactionId: 'int-42',
  runId: 'run-0430',
  workflowName: 'Demo NVIDIA Issue to PR',
  stepId: 'step-9',
  stepName: 'Senior Review',
  agent: 'senior-reviewer',
  toolName: 'github.post_review',
  intent: '{"pull_number":"23"}',
  prompt: "Tool access request: agent 'senior-reviewer' needs tool 'github.post_review'…",
  options: ['approve', 'deny', 'fail'],
  createdAt: '2026-07-12T08:00:00Z',
};

describe('ApprovalsDashboard integration', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.getApprovals).mockResolvedValue(approvalsFixture);
    vi.mocked(apiClient.decideApproval).mockResolvedValue(undefined);
    vi.mocked(apiClient.getToolAccessRequests).mockResolvedValue([]);
    vi.mocked(apiClient.answerInteraction).mockResolvedValue(undefined);
    vi.mocked(apiClient.getPendingInteractions).mockResolvedValue([]);
    vi.mocked(apiClient.rejectInteraction).mockResolvedValue(undefined);
  });

  it('opens review drawer and submits approval decision', async () => {
    render(
      <MemoryRouter>
        <ApprovalsDashboard auth={adminAuthFixture} />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Approvals')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Review' }));

    const dialog = screen.getByRole('dialog', { name: 'Approval Request' });
    expect(dialog).toBeInTheDocument();
    expect(within(dialog).getByText('Risk Summary')).toBeInTheDocument();
    expect(within(dialog).getByText('High 72')).toBeInTheDocument();
    expect(screen.getAllByText('Policy requires review.').length).toBeGreaterThan(0);
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
    expect(await screen.findByRole('status')).toHaveTextContent('Approval approved');
  });

  it('renders and answers pending interactions from every run', async () => {
    vi.mocked(apiClient.getPendingInteractions).mockResolvedValue([{
      id: 'int-9', runId: 'run-99', workflowName: 'Production deploy', stepId: 'review',
      from: 'reviewer', kind: 'choice', addresseeType: 'human', addressee: null,
      blocking: true, prompt: 'Ship now?', options: ['Ship', 'Wait'], status: 'pending',
      createdAt: new Date(Date.now() - 120_000).toISOString(),
      timeoutAt: new Date(Date.now() + 600_000).toISOString(),
    }]);

    render(<MemoryRouter><ApprovalsDashboard auth={adminAuthFixture} /></MemoryRouter>);

    const inbox = await screen.findByRole('region', { name: 'Pending interactions' });
    expect(within(inbox).getByText('Production deploy')).toBeInTheDocument();
    expect(within(inbox).getByText('Ship now?')).toBeInTheDocument();
    expect(within(inbox).getByText('Blocking')).toBeInTheDocument();
    expect(within(inbox).getByText(/2m old/i)).toBeInTheDocument();
    expect(within(inbox).getByText(/expires in 10m/i)).toBeInTheDocument();
    fireEvent.click(within(inbox).getByRole('button', { name: 'Ship' }));
    await waitFor(() => expect(apiClient.answerInteraction).toHaveBeenCalledWith('run-99', 'int-9', 'Ship'));
  });

  it('treats a 409 race as a neutral already-answered update', async () => {
    vi.mocked(apiClient.getPendingInteractions).mockResolvedValue([{
      id: 'int-9', runId: 'run-99', workflowName: 'Deploy', from: 'reviewer', kind: 'confirm',
      addresseeType: 'human', blocking: true, prompt: 'Deploy?', options: ['approve', 'reject'],
      status: 'pending', createdAt: new Date().toISOString(),
    }]);
    vi.mocked(apiClient.answerInteraction).mockRejectedValue(
      Object.assign(new Error('Already answered via Slack by dana.'), { status: 409 }),
    );

    render(<MemoryRouter><ApprovalsDashboard auth={adminAuthFixture} /></MemoryRouter>);
    const inbox = await screen.findByRole('region', { name: 'Pending interactions' });
    fireEvent.click(within(inbox).getByRole('button', { name: 'Approve' }));

    expect(await within(inbox).findByRole('status')).toHaveTextContent('Already answered via Slack by dana.');
    expect(within(inbox).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows an empty approval queue without a blank review area', async () => {
    vi.mocked(apiClient.getApprovals).mockResolvedValue([]);

    render(
      <MemoryRouter>
        <ApprovalsDashboard auth={adminAuthFixture} />
      </MemoryRouter>,
    );

    expect(await screen.findByText('No pending approvals')).toBeInTheDocument();
    expect(screen.getByText('Nothing needs human review right now.')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Refresh approvals' }).length).toBeGreaterThan(0);
  });

  it('keeps reject validation inside the active review screen', async () => {
    render(
      <MemoryRouter>
        <ApprovalsDashboard auth={adminAuthFixture} />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Approvals')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Review' }));
    fireEvent.click(screen.getByRole('button', { name: 'Reject' }));

    expect(screen.getByRole('dialog', { name: 'Approval Request' })).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Add a comment before rejecting this approval.');
    expect(apiClient.decideApproval).not.toHaveBeenCalled();
  });

  it('filters to decided approvals and shows their status history', async () => {
    render(
      <MemoryRouter>
        <ApprovalsDashboard auth={adminAuthFixture} />
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
        <ApprovalsDashboard auth={adminAuthFixture} />
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
        <ApprovalsDashboard auth={adminAuthFixture} />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Approvals')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Review' }));

    expect(screen.getByRole('dialog', { name: 'Approval Request' })).toBeInTheDocument();
    expect(screen.queryByLabelText('Generated artifact')).not.toBeInTheDocument();
    expect(apiClient.getRunArtifactContent).not.toHaveBeenCalled();
  });

  it('opens approval detail for viewers without decision controls', async () => {
    render(
      <MemoryRouter>
        <ApprovalsDashboard auth={viewerAuthFixture} />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Approvals')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Review' }));

    const dialog = screen.getByRole('dialog', { name: 'Approval Request' });
    expect(dialog).toBeInTheDocument();
    expect(within(dialog).getByText('Approver role required to submit a decision.')).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
  });

  it('lists tool access requests with agent, tool, step, and intent (#202)', async () => {
    vi.mocked(apiClient.getToolAccessRequests).mockResolvedValue([toolAccessFixture]);

    render(
      <MemoryRouter>
        <ApprovalsDashboard auth={adminAuthFixture} />
      </MemoryRouter>,
    );

    const section = await screen.findByRole('region', { name: 'Tool access requests' });
    expect(within(section).getByText('Agent senior-reviewer needs tool github.post_review')).toBeInTheDocument();
    expect(within(section).getByText(/step: Senior Review/)).toBeInTheDocument();
    expect(within(section).getByText('{"pull_number":"23"}')).toBeInTheDocument();
  });

  it('approves a tool access request through the interaction answer endpoint', async () => {
    vi.mocked(apiClient.getToolAccessRequests).mockResolvedValue([toolAccessFixture]);

    render(
      <MemoryRouter>
        <ApprovalsDashboard auth={adminAuthFixture} />
      </MemoryRouter>,
    );

    const section = await screen.findByRole('region', { name: 'Tool access requests' });
    fireEvent.click(within(section).getByRole('button', { name: 'Approve for this run' }));

    await waitFor(() => {
      expect(apiClient.answerInteraction).toHaveBeenCalledWith('run-0430', 'int-42', 'approve');
    });
  });

  it('requires guidance text before denying, and sends it as the answer', async () => {
    vi.mocked(apiClient.getToolAccessRequests).mockResolvedValue([toolAccessFixture]);

    render(
      <MemoryRouter>
        <ApprovalsDashboard auth={adminAuthFixture} />
      </MemoryRouter>,
    );

    const section = await screen.findByRole('region', { name: 'Tool access requests' });
    const denyButton = within(section).getByRole('button', { name: 'Deny with guidance' });
    expect(denyButton).toBeDisabled();

    fireEvent.change(within(section).getByLabelText('Guidance for the agent (sent as a denial)'), {
      target: { value: 'Comment on the issue instead.' },
    });
    fireEvent.click(denyButton);

    await waitFor(() => {
      expect(apiClient.answerInteraction).toHaveBeenCalledWith('run-0430', 'int-42', 'Comment on the issue instead.');
    });
  });

  it('fails the step when the operator chooses Fail step', async () => {
    vi.mocked(apiClient.getToolAccessRequests).mockResolvedValue([toolAccessFixture]);

    render(
      <MemoryRouter>
        <ApprovalsDashboard auth={adminAuthFixture} />
      </MemoryRouter>,
    );

    const section = await screen.findByRole('region', { name: 'Tool access requests' });
    fireEvent.click(within(section).getByRole('button', { name: 'Fail step' }));

    await waitFor(() => {
      expect(apiClient.answerInteraction).toHaveBeenCalledWith('run-0430', 'int-42', 'fail');
    });
  });
});
