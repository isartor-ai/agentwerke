import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { apiClient } from '../api/client';
import { RunBoard } from '../views/RunBoard';
import { firstRunWorkflowFixture, operatorAuthFixture, runsFixture, viewerAuthFixture, workflowsFixture } from './fixtures';

vi.mock('../api/client', () => ({
  apiClient: {
    getRuns: vi.fn(),
    getWorkflows: vi.fn(),
    startRun: vi.fn(),
  },
}));

describe('RunBoard integration', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getRuns).mockResolvedValue(runsFixture);
    vi.mocked(apiClient.getWorkflows).mockResolvedValue(workflowsFixture);
    vi.mocked(apiClient.startRun).mockResolvedValue({ runId: 'run-first-run-sample' });
  });

  it('loads runs, filters by status, and navigates to detail', async () => {
    render(
      <MemoryRouter initialEntries={['/runs']}>
        <RunBoard auth={operatorAuthFixture} />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Runs')).toBeInTheDocument();
    expect(screen.getByText('run-0421')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Running' }));

    await waitFor(() => {
      expect(screen.queryByText('run-0421')).not.toBeInTheDocument();
      expect(screen.getByText('run-0420')).toBeInTheDocument();
    });
  });

  it('keeps existing run data visible and shows a toast when refresh fails', async () => {
    vi.mocked(apiClient.getRuns)
      .mockResolvedValueOnce(runsFixture)
      .mockRejectedValueOnce(new Error('runtime unavailable'));

    render(
      <MemoryRouter initialEntries={['/runs']}>
        <RunBoard auth={operatorAuthFixture} />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Runs')).toBeInTheDocument();
    expect(screen.getByText('run-0421')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Sync' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Run refresh failed');
    expect(screen.getByText('runtime unavailable')).toBeInTheDocument();
    expect(screen.getByText('run-0421')).toBeInTheDocument();
    expect(screen.queryByText('Unable to load data')).not.toBeInTheDocument();
  });

  it('shows an explicit empty state when no runs exist', async () => {
    vi.mocked(apiClient.getRuns).mockResolvedValue([]);

    render(
      <MemoryRouter initialEntries={['/runs']}>
        <RunBoard auth={operatorAuthFixture} />
      </MemoryRouter>,
    );

    expect(await screen.findByText('No runs have started yet')).toBeInTheDocument();
    expect(screen.getByText('Start from a workflow to create the first monitored run.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open Workflows' })).toBeInTheDocument();
  });

  it('starts the seeded first-run sample from the empty run board', async () => {
    vi.mocked(apiClient.getRuns).mockResolvedValue([]);
    vi.mocked(apiClient.getWorkflows).mockResolvedValue([firstRunWorkflowFixture]);

    render(
      <MemoryRouter initialEntries={['/runs']}>
        <RunBoard auth={operatorAuthFixture} />
      </MemoryRouter>,
    );

    expect(await screen.findByRole('heading', { name: 'Run your first workflow' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Run sample workflow' }));

    await waitFor(() => {
      expect(apiClient.startRun).toHaveBeenCalledWith('wf-first-run-sample');
    });
  });

  it('does not offer run start controls to viewers', async () => {
    vi.mocked(apiClient.getRuns).mockResolvedValue([]);
    vi.mocked(apiClient.getWorkflows).mockResolvedValue([firstRunWorkflowFixture]);

    render(
      <MemoryRouter initialEntries={['/runs']}>
        <RunBoard auth={viewerAuthFixture} />
      </MemoryRouter>,
    );

    const runButton = await screen.findByRole('button', { name: 'Run sample workflow' });
    expect(runButton).toBeDisabled();
    expect(screen.getAllByRole('button', { name: /View Workflows/i }).length).toBeGreaterThan(0);
  });
});
