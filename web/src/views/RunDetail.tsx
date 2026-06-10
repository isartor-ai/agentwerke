import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiClient } from '../api/client';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import { RiskBadge } from '../components/RiskBadge';
import { StatusBadge } from '../components/StatusBadge';
import { StepTimeline } from '../components/StepTimeline';
import type { WorkflowRun } from '../types';

const tabs = ['Summary', 'Logs', 'I/O', 'Policy', 'Artifacts', 'Approvals'];

export function RunDetail() {
  const { runId } = useParams();
  const [run, setRun] = useState<WorkflowRun | null>(null);
  const [activeTab, setActiveTab] = useState(tabs[0]);
  const [expandedStepId, setExpandedStepId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const navigate = useNavigate();

  const loadRun = useCallback(async () => {
    if (!runId) {
      setError('Run ID is missing in the URL.');
      setLoading(false);
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const data = await apiClient.getRun(runId);
      if (!data) {
        setError(`Run ${runId} was not found.`);
        return;
      }
      setRun(data);
      setExpandedStepId(data.steps?.[data.steps.length - 1]?.id ?? null);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  }, [runId]);

  useEffect(() => {
    loadRun();
  }, [loadRun]);

  const selectedStep = useMemo(() => {
    if (!run || !expandedStepId || !run.steps) {
      return null;
    }
    return run.steps.find((step) => step.id === expandedStepId) ?? null;
  }, [expandedStepId, run]);

  if (loading) {
    return <LoadingState message="Loading run detail" />;
  }

  if (error || !run) {
    return <ErrorState message={error ?? 'Run not found'} onRetry={loadRun} />;
  }

  return (
    <section>
      <PageHeader
        title={`Run ${run.id}`}
        description={`${run.workflowName} · ${run.workflowVersion}`}
        actions={
          <Link to="/runs" className="btn btn-secondary">
            Back to runs
          </Link>
        }
      />

      <section className="panel run-meta-strip">
        <StatusBadge status={run.status} />
        <RiskBadge level={run.riskLevel} />
        <span>Requester: {run.requestedBy}</span>
        <span>{run.pendingApprovals} pending approval(s)</span>
      </section>

      <section className="run-detail-grid">
        <StepTimeline
          steps={run.steps ?? []}
          expandedStepId={expandedStepId}
          onToggleStep={(stepId) => setExpandedStepId((current) => (current === stepId ? null : stepId))}
        />

        <article className="panel run-side-panel">
          <div className="tab-row" role="tablist" aria-label="Run detail tabs">
            {tabs.map((tab) => (
              <button
                key={tab}
                type="button"
                role="tab"
                aria-selected={activeTab === tab}
                className={`tab ${activeTab === tab ? 'tab-active' : ''}`}
                onClick={() => setActiveTab(tab)}
              >
                {tab}
              </button>
            ))}
          </div>

          <section role="tabpanel" className="tab-panel">
            {activeTab === 'Summary' ? (
              <dl className="definition-list">
                <div>
                  <dt>Run ID</dt>
                  <dd>{run.id}</dd>
                </div>
                <div>
                  <dt>Workflow</dt>
                  <dd>{run.workflowName}</dd>
                </div>
                <div>
                  <dt>Status</dt>
                  <dd>{run.status.replace('_', ' ')}</dd>
                </div>
                <div>
                  <dt>Current Step</dt>
                  <dd>{run.currentStep ?? '-'}</dd>
                </div>
                <div>
                  <dt>Tags</dt>
                  <dd>{run.tags.join(', ') || '-'}</dd>
                </div>
              </dl>
            ) : (
              <p>Content for {activeTab} will be expanded in later phases.</p>
            )}

            {selectedStep?.policyDecision ? (
              <section className="policy-box">
                <h3>Policy decision</h3>
                <p>
                  {selectedStep.policyDecision.policyName} decided {selectedStep.policyDecision.kind}.
                </p>
                <p>{selectedStep.policyDecision.rationale}</p>
                <p>
                  Risk score {selectedStep.policyDecision.riskScore} with factors:{' '}
                  {selectedStep.policyDecision.riskFactors.join(', ')}.
                </p>
              </section>
            ) : null}
          </section>

          <div className="action-row">
            <button type="button" className="btn btn-secondary" onClick={() => navigate('/approvals')}>
              Request Approval
            </button>
            <button type="button" className="btn btn-secondary">
              Export Audit Bundle
            </button>
            <button
              type="button"
              className="btn btn-danger"
              aria-label="Cancel run and stop further execution"
              onClick={() => setConfirmOpen(true)}
            >
              Cancel Run
            </button>
          </div>
        </article>
      </section>

      <ConfirmDialog
        title="Cancel this run?"
        body="This action stops execution and cannot be automatically resumed."
        open={confirmOpen}
        danger
        confirmLabel="Cancel run"
        onConfirm={() => setConfirmOpen(false)}
        onCancel={() => setConfirmOpen(false)}
      />
    </section>
  );
}
