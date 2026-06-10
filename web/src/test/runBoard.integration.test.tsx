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
});
