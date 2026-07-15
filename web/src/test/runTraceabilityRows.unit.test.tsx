import { render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RunTraceabilityRows } from '../components/RunTraceabilityRows';
import { apiClient } from '../api/client';
import type { TraceabilityRow } from '../types';

const row = (overrides: Partial<TraceabilityRow> = {}): TraceabilityRow => ({
  requirementProvider: 'github',
  requirementId: '42',
  requirementUrl: 'https://github.com/octo/app/issues/42',
  testId: 'Example.Tests.OrderTests.CreatesOrder',
  testName: 'CreatesOrder',
  ciRunId: '17482',
  ciRunUrl: 'https://github.com/octo/app/actions/runs/17482',
  status: 'passed',
  evidenceArtifact: 'verification-step-ingest-junit.xml',
  failureMessage: null,
  ...overrides,
});

describe('RunTraceabilityRows', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('links the requirement and CI run out to their systems of record', async () => {
    vi.spyOn(apiClient, 'getRunTraceability').mockResolvedValue({ runId: 'run_1', rows: [row()] });

    render(<RunTraceabilityRows runId="run_1" />);

    const requirement = await screen.findByRole('link', { name: 'github #42' });
    expect(requirement).toHaveAttribute('href', 'https://github.com/octo/app/issues/42');

    // The point of the row: both ids resolve to real external records, not authored text.
    const ciRun = screen.getByRole('link', { name: '#17482' });
    expect(ciRun).toHaveAttribute('href', 'https://github.com/octo/app/actions/runs/17482');

    expect(screen.getByText('CreatesOrder')).toBeInTheDocument();
    expect(screen.getByText('Example.Tests.OrderTests.CreatesOrder')).toBeInTheDocument();
    expect(screen.getByText('verification-step-ingest-junit.xml')).toBeInTheDocument();
  });

  it('shows a failing case with its failure message', async () => {
    vi.spyOn(apiClient, 'getRunTraceability').mockResolvedValue({
      runId: 'run_1',
      rows: [
        row(),
        row({
          testId: 'Example.Tests.OrderTests.RejectsEmptyCart',
          testName: 'RejectsEmptyCart',
          status: 'failed',
          failureMessage: 'expected 400, got 200',
        }),
      ],
    });

    const { container } = render(<RunTraceabilityRows runId="run_1" />);

    expect(await screen.findByText('expected 400, got 200')).toBeInTheDocument();

    // The counts are interleaved with <strong>, so assert on the rendered sentence.
    const summary = container.querySelector('.traceability-summary');
    expect(summary?.textContent).toContain('2 test cases');
    expect(summary?.textContent).toContain('1 failing');
  });

  /**
   * A run that has not reached verification is not the same as a run whose verification found
   * nothing — an empty table would read as the latter.
   */
  it('explains an empty result rather than rendering an empty table', async () => {
    vi.spyOn(apiClient, 'getRunTraceability').mockResolvedValue({ runId: 'run_1', rows: [] });

    render(<RunTraceabilityRows runId="run_1" />);

    expect(await screen.findByText(/No verified rows yet/)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  /**
   * An id rendered as a link that resolves nowhere is precisely the failure the matrix exists to
   * rule out, so a row without a URL must not look clickable.
   */
  it('does not render a requirement without a URL as a link', async () => {
    vi.spyOn(apiClient, 'getRunTraceability').mockResolvedValue({
      runId: 'run_1',
      rows: [row({ requirementUrl: null, ciRunUrl: null })],
    });

    render(<RunTraceabilityRows runId="run_1" />);

    expect(await screen.findByText('github #42')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'github #42' })).not.toBeInTheDocument();
  });

  it('renders results with no requirement link rather than hiding them', async () => {
    vi.spyOn(apiClient, 'getRunTraceability').mockResolvedValue({
      runId: 'run_1',
      rows: [row({ requirementProvider: null, requirementId: null, requirementUrl: null })],
    });

    render(<RunTraceabilityRows runId="run_1" />);

    // The tests demonstrably ran; the missing requirement shows as a gap, not as a hidden row.
    const table = await screen.findByRole('table');
    expect(within(table).getByText('CreatesOrder')).toBeInTheDocument();
  });

  it('surfaces a load failure instead of looking like an unverified run', async () => {
    vi.spyOn(apiClient, 'getRunTraceability').mockRejectedValue(new Error('boom'));

    render(<RunTraceabilityRows runId="run_1" />);

    await waitFor(() => expect(screen.getByText('boom')).toBeInTheDocument());
    expect(screen.queryByText(/No verified rows yet/)).not.toBeInTheDocument();
  });
});
