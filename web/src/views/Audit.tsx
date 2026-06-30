import { useEffect, useState } from 'react';
import { apiClient } from '../api/client';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import type { AuditEntry } from '../types';

function outcomeClass(outcome: string): string {
  switch (outcome.toLowerCase()) {
    case 'success':
      return 'badge badge-success';
    case 'failure':
    case 'rejected':
      return 'validation-error';
    case 'escalated':
      return 'badge badge-warning';
    default:
      return 'badge';
  }
}

export function Audit() {
  const [entries, setEntries] = useState<AuditEntry[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [runId, setRunId] = useState('');
  const [appliedRunId, setAppliedRunId] = useState<string | undefined>(undefined);

  const load = async (filterRunId?: string) => {
    setLoading(true);
    setError(null);
    try {
      setEntries(await apiClient.getAuditEntries({ runId: filterRunId, limit: 200 }));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load the audit trail.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load(appliedRunId);
  }, [appliedRunId]);

  const search = () => setAppliedRunId(runId.trim() || undefined);
  const clear = () => {
    setRunId('');
    setAppliedRunId(undefined);
  };

  const list = entries ?? [];

  return (
    <section>
      <PageHeader
        title="Audit"
        description="Immutable audit records and per-run decision trace."
        actions={
          <button type="button" className="btn btn-secondary" onClick={() => void load(appliedRunId)}>
            Refresh
          </button>
        }
      />

      <article className="panel">
        <div className="form-grid">
          <label>
            Run ID
            <input
              value={runId}
              onChange={(event) => setRunId(event.target.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  search();
                }
              }}
              placeholder="Filter by run to see its decision trace"
            />
          </label>
        </div>
        <button type="button" className="btn btn-primary" onClick={search}>
          Search
        </button>
        {appliedRunId ? (
          <button type="button" className="btn btn-secondary" onClick={clear}>
            Clear
          </button>
        ) : null}
      </article>

      {loading ? (
        <LoadingState message="Loading audit trail..." />
      ) : error ? (
        <ErrorState message={error} onRetry={() => void load(appliedRunId)} />
      ) : list.length === 0 ? (
        <EmptyState
          title="No audit records"
          description={appliedRunId ? `No audit records for run ${appliedRunId}.` : 'No audit records yet.'}
        />
      ) : (
        <article className="panel">
          <h2>{appliedRunId ? `Decision trace — ${appliedRunId}` : 'Recent audit records'}</h2>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Time</th>
                  <th>Actor</th>
                  <th>Action</th>
                  <th>Resource</th>
                  <th>Outcome</th>
                </tr>
              </thead>
              <tbody>
                {list.map((entry) => (
                  <tr key={entry.id}>
                    <td>
                      <span className="muted">{entry.timestamp}</span>
                    </td>
                    <td>
                      {entry.actor}
                      <br />
                      <span className="muted">{entry.actorType}</span>
                    </td>
                    <td>{entry.action}</td>
                    <td>
                      {entry.resourceType
                        ? `${entry.resourceType}: ${entry.resourceId ?? ''}`
                        : entry.resourceId ?? '—'}
                    </td>
                    <td>
                      <span className={outcomeClass(entry.outcome)}>{entry.outcome}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </article>
      )}
    </section>
  );
}
