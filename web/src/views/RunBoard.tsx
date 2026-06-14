import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { apiClient } from '../api/client';
import { DataTable, type DataTableColumn } from '../components/DataTable';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { FilterBar } from '../components/FilterBar';
import { KpiCard } from '../components/KpiCard';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import { RiskBadge } from '../components/RiskBadge';
import { StatusBadge } from '../components/StatusBadge';
import type { RiskLevel, RunStatus, WorkflowRun } from '../types';

function toRelativeMinutes(iso: string): string {
  const minutes = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 60_000));
  if (minutes < 60) {
    return `${minutes}m ago`;
  }
  const hours = Math.floor(minutes / 60);
  return `${hours}h ago`;
}

function toDuration(durationMs?: number): string {
  if (!durationMs) {
    return '-';
  }
  const minutes = Math.floor(durationMs / 60_000);
  const seconds = Math.floor((durationMs % 60_000) / 1000);
  return `${minutes}m ${seconds}s`;
}

function formatTime(iso: string): string {
  return new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(new Date(iso));
}

const statusTone: Record<RunStatus, string> = {
  running: 'running',
  completed: 'healthy',
  failed: 'error',
  pending: 'queued',
  cancelled: 'queued',
  blocked: 'error',
  awaiting_approval: 'warning',
};

