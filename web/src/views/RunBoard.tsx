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

  if (loading) {
    return <LoadingState message="Loading runs" />;
  }

  if (error) {
    return <ErrorState message={error} onRetry={loadRuns} />;
  }

  return (
    <section>
      <PageHeader
        title="Runs"
        description="Monitor active and historical workflow runs."
        actions={
          <button type="button" className="btn btn-secondary" onClick={loadRuns}>
            Refresh
          </button>
        }
      />

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
        <DataTable
          caption="Workflow runs"
          columns={columns}
          rows={filteredRuns}
          rowKey={(run) => run.id}
          onRowClick={(run) => navigate(`/runs/${run.id}`)}
        />
      )}
    </section>
  );
}
