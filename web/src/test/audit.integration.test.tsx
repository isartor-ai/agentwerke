import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { apiClient } from '../api/client';
import type { AuditEntry } from '../types';
import { Audit } from '../views/Audit';

vi.mock('../api/client', () => ({
  apiClient: {
    getAuditEntries: vi.fn(),
  },
}));

const entriesFixture: AuditEntry[] = [
  {
    id: 'aud-1',
    runId: 'run-42',
    timestamp: '2026-06-30T10:00:00.000Z',
    actorType: 'user',
    actor: 'alice',
    action: 'approval.decision',
    resourceType: 'approval',
    resourceId: 'apr-1',
    outcome: 'success',
    details: null,
  },
  {
    id: 'aud-2',
    runId: 'run-42',
    timestamp: '2026-06-30T09:59:00.000Z',
    actorType: 'agent',
    actor: 'reviewer-agent',
    action: 'github.create_pull_request',
    resourceType: 'pull_request',
    resourceId: '7',
    outcome: 'success',
    details: null,
  },
];

function renderAudit(initialEntry = '/audit') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Audit />
    </MemoryRouter>,
  );
}

describe('Audit', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.getAuditEntries).mockResolvedValue(entriesFixture);
  });

  it('lists recent audit records', async () => {
    renderAudit();

    expect(await screen.findByText('approval.decision')).toBeInTheDocument();
    expect(screen.getByText('github.create_pull_request')).toBeInTheDocument();
    await waitFor(() => expect(apiClient.getAuditEntries).toHaveBeenCalledWith({ runId: undefined, limit: 200 }));
  });

  it('filters the decision trace by run id', async () => {
    renderAudit();
    await screen.findByText('approval.decision');

    fireEvent.change(screen.getByPlaceholderText('Filter by run to see its decision trace'), {
      target: { value: 'run-42' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Search' }));

    await waitFor(() => expect(apiClient.getAuditEntries).toHaveBeenCalledWith({ runId: 'run-42', limit: 200 }));
    expect(await screen.findByText('Decision trace — run-42')).toBeInTheDocument();
  });

  it('pages audit records ten rows at a time', async () => {
    const pagedEntries = Array.from({ length: 12 }, (_, index) => ({
      ...entriesFixture[0],
      id: `aud-page-${String(index + 1).padStart(2, '0')}`,
      action: `audit.action.${String(index + 1).padStart(2, '0')}`,
    }));
    vi.mocked(apiClient.getAuditEntries).mockResolvedValue(pagedEntries);

    renderAudit();

    const table = await screen.findByRole('table');
    expect(within(table).getAllByRole('row')).toHaveLength(11);
    expect(within(table).getByText('audit.action.01')).toBeInTheDocument();
    expect(within(table).queryByText('audit.action.11')).not.toBeInTheDocument();
    expect(screen.getByText('1–10 of 12 audit records')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous page' })).toBeDisabled();

    fireEvent.click(screen.getByRole('button', { name: 'Next page' }));

    await waitFor(() => {
      expect(within(table).getAllByRole('row')).toHaveLength(3);
      expect(within(table).getByText('audit.action.11')).toBeInTheDocument();
      expect(within(table).queryByText('audit.action.01')).not.toBeInTheDocument();
      expect(screen.getByText('11–12 of 12 audit records')).toBeInTheDocument();
    });

    expect(screen.getByRole('button', { name: 'Next page' })).toBeDisabled();
  });
});
