import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { apiClient } from '../api/client';
import { RunDetail } from '../views/RunDetail';
import { runsFixture } from './fixtures';

vi.mock('../api/client', () => ({
  apiClient: {
    getRun: vi.fn(),
  },
}));

describe('RunDetail integration', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getRun).mockResolvedValue(runsFixture[0]);
  });

  it('renders run detail tabs and opens cancel dialog', async () => {
    render(
      <MemoryRouter initialEntries={['/runs/run-0421']}>
        <Routes>
          <Route path="/runs/:runId" element={<RunDetail />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Summary' })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Policy' }));
    expect(screen.getByText('Content for Policy will be expanded in later phases.')).toBeInTheDocument();

    fireEvent.click(
      screen.getByRole('button', {
        name: 'Cancel run and stop further execution',
      }),
    );
    expect(screen.getByRole('dialog', { name: 'Cancel this run?' })).toBeInTheDocument();
  });
});