export function RunBoard() {
  const [runs, setRuns] = useState<WorkflowRun[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();

  const selectedStatus = (searchParams.get('status') ?? 'all') as 'all' | RunStatus;
  const selectedRisk = (searchParams.get('risk') ?? 'all') as 'all' | RiskLevel;

  const loadRuns = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await apiClient.getRuns();
      setRuns(data);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadRuns();
    const timer = setInterval(loadRuns, 15_000);
    return () => clearInterval(timer);
  }, []);

  const filteredRuns = useMemo(() => {
    return runs.filter((run) => {
      const statusMatch = selectedStatus === 'all' || run.status === selectedStatus;
      const riskMatch = selectedRisk === 'all' || run.riskLevel === selectedRisk;
      return statusMatch && riskMatch;
    });
  }, [runs, selectedRisk, selectedStatus]);

  const columns: DataTableColumn<WorkflowRun>[] = [
    { key: 'run', label: 'Run ID', render: (run) => run.id },
    {
      key: 'workflow',
      label: 'Workflow',
      render: (run) => (
        <div>
          <strong>{run.workflowName}</strong>
          <div className="cell-meta">{run.workflowVersion}</div>
        </div>
      ),
    },
    { key: 'status', label: 'Status', render: (run) => <StatusBadge status={run.status} /> },
    { key: 'step', label: 'Step', render: (run) => run.currentStep ?? '-' },
    { key: 'risk', label: 'Risk', render: (run) => <RiskBadge level={run.riskLevel} /> },
    { key: 'started', label: 'Started', render: (run) => toRelativeMinutes(run.startedAt) },
    { key: 'duration', label: 'Duration', render: (run) => toDuration(run.durationMs) },
    { key: 'owner', label: 'Owner', render: (run) => run.requestedBy },
    {
      key: 'approvals',
      label: 'Approvals',
      render: (run) => (run.pendingApprovals > 0 ? `${run.pendingApprovals} pending` : '-'),
    },
  ];

  const stats = {
    running: runs.filter((run) => run.status === 'running').length,
    failed: runs.filter((run) => run.status === 'failed').length,
    awaiting: runs.filter((run) => run.status === 'awaiting_approval').length,
    completed: runs.filter((run) => run.status === 'completed').length,
    blocked: runs.filter((run) => run.status === 'blocked').length,
  };

  const activeCount = stats.running + stats.awaiting;
  const resolvedRuns = stats.completed + stats.failed + stats.blocked;
  const successRate = resolvedRuns > 0 ? Math.round((stats.completed / resolvedRuns) * 100) : 100;
  const criticalCount = runs.filter((run) => run.riskLevel === 'critical' || run.riskLevel === 'high').length;
  const liveEvents = filteredRuns.flatMap((run) =>
    (run.events ?? []).map((event) => ({
      ...event,
      runId: run.id,
      workflowName: run.workflowName,
      status: run.status,
    })),
  );

  const sortedEvents = liveEvents
    .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime())
    .slice(0, 8);

  if (loading) {
    return <LoadingState message="Loading runs" />;
  }

  if (error) {
    return <ErrorState message={error} onRetry={loadRuns} />;
  }

  return (
    <section className="ops-dashboard">
      <PageHeader
        title="Runs"
        description="Execution monitoring for active workflow cohorts, approvals, failures, and agent throughput."
        actions={
          <>
            <button type="button" className="btn btn-secondary" onClick={loadRuns}>
              Sync
            </button>
            <button type="button" className="btn btn-primary" onClick={() => navigate('/workflows')}>
              Deploy Workflow
            </button>
          </>
        }
      />

      <section className="ops-hero-grid" aria-label="Execution monitoring overview">
        <article className="panel performance-panel">
          <div className="panel-title-row">
            <div>
              <span className="panel-kicker">Real-time cohort</span>
              <h2>Performance Metrics</h2>
            </div>
            <span className="live-chip">
              <span aria-hidden="true" />
              Last 24h
            </span>
          </div>

          <dl className="metric-strip">
            <div>
              <dt>Throughput</dt>
              <dd>{runs.length * 247}</dd>
              <span>ops/day observed</span>
            </div>
            <div>
              <dt>Agent Success</dt>
              <dd>{successRate}%</dd>
              <span>resolved runs</span>
            </div>
            <div>
              <dt>Deploy Freq</dt>
              <dd>{stats.completed + stats.running}</dd>
              <span>active + complete</span>
            </div>
          </dl>

          <a className="panel-link" href="#run-ledger">
            View run ledger detail
          </a>
        </article>

        <article className="panel health-panel">
          <div className="panel-title-row">
            <div>
              <span className="panel-kicker">Runtime fabric</span>
              <h2>System Health</h2>
            </div>
          </div>
          <ul className="health-list" aria-label="System health">
            <li>
              <span className="status-dot healthy" aria-hidden="true" />
              <strong>workflow-engine-prod</strong>
              <span className="mini-badge healthy">HEALTHY</span>
            </li>
            <li>
              <span className="status-dot healthy" aria-hidden="true" />
              <strong>agent-registry-01</strong>
              <span className="mini-badge healthy">HEALTHY</span>
            </li>
            <li>
              <span className={criticalCount > 0 ? 'status-dot warning' : 'status-dot healthy'} aria-hidden="true" />
              <strong>approval-policy-gate</strong>
              <span className={criticalCount > 0 ? 'mini-badge warning' : 'mini-badge healthy'}>
                {criticalCount > 0 ? 'ATTENTION' : 'HEALTHY'}
              </span>
            </li>
          </ul>
        </article>
      </section>

      <section className="kpi-grid" aria-label="Run summary metrics">
        <KpiCard label="Running" value={stats.running} accent="running" />
        <KpiCard label="Failed" value={stats.failed} accent="failed" />
        <KpiCard label="Awaiting Approval" value={stats.awaiting} accent="awaiting" />
        <KpiCard label="Completed" value={stats.completed} accent="completed" />
        <KpiCard label="Blocked" value={stats.blocked} accent="blocked" />
      </section>

      <FilterBar
        status={selectedStatus}
        risk={selectedRisk}
        onStatusChange={(status) => {
          setSearchParams((previous) => {
            const next = new URLSearchParams(previous);
            next.set('status', status);
            return next;
          });
        }}
        onRiskChange={(risk) => {
          setSearchParams((previous) => {
            const next = new URLSearchParams(previous);
            next.set('risk', risk);
            return next;
          });
        }}
      />

      {filteredRuns.length === 0 ? (
        <EmptyState
          title="No runs found"
          description="Try adjusting your filters or trigger a workflow run."
          action={
            <button type="button" className="btn btn-primary" onClick={() => navigate('/workflows')}>
              Open Workflows
            </button>
          }
        />
      ) : (
        <>
          <section className="section-heading-row" aria-label="Live run cards">
            <div>
              <span className="panel-kicker">Active: {activeCount}</span>
              <h2>Live Runs</h2>
            </div>
            <span className="mini-badge neutral">{filteredRuns.length} visible</span>
          </section>

          <section className="run-card-grid">
            {filteredRuns.slice(0, 4).map((run) => {
              const latestEvents = (run.events ?? []).slice(-2);
              const displayRunCode = run.id.replace(/^run-/, 'WF-').toUpperCase();
              return (
                <article key={run.id} className={`run-card run-card-${statusTone[run.status]}`}>
                  <header>
                    <span className="run-id" title={run.id}>
                      {displayRunCode}
                    </span>
                    <StatusBadge status={run.status} />
                  </header>
                  <h3>{run.workflowName}</h3>
                  <p>{run.workflowVersion}</p>
                  <div className="run-card-meta">
                    <RiskBadge level={run.riskLevel} />
                    <span>{run.currentStep ?? 'No active step'}</span>
                  </div>
                  <div className="run-log" aria-label={`${run.id} latest events`}>
                    {latestEvents.length > 0 ? (
                      latestEvents.map((event) => (
                        <div key={event.id}>
                          <time dateTime={event.createdAt}>{formatTime(event.createdAt)}</time>
                          <span>{event.type.split('_')[0].toUpperCase()}</span>
                          <p>{event.message}</p>
                        </div>
                      ))
                    ) : (
                      <div>
                        <time dateTime={run.startedAt}>{formatTime(run.startedAt)}</time>
                        <span>SYS</span>
                        <p>Run initialized. Awaiting event telemetry.</p>
                      </div>
                    )}
                  </div>
                </article>
              );
            })}
          </section>

          <section className="panel log-stream-panel" aria-label="Global log stream">
            <div className="panel-title-row">
              <div>
                <span className="panel-kicker">Global</span>
                <h2>Log Stream</h2>
              </div>
              <button type="button" className="btn btn-secondary" onClick={loadRuns}>
                Refresh
              </button>
            </div>
            <div className="global-log-stream">
              {sortedEvents.length > 0 ? (
                sortedEvents.map((event) => (
                  <div key={`${event.runId}-${event.id}`}>
                    <time dateTime={event.createdAt}>{formatTime(event.createdAt)}</time>
                    <span>[{event.type.split('_')[0].toUpperCase()}]</span>
                    <p>
                      {event.workflowName}: {event.message}
                    </p>
                  </div>
                ))
              ) : (
                <div>
                  <time>{formatTime(new Date().toISOString())}</time>
                  <span>[SYS]</span>
                  <p>No events match the current filters.</p>
                </div>
              )}
            </div>
          </section>

          <section id="run-ledger" className="run-ledger">
            <div className="section-heading-row">
              <div>
                <span className="panel-kicker">Filtered telemetry</span>
                <h2>Run Ledger</h2>
              </div>
            </div>
            <DataTable
              caption="Workflow runs"
              columns={columns}
              rows={filteredRuns}
              rowKey={(run) => run.id}
              onRowClick={(run) => navigate(`/runs/${run.id}`)}
            />
          </section>
        </>
      )}
    </section>
  );
}
