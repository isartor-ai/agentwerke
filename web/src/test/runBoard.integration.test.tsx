import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { apiClient } from '../api/client';
import { RunBoard } from '../views/RunBoard';
import { runsFixture } from './fixtures';

vi.mock('../api/client', () => ({
  apiClient: {
    getRuns: vi.fn(),
  },
}));

describe('RunBoard integration', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getRuns).mockResolvedValue(runsFixture);
  });

  it('loads runs, filters by status, and navigates to detail', async () => {
    render(
      <MemoryRouter initialEntries={['/runs']}>
        <RunBoard />
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
        <RunBoard />
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
        <RunBoard />
      </MemoryRouter>,
    );

    expect(await screen.findByText('No runs have started yet')).toBeInTheDocument();
    expect(screen.getByText('Start from a workflow to create the first monitored run.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open Workflows' })).toBeInTheDocument();
  });
});
