import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiClient } from '../api/client';
import { AgentDetailPanel } from '../components/AgentDetailPanel';
import { BpmnRunGraph } from '../components/BpmnRunGraph';
import { BpmnViewer } from '../components/BpmnViewer';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import { RiskBadge } from '../components/RiskBadge';
import { RunDiffModal } from '../components/RunDiffModal';
import { StatusBadge } from '../components/StatusBadge';
import { StepTimeline } from '../components/StepTimeline';
import type { RunStatus, WorkflowRun } from '../types';

const tabs = ['Summary', 'Logs', 'I/O', 'Policy', 'Artifacts', 'Approvals'];

function formatBytes(sizeBytes: number): string {
  if (sizeBytes < 1024) return `${sizeBytes} B`;
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} KB`;
  return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function RunDetail() {
  const { runId } = useParams();
  const navigate = useNavigate();
  const [run, setRun] = useState<WorkflowRun | null>(null);
  const [activeTab, setActiveTab] = useState(tabs[0]);
  const [expandedStepId, setExpandedStepId] = useState<string | null>(null);
  const [panelStepId, setPanelStepId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [cancelling, setCancelling] = useState(false);

  // Workflow BPMN XML for the viewer + diff modal
  const [workflowXml, setWorkflowXml] = useState('');

  // Diff modal
  const [diffOpen, setDiffOpen] = useState(false);

  // Inline approval decisions
  const [decisionComment, setDecisionComment] = useState('');
  const [submittingApprovalId, setSubmittingApprovalId] = useState<string | null>(null);
  const [approvalError, setApprovalError] = useState<string | null>(null);

  const isFirstLoad = useRef(true);
  const lastManualSelectAt = useRef<number>(0);
  const prevCurrentStep = useRef<string | null | undefined>(undefined);

  const MANUAL_OVERRIDE_MS = 30_000;
  const TERMINAL = ['completed', 'failed', 'cancelled'] as const;
  const isTerminal = run ? (TERMINAL as readonly string[]).includes(run.status) : false;

  const loadRun = useCallback(async () => {
    if (!runId) {
      setError('Run ID is missing in the URL.');
      setLoading(false);
      return;
    }
    const firstLoad = isFirstLoad.current;
    if (firstLoad) setLoading(true);
    setError(null);
    try {
      const data = await apiClient.getRun(runId);
      if (!data) { setError(`Run ${runId} was not found.`); return; }
      if (firstLoad) {
        isFirstLoad.current = false;
        setExpandedStepId(data.steps?.[data.steps.length - 1]?.id ?? null);
        prevCurrentStep.current = data.currentStep;
      }
      setRun(data);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Unknown error');
    } finally {
      if (firstLoad) setLoading(false);
    }
  }, [runId]);

  useEffect(() => { void loadRun(); }, [loadRun]);

  // Polling: 10s while in-flight
  useEffect(() => {
    if (!run || isTerminal) return;
    const timer = setInterval(() => void loadRun(), 10_000);
    return () => clearInterval(timer);
  }, [loadRun, run, isTerminal]);

  // Auto-track: open panel for newly active step
  useEffect(() => {
    if (!run || isFirstLoad.current) return;
    const newStep = run.currentStep;
    if (newStep === prevCurrentStep.current) return;
    prevCurrentStep.current = newStep;
    if (!newStep) return;
    if (Date.now() - lastManualSelectAt.current > MANUAL_OVERRIDE_MS) {
      const stepId = run.steps?.find((s) => s.name === newStep)?.id ?? null;
      if (stepId) { setPanelStepId(stepId); setExpandedStepId(stepId); }
    }
  }, [run]);

  // Fetch workflow BPMN XML once we know the workflowId
  useEffect(() => {
    if (!run?.workflowId) return;
    let cancelled = false;
    void apiClient.getWorkflow(run.workflowId)
      .then((wf) => { if (!cancelled) setWorkflowXml(wf?.bpmnXml ?? ''); })
      .catch(() => {});
    return () => { cancelled = true; };
  }, [run?.workflowId]);

  // Build step-name → status map for the BpmnViewer
  const stepStatuses = useMemo(() => {
    const map: Record<string, RunStatus> = {};
    for (const step of run?.steps ?? []) map[step.name] = step.status;
    return map;
  }, [run?.steps]);

  const selectedStep = useMemo(() => {
    if (!run?.steps || !expandedStepId) return null;
    return run.steps.find((s) => s.id === expandedStepId) ?? null;
  }, [expandedStepId, run]);

  const panelStep = useMemo(() => {
    if (!run?.steps || !panelStepId) return null;
    return run.steps.find((s) => s.id === panelStepId) ?? null;
  }, [panelStepId, run]);

  const policySteps = useMemo(
    () => (run?.steps ?? []).filter((s) => Boolean(s.policyDecision)),
    [run?.steps],
  );

  const runApprovals = useMemo(() => run?.approvals ?? [], [run]);

  const handleApprovalDecision = async (
    approvalId: string,
    decision: 'approve' | 'reject' | 'escalate',
  ) => {
    if (decision === 'reject' && !decisionComment.trim()) {
      setApprovalError('A reason is required when rejecting an approval request.');
      return;
    }
    setSubmittingApprovalId(approvalId);
    setApprovalError(null);
    try {
      await apiClient.decideApproval(approvalId, decision, decisionComment.trim() || undefined);
      setDecisionComment('');
      await loadRun();
    } catch (err) {
      setApprovalError(err instanceof Error ? err.message : 'Failed to submit decision.');
    } finally {
      setSubmittingApprovalId(null);
    }
  };

  const handleCancelRun = async () => {
    if (!runId) return;
    setCancelling(true);
    try {
      await apiClient.cancelRun(runId);
      setConfirmOpen(false);
      navigate('/runs');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to cancel run.');
      setConfirmOpen(false);
    } finally {
      setCancelling(false);
    }
  };

  const renderTabContent = () => {
    if (!run) return null;
    switch (activeTab) {
      case 'Summary':
        return (
          <dl className="definition-list">
            <div><dt>Run ID</dt><dd>{run.id}</dd></div>
            <div><dt>Workflow</dt><dd>{run.workflowName}</dd></div>
            <div><dt>Status</dt><dd>{run.status.replace('_', ' ')}</dd></div>
            <div><dt>Current Step</dt><dd>{run.currentStep ?? '-'}</dd></div>
            <div><dt>Tags</dt><dd>{run.tags.join(', ') || '-'}</dd></div>
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
            <div><dt>Selected Step</dt><dd>{selectedStep.name}</dd></div>
            <div><dt>Agent</dt><dd>{selectedStep.agentName ?? '-'}</dd></div>
            <div><dt>Output</dt><dd>{selectedStep.output ?? 'No output captured.'}</dd></div>
            <div><dt>Error</dt><dd>{selectedStep.error ?? 'No error captured.'}</dd></div>
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
                    <RiskBadge level={step.policyDecision.riskLevel} score={step.policyDecision.riskScore} />
                  ) : null}
                </div>
                <p>{step.policyDecision?.rationale}</p>
                <p className="cell-meta">
                  Decision: {step.policyDecision?.kind} via {step.policyDecision?.policyName}
                </p>
                {step.policyDecision?.constraints?.length ? (
                  <div className="tag-row">
                    {step.policyDecision.constraints.map((c) => (
                      <span key={c} className="chip chip-static">{c}</span>
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
                <p className="cell-meta">Updated {new Date(artifact.lastModifiedAt).toLocaleString()}</p>
                <div className="approval-footer">
                  <span>Persisted run artifact</span>
                  <a className="btn btn-secondary" href={apiClient.getRunArtifactDownloadUrl(run.id, artifact.name)}>
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
        return (
          <>
            {approvalError && <p className="validation-error">{approvalError}</p>}
            {runApprovals.length > 0 ? (
              <div className="approval-list">
                {runApprovals.map((approval) => (
                  <article key={approval.id} className="panel approval-card">
                    <div className="approval-card-head">
                      <strong>{approval.actionRequested}</strong>
                      <RiskBadge level={approval.riskLevel} score={approval.riskScore} />
                    </div>
                    <p>{approval.policyRationale}</p>
                    <p className="cell-meta">Requested by {approval.agentName}</p>
                    {approval.decisionComment ? (
                      <p>Decision note: {approval.decisionComment}</p>
                    ) : null}
                    {approval.status === 'pending' ? (
                      <div className="approval-inline-decision">
                        <textarea
                          className="approval-comment-input"
                          placeholder="Comment (required for rejection)"
                          value={decisionComment}
                          onChange={(e) => setDecisionComment(e.target.value)}
                          rows={2}
                        />
                        <div className="approval-decision-actions">
                          <button
                            type="button"
                            className="btn btn-primary"
                            disabled={submittingApprovalId === approval.id}
                            aria-label={`Approve ${approval.actionRequested}`}
                            onClick={() => void handleApprovalDecision(approval.id, 'approve')}
                          >
                            {submittingApprovalId === approval.id ? 'Processing…' : 'Approve'}
                          </button>
                          <button
                            type="button"
                            className="btn btn-danger"
                            disabled={submittingApprovalId === approval.id}
                            aria-label={`Reject ${approval.actionRequested}`}
                            onClick={() => void handleApprovalDecision(approval.id, 'reject')}
                          >
                            Reject
                          </button>
                          <button
                            type="button"
                            className="btn btn-secondary"
                            disabled={submittingApprovalId === approval.id}
                            aria-label={`Escalate ${approval.actionRequested}`}
                            onClick={() => void handleApprovalDecision(approval.id, 'escalate')}
                          >
                            Escalate
                          </button>
                        </div>
                      </div>
                    ) : (
                      <p className="cell-meta approval-decided">
                        {approval.status.charAt(0).toUpperCase() + approval.status.slice(1)}
                        {approval.decidedBy ? ` by ${approval.decidedBy}` : ''}
                        {approval.decidedAt
                          ? ` at ${new Date(approval.decidedAt).toLocaleString()}`
                          : ''}
                      </p>
                    )}
                  </article>
                ))}
              </div>
            ) : (
              <p>No approval records are attached to this run.</p>
            )}
          </>
        );

      default:
        return <p>Unsupported tab.</p>;
    }
  };

  if (loading) return <LoadingState message="Loading run detail" />;
  if (error || !run) return <ErrorState message={error ?? 'Run not found'} onRetry={loadRun} />;

  return (
    <section>
      <PageHeader
        title={`Run ${run.id}`}
        description={`${run.workflowName} · ${run.workflowVersion}`}
        actions={
          <>
            {isTerminal && (
              <button type="button" className="btn btn-secondary" onClick={() => void loadRun()}>
                Refresh
              </button>
            )}
            <Link to="/runs" className="btn btn-secondary">Back to runs</Link>
          </>
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
          {/* SVG flow graph — compact token overview */}
          <BpmnRunGraph
            steps={run.steps ?? []}
            currentStep={run.currentStep}
            runStatus={run.status}
            selectedStepId={panelStepId}
            onSelectStep={(id) => {
              const next = panelStepId === id ? null : id;
              setPanelStepId(next);
              if (next) { setExpandedStepId(next); lastManualSelectAt.current = Date.now(); }
            }}
          />

          {/* Real BPMN canvas with token colour markers (loads once workflow XML is fetched) */}
          {workflowXml && (
            <section className="panel graph-panel" aria-label="BPMN workflow canvas">
              <h2>BPMN Canvas</h2>
              <BpmnViewer xml={workflowXml} stepStatuses={stepStatuses} />
              <div className="graph-legend">
                {[
                  { cls: 'raf-completed', label: 'completed' },
                  { cls: 'raf-running', label: 'running' },
                  { cls: 'raf-awaiting', label: 'awaiting approval' },
                  { cls: 'raf-failed', label: 'failed' },
                ].map(({ cls, label }) => (
                  <span key={label} className="graph-legend-item">
                    <span className={`graph-legend-swatch raf-swatch-${cls}`} />
                    {label}
                  </span>
                ))}
              </div>
            </section>
          )}

          <AgentDetailPanel
            step={panelStep}
            events={run.events ?? []}
            onClose={() => setPanelStepId(null)}
          />

          <StepTimeline
            steps={run.steps ?? []}
            expandedStepId={expandedStepId}
            onToggleStep={(stepId) =>
              setExpandedStepId((current) => (current === stepId ? null : stepId))
            }
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
                <p>{selectedStep.policyDecision.policyName} decided {selectedStep.policyDecision.kind}.</p>
                <p>{selectedStep.policyDecision.rationale}</p>
                <p>
                  Risk score {selectedStep.policyDecision.riskScore} with factors:{' '}
                  {selectedStep.policyDecision.riskFactors.join(', ')}.
                </p>
              </section>
            ) : null}
          </section>

          <div className="action-row">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => setDiffOpen(true)}
            >
              View Execution Diff
            </button>
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
        confirmLabel={cancelling ? 'Cancelling…' : 'Cancel run'}
        onConfirm={() => void handleCancelRun()}
        onCancel={() => setConfirmOpen(false)}
      />

      {diffOpen && (
        <RunDiffModal
          run={run}
          workflowXml={workflowXml}
          onClose={() => setDiffOpen(false)}
        />
      )}
    </section>
  );
}
