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
import { BpmnRunGraph } from '../components/BpmnRunGraph';
import type { WorkflowRun } from '../types';

const tabs = ['Summary', 'Logs', 'I/O', 'Policy', 'Artifacts', 'Approvals'];

function formatBytes(sizeBytes: number): string {
  if (sizeBytes < 1024) {
    return `${sizeBytes} B`;
  }

  if (sizeBytes < 1024 * 1024) {
    return `${(sizeBytes / 1024).toFixed(1)} KB`;
  }

  return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
}

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

  useEffect(() => {
    if (!run || run.status === 'completed' || run.status === 'failed' || run.status === 'cancelled') {
      return;
    }
    const timer = setInterval(loadRun, 10_000);
    return () => clearInterval(timer);
  }, [loadRun, run?.status]);

  const selectedStep = useMemo(() => {
    if (!run || !expandedStepId || !run.steps) {
      return null;
    }
    return run.steps.find((step) => step.id === expandedStepId) ?? null;
  }, [expandedStepId, run]);

  const policySteps = useMemo(
    () => (run?.steps ?? []).filter((step) => Boolean(step.policyDecision)),
    [run?.steps],
  );

  const runApprovals = useMemo(() => run?.approvals ?? [], [run]);

  const renderTabContent = () => {
    if (!run) return null;
    switch (activeTab) {
      case 'Summary':
        return (
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
        );
      case 'Logs':
        return run.events && run.events.length > 0 ? (
          <ul className="event-list" role="list">
            {run.events.map((event) => (
              <li key={event.id}>
                <strong>{event.type.replaceAll('_', ' ')}</strong>
                <p>{event.message}</p>
                <span className="cell-meta">{new Date(event.createdAt).toLocaleString()}</span>
              </li>
            ))}
          </ul>
        ) : (
          <p>No runtime logs have been persisted for this run.</p>
        );
      case 'I/O':
        return selectedStep ? (
          <dl className="definition-list">
            <div>
              <dt>Selected Step</dt>
              <dd>{selectedStep.name}</dd>
            </div>
            <div>
              <dt>Agent</dt>
              <dd>{selectedStep.agentName ?? '-'}</dd>
            </div>
            <div>
              <dt>Output</dt>
              <dd>{selectedStep.output ?? 'No output captured.'}</dd>
            </div>
            <div>
              <dt>Error</dt>
              <dd>{selectedStep.error ?? 'No error captured.'}</dd>
            </div>
          </dl>
        ) : (
          <p>Select a step in the timeline to inspect its inputs and outputs.</p>
        );
      case 'Policy':
        return policySteps.length > 0 ? (
          <div className="approval-list">
            {policySteps.map((step) => (
              <article key={step.id} className="panel approval-card">
                <div className="approval-card-head">
                  <strong>{step.name}</strong>
                  {step.policyDecision ? (
                    <RiskBadge
                      level={step.policyDecision.riskLevel}
                      score={step.policyDecision.riskScore}
                    />
                  ) : null}
                </div>
                <p>{step.policyDecision?.rationale}</p>
                <p className="cell-meta">
                  Decision: {step.policyDecision?.kind} via {step.policyDecision?.policyName}
                </p>
                {step.policyDecision?.constraints?.length ? (
                  <div className="tag-row">
                    {step.policyDecision.constraints.map((constraint) => (
                      <span key={constraint} className="chip chip-static">
                        {constraint}
                      </span>
                    ))}
                  </div>
                ) : null}
              </article>
            ))}
          </div>
        ) : (
          <p>No policy decisions were persisted for this run.</p>
        );
      case 'Artifacts':
        return run.artifacts && run.artifacts.length > 0 ? (
          <div className="approval-list">
            {run.artifacts.map((artifact) => (
              <article key={artifact.name} className="panel approval-card">
                <div className="approval-card-head">
                  <strong>{artifact.name}</strong>
                  <span className="cell-meta">{formatBytes(artifact.sizeBytes)}</span>
                </div>
                <p className="cell-meta">
                  Updated {new Date(artifact.lastModifiedAt).toLocaleString()}
                </p>
                <div className="approval-footer">
                  <span>Persisted run artifact</span>
                  <a
                    className="btn btn-secondary"
                    href={apiClient.getRunArtifactDownloadUrl(run.id, artifact.name)}
                  >
                    Download
                  </a>
                </div>
              </article>
            ))}
          </div>
        ) : (
          <p>No artifacts have been uploaded for this run.</p>
        );
      case 'Approvals':
        return runApprovals.length > 0 ? (
          <div className="approval-list">
            {runApprovals.map((approval) => (
              <article key={approval.id} className="panel approval-card">
                <div className="approval-card-head">
                  <strong>{approval.actionRequested}</strong>
                  <RiskBadge level={approval.riskLevel} score={approval.riskScore} />
                </div>
                <p>{approval.policyRationale}</p>
                <p className="cell-meta">
                  {approval.status.replace('_', ' ')} by {approval.agentName}
                </p>
                {approval.decisionComment ? <p>Decision note: {approval.decisionComment}</p> : null}
              </article>
            ))}
          </div>
        ) : (
          <p>No approval records are attached to this run.</p>
        );
      default:
        return <p>Unsupported tab.</p>;
    }
  };

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
        <div className="run-detail-left">
          <BpmnRunGraph
            steps={run.steps ?? []}
            currentStep={run.currentStep}
            runStatus={run.status}
            selectedStepId={expandedStepId}
            onSelectStep={(id) => setExpandedStepId((prev) => (prev === id ? null : id))}
          />
          <StepTimeline
            steps={run.steps ?? []}
            expandedStepId={expandedStepId}
            onToggleStep={(stepId) => setExpandedStepId((current) => (current === stepId ? null : stepId))}
          />

          <article className="panel event-monitor-panel" aria-label="Run event monitor">
            <h2>Runtime Events</h2>
            {run.events && run.events.length > 0 ? (
              <ul className="event-list" role="list">
                {run.events.map((event) => (
                  <li key={event.id}>
                    <strong>{event.type.replace('_', ' ')}</strong>
                    <p>{event.message}</p>
                    <span className="cell-meta">{new Date(event.createdAt).toLocaleString()}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <p>No runtime events available.</p>
            )}
          </article>
        </div>

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
            {renderTabContent()}

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
