import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { apiClient } from '../api/client';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import { Pagination } from '../components/Pagination';
import type { AuditEntry } from '../types';

const AUDIT_PAGE_SIZE = 10;

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
  const [searchParams, setSearchParams] = useSearchParams();

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

  const list = entries ?? [];
  const requestedPage = Number.parseInt(searchParams.get('page') ?? '1', 10);
  const normalizedPage = Number.isFinite(requestedPage) && requestedPage > 0 ? requestedPage : 1;
  const pageCount = Math.max(1, Math.ceil(list.length / AUDIT_PAGE_SIZE));
  const page = Math.min(normalizedPage, pageCount);
  const pageEntries = list.slice((page - 1) * AUDIT_PAGE_SIZE, page * AUDIT_PAGE_SIZE);

  const setPage = useCallback((nextPage: number) => {
    setSearchParams((previous) => {
      const next = new URLSearchParams(previous);
      if (nextPage <= 1) {
        next.delete('page');
      } else {
        next.set('page', String(nextPage));
      }
      return next;
    });
  }, [setSearchParams]);

  useEffect(() => {
    if (loading || list.length === 0 || requestedPage === page) {
      return;
    }

    setPage(page);
  }, [list.length, loading, page, requestedPage, setPage]);

  const search = () => {
    setAppliedRunId(runId.trim() || undefined);
    setPage(1);
  };
  const clear = () => {
    setRunId('');
    setAppliedRunId(undefined);
    setPage(1);
  };

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
        <article className="panel audit-records">
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
                {pageEntries.map((entry) => (
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
          <Pagination
            currentPage={page}
            pageSize={AUDIT_PAGE_SIZE}
            totalItems={list.length}
            itemLabel="audit records"
            onPageChange={setPage}
          />
        </article>
      )}
    </section>
  );
}
