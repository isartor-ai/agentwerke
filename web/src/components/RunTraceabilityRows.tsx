import { useEffect, useState } from 'react';
import { apiClient } from '../api/client';
import type { TraceabilityRow } from '../types';

interface RunTraceabilityRowsProps {
  runId: string;
}

const STATUS_TONE: Record<string, string> = {
  passed: 'chip-success',
  failed: 'chip-danger',
  error: 'chip-danger',
  skipped: 'chip-pending',
};

/**
 * The run's evidence-backed traceability rows (#210) — the counterpart to the BPMN-derived matrix
 * below it. That one shows what the workflow *declares*; this shows what the run actually did, with
 * every external field resolving to a record a reader can open.
 */
export function RunTraceabilityRows({ runId }: RunTraceabilityRowsProps) {
  const [rows, setRows] = useState<TraceabilityRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setRows(null);
    setError(null);

    apiClient
      .getRunTraceability(runId)
      .then((result) => {
        if (!cancelled) setRows(result.rows);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause instanceof Error ? cause.message : 'Could not load traceability rows.');
      });

    return () => {
      cancelled = true;
    };
  }, [runId]);

  if (error) {
    return (
      <section className="traceability" aria-label="Verified traceability">
        <p className="cell-meta">{error}</p>
      </section>
    );
  }

  if (rows === null) {
    return (
      <section className="traceability" aria-label="Verified traceability">
        <p className="cell-meta">Loading traceability rows…</p>
      </section>
    );
  }

  if (rows.length === 0) {
    // A run that has not reached verification has no rows. Say that, rather than rendering an empty
    // table that reads as "nothing was verified" for a run that simply has not got there yet.
    return (
      <section className="traceability" aria-label="Verified traceability">
        <h3 className="traceability-heading">Verified traceability</h3>
        <p className="cell-meta">
          No verified rows yet. Rows appear once a run has read a requirement and ingested a CI
          test result.
        </p>
      </section>
    );
  }

  const failed = rows.filter((row) => row.status === 'failed' || row.status === 'error').length;

  return (
    <section className="traceability" aria-label="Verified traceability">
      <h3 className="traceability-heading">Verified traceability</h3>
      <p className="cell-meta traceability-summary">
        <strong>{rows.length}</strong> test {rows.length === 1 ? 'case' : 'cases'} traced to an
        external requirement and CI run
        {failed > 0 ? <>, <strong>{failed}</strong> failing</> : null}.
      </p>

      <div className="table-wrap">
        <table className="traceability-matrix">
          <thead>
            <tr>
              <th scope="col">Requirement</th>
              <th scope="col">Test</th>
              <th scope="col">CI run</th>
              <th scope="col">Status</th>
              <th scope="col">Evidence</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={`${row.requirementId ?? 'none'}:${row.testId}`}>
                <td>{renderRequirement(row)}</td>
                <td>
                  <strong>{row.testName}</strong>
                  <span className="cell-meta traceability-detail">{row.testId}</span>
                </td>
                <td>{renderCiRun(row)}</td>
                <td>
                  <span className={`chip chip-static ${STATUS_TONE[row.status] ?? ''}`}>{row.status}</span>
                  {row.failureMessage ? (
                    <span className="cell-meta traceability-detail" title={row.failureMessage}>
                      {row.failureMessage}
                    </span>
                  ) : null}
                </td>
                <td>
                  {row.evidenceArtifact ? (
                    <span className="cell-meta">{row.evidenceArtifact}</span>
                  ) : (
                    <span className="cell-meta">—</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/**
 * The requirement id links out to its system of record. Without a URL the id is shown plain rather
 * than as a dead link — an id that looks clickable but resolves nowhere is the failure this whole
 * matrix exists to rule out.
 */
function renderRequirement(row: TraceabilityRow) {
  if (!row.requirementId) {
    return <span className="cell-meta">—</span>;
  }

  const label = row.requirementProvider ? `${row.requirementProvider} #${row.requirementId}` : row.requirementId;

  return row.requirementUrl ? (
    <a href={row.requirementUrl} target="_blank" rel="noreferrer">
      {label}
    </a>
  ) : (
    <span>{label}</span>
  );
}

function renderCiRun(row: TraceabilityRow) {
  if (!row.ciRunUrl) {
    return <span className="cell-meta">—</span>;
  }

  return (
    <a href={row.ciRunUrl} target="_blank" rel="noreferrer">
      {row.ciRunId ? `#${row.ciRunId}` : 'CI run'}
    </a>
  );
}
