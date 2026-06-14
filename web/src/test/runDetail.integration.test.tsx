import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { apiClient } from '../api/client';
import { RunDetail } from '../views/RunDetail';
import { runsFixture } from './fixtures';

vi.mock('../api/client', () => ({
  apiClient: {
    getRun: vi.fn(),
    getRunArtifactDownloadUrl: vi.fn(),
  },
}));

describe('RunDetail integration', () => {
  beforeEach(() => {
    vi.mocked(apiClient.getRun).mockResolvedValue(runsFixture[0]);
    vi.mocked(apiClient.getRunArtifactDownloadUrl).mockImplementation(
      (runId, artifactName) => `/api/runs/${runId}/artifacts/${artifactName}`,
    );
  });

  it('renders live run detail tabs with artifacts and approvals', async () => {
    render(
      <MemoryRouter initialEntries={['/runs/run-0421']}>
        <Routes>
          <Route path="/runs/:runId" element={<RunDetail />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('Run run-0421')).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Summary' })).toBeInTheDocument();
    expect(screen.getByText('BPMN Graph')).toBeInTheDocument();
    expect(screen.getByText('Runtime Events')).toBeInTheDocument();
    expect(screen.getByText('Retry scheduled after transient failure.')).toBeInTheDocument();
    expect(screen.getByText('Timeout boundary triggered on security scan.')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: 'Policy' }));
    expect(screen.getAllByText('Requires human approval.').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole('tab', { name: 'Artifacts' }));
    expect(screen.getByText('scan-report.json')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download' })).toHaveAttribute(
      'href',
      '/api/runs/run-0421/artifacts/scan-report.json',
    );

    fireEvent.click(screen.getByRole('tab', { name: 'Approvals' }));
    expect(screen.getByText('Merge branch feature/auth-refactor to main')).toBeInTheDocument();

    fireEvent.click(
      screen.getByRole('button', {
        name: 'Cancel run and stop further execution',
      }),
    );
    expect(screen.getByRole('dialog', { name: 'Cancel this run?' })).toBeInTheDocument();
  });
});
