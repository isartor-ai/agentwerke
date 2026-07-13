import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { apiClient } from '../api/client';
import { canOperate } from '../auth/permissions';
import { DataTable, type DataTableColumn } from '../components/DataTable';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { FilterBar } from '../components/FilterBar';
import { KpiCard } from '../components/KpiCard';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import { Pagination } from '../components/Pagination';
import { RiskBadge } from '../components/RiskBadge';
import { StatusBadge } from '../components/StatusBadge';
import { ToastRegion } from '../components/ToastRegion';
import { useToastQueue } from '../components/useToastQueue';
import type { AuthState, RiskLevel, RunStatus, Workflow, WorkflowRun } from '../types';

const FIRST_RUN_SAMPLE_WORKFLOW_ID = 'wf-first-run-sample';
const RUN_LEDGER_PAGE_SIZE = 10;

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

function toPercent(value: number, total: number): number {
  return total > 0 ? Math.round((value / total) * 100) : 100;
}

function toP95Duration(runs: WorkflowRun[]): string {
  const durations = runs
    .map((run) => run.durationMs)
    .filter((duration): duration is number => typeof duration === 'number')
    .sort((a, b) => a - b);

  if (durations.length === 0) {
    return '-';
  }

  const index = Math.max(0, Math.ceil(durations.length * 0.95) - 1);
  return toDuration(durations[index]);
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
  waiting_external: 'warning',
  needs_config: 'warning',
};

interface RunBoardProps {
  auth: AuthState;
}

export function RunBoard({ auth }: RunBoardProps) {
  const [runs, setRuns] = useState<WorkflowRun[]>([]);
  const [workflows, setWorkflows] = useState<Workflow[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [startingSample, setStartingSample] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [searchParams, setSearchParams] = useSearchParams();
  const navigate = useNavigate();
  const runsRef = useRef<WorkflowRun[]>([]);
  const { toasts, pushToast, dismissToast } = useToastQueue();
  const canStartRuns = canOperate(auth);

  const selectedStatus = (searchParams.get('status') ?? 'all') as 'all' | RunStatus;
  const selectedRisk = (searchParams.get('risk') ?? 'all') as 'all' | RiskLevel;
  const selectedSearch = searchParams.get('q') ?? '';

  useEffect(() => {
    runsRef.current = runs;
  }, [runs]);

  const loadRuns = useCallback(async (options: { background?: boolean } = {}) => {
    const hasExistingRuns = runsRef.current.length > 0;
    const shouldBlock = !options.background && !hasExistingRuns;

    if (shouldBlock) {
      setLoading(true);
      setError(null);
    } else {
      setRefreshing(true);
    }

    try {
      const data = await apiClient.getRuns();
      setRuns(data);
      setError(null);
    } catch (loadError) {
      const message = loadError instanceof Error ? loadError.message : 'Unknown error';
      if (hasExistingRuns) {
        pushToast({ tone: 'error', title: 'Run refresh failed', message });
      } else {
        setError(message);
      }
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [pushToast]);

  const loadWorkflows = useCallback(async () => {
    try {
      const data = await apiClient.getWorkflows();
      setWorkflows(data);
    } catch (loadError) {
      const message = loadError instanceof Error ? loadError.message : 'Unknown error';
      pushToast({ tone: 'error', title: 'Workflow catalog unavailable', message });
    }
  }, [pushToast]);

  useEffect(() => {
    void loadRuns();
    void loadWorkflows();
    const timer = setInterval(() => void loadRuns({ background: true }), 15_000);
    return () => clearInterval(timer);
  }, [loadRuns, loadWorkflows]);

  const sampleWorkflow = useMemo(() => {
    const activeWorkflows = workflows.filter((workflow) => workflow.status === 'active');
    return activeWorkflows.find((workflow) => workflow.id === FIRST_RUN_SAMPLE_WORKFLOW_ID)
      ?? activeWorkflows.find((workflow) =>
        workflow.tags.some((tag) => ['first-run', 'quickstart', 'sample'].includes(tag.toLowerCase())),
      );
  }, [workflows]);

  const startSampleWorkflow = useCallback(async () => {
    if (!canStartRuns) {
      pushToast({
        tone: 'error',
        title: 'Operator role required',
        message: 'Starting workflow runs requires the Operator or Admin role.',
      });
      return;
    }

    if (!sampleWorkflow) {
      navigate('/workflows');
      return;
    }

    setStartingSample(true);
    try {
      const result = await apiClient.startRun(sampleWorkflow.id);
      pushToast({
        tone: 'success',
        title: 'Sample run started',
        message: sampleWorkflow.name,
      });
      navigate(`/runs/${result.runId}`);
    } catch (startError) {
      const message = startError instanceof Error ? startError.message : 'Unknown error';
      pushToast({ tone: 'error', title: 'Sample run failed', message });
    } finally {
      setStartingSample(false);
    }
  }, [canStartRuns, navigate, pushToast, sampleWorkflow]);

  const filteredRuns = useMemo(() => {
    const normalizedSearch = selectedSearch.trim().toLowerCase();

    return runs.filter((run) => {
      const statusMatch = selectedStatus === 'all' || run.status === selectedStatus;
      const riskMatch = selectedRisk === 'all' || run.riskLevel === selectedRisk;
      const searchMatch = normalizedSearch.length === 0 || [
        run.id,
        run.workflowName,
        run.workflowVersion,
        run.status,
        run.riskLevel,
        run.currentStep ?? '',
        run.requestedBy,
        ...run.tags,
      ].some((value) => value.toLowerCase().includes(normalizedSearch));

      return statusMatch && riskMatch && searchMatch;
    });
  }, [runs, selectedRisk, selectedSearch, selectedStatus]);

  const requestedPage = Number.parseInt(searchParams.get('page') ?? '1', 10);
  const normalizedPage = Number.isFinite(requestedPage) && requestedPage > 0 ? requestedPage : 1;
  const ledgerPageCount = Math.max(1, Math.ceil(filteredRuns.length / RUN_LEDGER_PAGE_SIZE));
  const ledgerPage = Math.min(normalizedPage, ledgerPageCount);
  const ledgerRuns = filteredRuns.slice(
    (ledgerPage - 1) * RUN_LEDGER_PAGE_SIZE,
    ledgerPage * RUN_LEDGER_PAGE_SIZE,
  );

  const setLedgerPage = useCallback((page: number) => {
    setSearchParams((previous) => {
      const next = new URLSearchParams(previous);
      if (page <= 1) {
        next.delete('page');
      } else {
        next.set('page', String(page));
      }
      return next;
    });
  }, [setSearchParams]);

  useEffect(() => {
    if (loading || filteredRuns.length === 0 || requestedPage === ledgerPage) {
      return;
    }

    setLedgerPage(ledgerPage);
  }, [filteredRuns.length, ledgerPage, loading, requestedPage, setLedgerPage]);

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
  const successRate = toPercent(stats.completed, resolvedRuns);
  const criticalCount = runs.filter((run) => run.riskLevel === 'critical' || run.riskLevel === 'high').length;
  const pendingApprovalCount = runs.reduce((total, run) => total + run.pendingApprovals, 0);
  const evidenceReadyCount = runs.filter(
    (run) => (run.events?.length ?? 0) > 0 || (run.artifacts?.length ?? 0) > 0 || (run.approvals?.length ?? 0) > 0,
  ).length;
  const evidenceCoverage = toPercent(evidenceReadyCount, runs.length);
  // Only signals derived from real run data belong here — no hardcoded posture claims.
  const readinessSignals = [
    {
      label: 'Audit',
      value: `${evidenceCoverage}% captured`,
      detail: `${evidenceReadyCount}/${runs.length || 0} runs with evidence`,
      tone: evidenceCoverage >= 95 ? 'healthy' : 'warning',
    },
    {
      label: 'SLO Risk',
      value: criticalCount > 0 ? `${criticalCount} elevated` : 'Normal',
      detail: `${pendingApprovalCount} approvals pending`,
      tone: criticalCount > 0 || pendingApprovalCount > 0 ? 'warning' : 'healthy',
    },
  ] as const;
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

  if (error && runs.length === 0) {
    return <ErrorState message={error} onRetry={() => void loadRuns()} />;
  }

  return (
    <section className="ops-dashboard">
      <ToastRegion toasts={toasts} onDismiss={dismissToast} />
      <PageHeader
        title="Runs"
        description="Execution monitoring for active workflow cohorts, approvals, failures, and agent throughput."
        actions={
          <>
            <button
              type="button"
              className="btn btn-secondary"
              disabled={refreshing}
              onClick={() => void loadRuns({ background: true })}
            >
              {refreshing ? 'Syncing...' : 'Sync'}
            </button>
            <button
              type="button"
              className={canStartRuns ? 'btn btn-primary' : 'btn btn-secondary'}
              onClick={() => navigate('/workflows')}
            >
              {canStartRuns ? 'Deploy Workflow' : 'View Workflows'}
            </button>
          </>
        }
      />

      <section className="readiness-strip" aria-label="Run governance summary">
        {readinessSignals.map((signal) => (
          <article key={signal.label} className={`readiness-item readiness-${signal.tone}`}>
            <span className={`status-dot ${signal.tone}`} aria-hidden="true" />
            <div>
              <span className="panel-kicker">{signal.label}</span>
              <strong>{signal.value}</strong>
              <p>{signal.detail}</p>
            </div>
          </article>
        ))}
      </section>

      <article className="panel performance-panel" aria-label="Execution monitoring overview">
        <div className="panel-title-row">
          <div>
            <span className="panel-kicker">Real-time cohort</span>
            <h2>Performance Metrics</h2>
          </div>
        </div>

        <dl className="metric-strip">
          <div>
            <dt>Active Runs</dt>
            <dd>{activeCount}</dd>
            <span>running + approvals</span>
          </div>
          <div>
            <dt>Agent Success</dt>
            <dd>{successRate}%</dd>
            <span>resolved runs</span>
          </div>
          <div>
            <dt>P95 Duration</dt>
            <dd>{toP95Duration(runs)}</dd>
            <span>observed run duration</span>
          </div>
        </dl>

        <a className="panel-link" href="#run-ledger">
          View run ledger detail
        </a>
      </article>

      <section className="kpi-grid" aria-label="Run summary metrics">
        <KpiCard label="Running" value={stats.running} accent="running" hint="Live workers" />
        <KpiCard label="Failed" value={stats.failed} accent="failed" hint="Needs triage" />
        <KpiCard label="Awaiting Approval" value={stats.awaiting} accent="awaiting" hint="Human gate" />
        <KpiCard label="Completed" value={stats.completed} accent="completed" hint="Resolved cleanly" />
        <KpiCard label="Blocked" value={stats.blocked} accent="blocked" hint="SLO risk" />
      </section>

      <FilterBar
        status={selectedStatus}
        risk={selectedRisk}
        search={selectedSearch}
        onStatusChange={(status) => {
          setSearchParams((previous) => {
            const next = new URLSearchParams(previous);
            next.set('status', status);
            next.delete('page');
            return next;
          });
        }}
        onRiskChange={(risk) => {
          setSearchParams((previous) => {
            const next = new URLSearchParams(previous);
            next.set('risk', risk);
            next.delete('page');
            return next;
          });
        }}
        onSearchChange={(search) => {
          setSearchParams((previous) => {
            const next = new URLSearchParams(previous);
            const trimmed = search.trim();
            if (trimmed) {
              next.set('q', trimmed);
            } else {
              next.delete('q');
            }
            next.delete('page');
            return next;
          });
        }}
      />

      {filteredRuns.length === 0 ? (
        runs.length === 0 && sampleWorkflow ? (
          <section className="panel first-run-onboarding" aria-label="First-run onboarding">
            <div className="first-run-onboarding-copy">
              <span className="panel-kicker">Seeded sample</span>
              <h2>Run your first workflow</h2>
              <p>
                Start the sample line to watch an agent task move into review with policy and evidence captured on the run.
              </p>
            </div>
            <ol className="first-run-steps" aria-label="First-run path">
              <li>
                <strong>Agent</strong>
                <span>{sampleWorkflow.name}</span>
              </li>
              <li>
                <strong>Policy</strong>
                <span>Decision recorded</span>
              </li>
              <li>
                <strong>Review</strong>
                <span>Approval gate</span>
              </li>
            </ol>
            <div className="first-run-actions">
              <button
                type="button"
                className="btn btn-primary"
                disabled={startingSample || !canStartRuns}
                title={canStartRuns ? undefined : 'Operator or Admin role required'}
                onClick={() => void startSampleWorkflow()}
              >
                {startingSample ? 'Starting...' : 'Run sample workflow'}
              </button>
              <button type="button" className="btn btn-secondary" onClick={() => navigate('/workflows')}>
                View workflows
              </button>
            </div>
          </section>
        ) : (
          <EmptyState
            title={runs.length === 0 ? 'No runs have started yet' : 'No runs match the current filters'}
            description={
              runs.length === 0
                ? 'Start from a workflow to create the first monitored run.'
                : 'Adjust search, status, or risk filters to widen the run ledger.'
            }
            action={
              <button type="button" className="btn btn-primary" onClick={() => navigate('/workflows')}>
                Open Workflows
              </button>
            }
          />
        )
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
                  <dl className="run-card-facts">
                    <div>
                      <dt>Owner</dt>
                      <dd>{run.requestedBy}</dd>
                    </div>
                    <div>
                      <dt>Approvals</dt>
                      <dd>{run.pendingApprovals > 0 ? `${run.pendingApprovals} pending` : 'Clear'}</dd>
                    </div>
                  </dl>
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

          <section id="run-ledger" className="run-ledger">
            <div className="section-heading-row">
              <div>
                <span className="panel-kicker">Filtered telemetry</span>
                <h2>Run Ledger</h2>
              </div>
            </div>
            <DataTable
              caption="Workflow runs with status, risk, ownership, and approval state"
              columns={columns}
              rows={ledgerRuns}
              rowKey={(run) => run.id}
              onRowClick={(run) => navigate(`/runs/${run.id}`)}
              rowAriaLabel={(run) => `Open run ${run.id} for ${run.workflowName}`}
            />
            <Pagination
              currentPage={ledgerPage}
              pageSize={RUN_LEDGER_PAGE_SIZE}
              totalItems={filteredRuns.length}
              itemLabel="runs"
              onPageChange={setLedgerPage}
            />
          </section>

          <details className="panel log-stream-panel run-events-collapsible" aria-label="Global log stream">
            <summary className="event-monitor-summary">
              <h2>Log Stream</h2>
              <span className="event-monitor-summary-actions">
                <span className="chip chip-static">{sortedEvents.length} event(s)</span>
                <span className="event-monitor-caret" aria-hidden="true">›</span>
              </span>
            </summary>
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
          </details>
        </>
      )}
    </section>
  );
}
