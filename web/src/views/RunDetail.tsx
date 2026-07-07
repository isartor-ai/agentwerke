import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { apiClient } from '../api/client';
import { canApprove, canOperate } from '../auth/permissions';
import { AgentDetailPanel } from '../components/AgentDetailPanel';
import { BpmnViewer } from '../components/BpmnViewer';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ErrorState } from '../components/ErrorState';
import { LoadingState } from '../components/LoadingState';
import { ModelActivityDetails } from '../components/ModelActivityDetails';
import { PageHeader } from '../components/PageHeader';
import { RiskBadge } from '../components/RiskBadge';
import { RunDiffModal } from '../components/RunDiffModal';
import { SandboxExecutionDetails } from '../components/SandboxExecutionDetails';
import { StatusBadge } from '../components/StatusBadge';
import { StepTimeline } from '../components/StepTimeline';
import { ConversationTab } from '../components/ConversationTab';
import type { AuthState, EvidencePack, RunEvent, RunInteraction, RunStatus, WorkflowRun } from '../types';
import { formatTokenCount, sumRunTokens } from '../utils/tokens';
import { extractAgentReasoningByStep, isLiveProgressEventType } from '../utils/visibleReasoning';

const tabs = ['Summary', 'Conversation', 'Evidence', 'Logs', 'I/O', 'Policy', 'Artifacts', 'Approvals'];

function formatBytes(sizeBytes: number): string {
  if (sizeBytes < 1024) return `${sizeBytes} B`;
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} KB`;
  return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`;
}

function countToolInvocations(run: WorkflowRun): number {
  return (run.steps ?? []).reduce((count, step) => (
    count +
    (step.toolInvocations?.length ?? 0) +
    (step.runtimeSnapshot?.toolInvocations?.length ?? 0)
  ), 0);
}

function formatDuration(ms?: number | null): string {
  if (ms == null) return '-';
  if (ms < 1000) return `${ms} ms`;
  return `${(ms / 1000).toFixed(1)} s`;
}

function formatTimestamp(value?: string | null): string {
  return value ? new Date(value).toLocaleString() : '-';
}

function totalEvidenceTokens(pack: EvidencePack | null): number {
  return (pack?.modelUsage ?? []).reduce(
    (total, usage) => total + usage.inputTokens + usage.outputTokens,
    0,
  );
}

function shouldRefreshRunForEvent(event: RunEvent): boolean {
  return !isLiveProgressEventType(event.type);
}

interface RunDetailProps {
  auth: AuthState;
}

export function RunDetail({ auth }: RunDetailProps) {
  const { runId } = useParams();
  const navigate = useNavigate();
  const [run, setRun] = useState<WorkflowRun | null>(null);
  const [interactions, setInteractions] = useState<RunInteraction[]>([]);
  const [interactionError, setInteractionError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState(tabs[0]);
  const [expandedStepId, setExpandedStepId] = useState<string | null>(null);
  const [panelStepId, setPanelStepId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [exportingEvidence, setExportingEvidence] = useState(false);
  const [evidenceExportError, setEvidenceExportError] = useState<string | null>(null);
  const [evidencePack, setEvidencePack] = useState<EvidencePack | null>(null);
  const [evidenceLoading, setEvidenceLoading] = useState(false);
  const [evidenceError, setEvidenceError] = useState<string | null>(null);

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
  const canControlRuns = canOperate(auth);
  const canSubmitApprovals = canApprove(auth);

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
        const initialStepId = data.steps?.[data.steps.length - 1]?.id ?? null;
        setExpandedStepId(initialStepId);
        setPanelStepId(initialStepId);
        prevCurrentStep.current = data.currentStep;
      }
      setRun(data);
      // Best-effort: the conversation thread should never block the run view (#192).
      void apiClient.getRunInteractions(runId)
        .then((items) => {
          setInteractions(items);
          setInteractionError(null);
        })
        .catch((interactionLoadError) => {
          setInteractionError(
            interactionLoadError instanceof Error
              ? interactionLoadError.message
              : 'Failed to load agent conversation.',
          );
        });
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Unknown error');
    } finally {
      if (firstLoad) setLoading(false);
    }
  }, [runId]);

  useEffect(() => { void loadRun(); }, [loadRun]);

  const handleAnswerInteraction = useCallback(async (interactionId: string, answer: string) => {
    if (!runId) return;
    await apiClient.answerInteraction(runId, interactionId, answer);
    await loadRun();
  }, [runId, loadRun]);

  // Count interactions per step so the timeline can mark steps that talked to a human or agent (#192).
  const interactionCountByStep = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const interaction of interactions) {
      if (interaction.stepId) {
        counts[interaction.stepId] = (counts[interaction.stepId] ?? 0) + 1;
      }
    }
    return counts;
  }, [interactions]);

  const reasoningByStep = useMemo(
    () => extractAgentReasoningByStep(run?.events),
    [run?.events],
  );

  const loadEvidencePack = useCallback(async () => {
    if (!runId || !canControlRuns) return;
    setEvidenceLoading(true);
    setEvidenceError(null);
    try {
      const pack = await apiClient.getRunEvidencePack(runId);
      setEvidencePack(pack);
    } catch (loadError) {
      setEvidenceError(loadError instanceof Error ? loadError.message : 'Failed to load evidence pack.');
    } finally {
      setEvidenceLoading(false);
    }
  }, [canControlRuns, runId]);

  useEffect(() => {
    if (!canControlRuns) {
      setEvidencePack(null);
      return;
    }

    void loadEvidencePack();
  }, [canControlRuns, loadEvidencePack]);

  // Polling: 10s while in-flight (fallback when SSE stream is unavailable)
  useEffect(() => {
    if (!run || isTerminal) return;
    const timer = setInterval(() => void loadRun(), 10_000);
    return () => clearInterval(timer);
  }, [loadRun, run, isTerminal]);

  // SSE live event stream: open while run is non-terminal, auto-reconnect on drop
  const [sseRevision, setSseRevision] = useState(0);
  useEffect(() => {
    if (!runId || isTerminal) return;
    const controller = new AbortController();
    apiClient.streamRunEvents(
      runId,
      (event) => {
        setRun((prev) => {
          if (!prev) return prev;
          const existing = prev.events ?? [];
          if (existing.some((e) => e.id === event.id)) return prev;
          return { ...prev, events: [...existing, event] };
        });
        if (shouldRefreshRunForEvent(event)) {
          void loadRun();
        }
      },
      () => {
        void loadRun();
        // If the component is still mounted and the run isn't terminal yet,
        // increment the revision to re-open the SSE stream.
        if (!controller.signal.aborted) {
          setSseRevision((r) => r + 1);
        }
      },
      controller.signal,
    );
    return () => { controller.abort(); };
  }, [runId, isTerminal, loadRun, sseRevision]);

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

  const runTokens = useMemo(() => sumRunTokens(run?.steps ?? []), [run?.steps]);

  const runApprovals = useMemo(() => run?.approvals ?? [], [run]);

  const evidenceSummary = useMemo(() => {
    if (!run) return null;

    const policyDecisionCount = (run.steps ?? []).filter((step) => Boolean(step.policyDecision)).length;
    const readiness = run.status === 'completed'
      ? 'Ready'
      : isTerminal
        ? 'Available'
        : 'Collecting';

    return {
      readiness,
      approvals: run.approvals?.length ?? 0,
      artifacts: run.artifacts?.length ?? 0,
      policyDecisions: policyDecisionCount,
      toolCalls: countToolInvocations(run),
      events: run.events?.length ?? 0,
    };
  }, [isTerminal, run]);

  const evidencePackSummary = useMemo(() => {
    if (!evidencePack) return null;
    return {
      tokens: totalEvidenceTokens(evidencePack),
      modelRuns: evidencePack.modelUsage.length,
      policyDecisions: evidencePack.policyDecisions.length,
      sandboxExecutions: evidencePack.sandboxExecutions.length,
      auditEntries: evidencePack.auditLog.length,
      artifacts: evidencePack.artifacts.length,
    };
  }, [evidencePack]);

  const handleApprovalDecision = async (
    approvalId: string,
    decision: 'approve' | 'reject' | 'escalate',
  ) => {
    if (!canSubmitApprovals) {
      setApprovalError('Approver or Admin role required to submit approval decisions.');
      return;
    }

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
    if (!runId || !canControlRuns) return;
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

  const handleEvidenceExport = async () => {
    if (!runId || !canControlRuns) return;
    setExportingEvidence(true);
    setEvidenceExportError(null);
    try {
      await apiClient.downloadRunEvidencePack(runId);
    } catch (err) {
      setEvidenceExportError(err instanceof Error ? err.message : 'Failed to export evidence pack.');
    } finally {
      setExportingEvidence(false);
    }
  };

  const renderEvidencePack = () => {
    if (!canControlRuns) {
      return <p>Operator role required to view and export evidence packs.</p>;
    }

    if (evidenceLoading && !evidencePack) {
      return <p>Loading evidence pack…</p>;
    }

    if (evidenceError) {
      return (
        <section className="policy-box">
          <h3>Evidence Pack Viewer</h3>
          <p className="validation-error">{evidenceError}</p>
          <button type="button" className="btn btn-secondary" onClick={() => void loadEvidencePack()}>
            Retry
          </button>
        </section>
      );
    }

    if (!evidencePack || !evidencePackSummary) {
      return <p>No evidence pack is available for this run yet.</p>;
    }

    const recentAudit = evidencePack.auditLog.slice(-4).reverse();
    const recentSandboxLogs = evidencePack.sandboxExecutions.flatMap((sandbox) => sandbox.logs.slice(-2));

    return (
      <div className="evidence-viewer">
        <div className="evidence-viewer-head">
          <div>
            <h3>Evidence Pack Viewer</h3>
            <p className="cell-meta">
              Generated {formatTimestamp(evidencePack.generatedAt)} · schema {evidencePack.schemaVersion}
            </p>
          </div>
          <button
            type="button"
            className="btn btn-secondary"
            disabled={exportingEvidence}
            onClick={() => void handleEvidenceExport()}
          >
            {exportingEvidence ? 'Downloading…' : 'Download JSON'}
          </button>
        </div>

        <dl className="metric-strip evidence-metrics">
          <div>
            <dt>Tokens</dt>
            <dd>{evidencePackSummary.tokens} tokens</dd>
          </div>
          <div>
            <dt>Cost</dt>
            <dd>Cost not recorded</dd>
          </div>
          <div>
            <dt>Policy</dt>
            <dd>{evidencePackSummary.policyDecisions}</dd>
          </div>
          <div>
            <dt>Sandbox</dt>
            <dd>{evidencePackSummary.sandboxExecutions}</dd>
          </div>
          <div>
            <dt>Audit</dt>
            <dd>{evidencePackSummary.auditEntries}</dd>
          </div>
        </dl>

        <section className="policy-box">
          <h3>Model Usage</h3>
          {evidencePack.modelUsage.length > 0 ? (
            <ul className="event-list compact-list" role="list">
              {evidencePack.modelUsage.map((usage) => (
                <li key={`${usage.stepId}-${usage.modelId ?? 'model'}`}>
                  <strong>{usage.stepName}</strong>
                  <p>
                    {(usage.inputTokens + usage.outputTokens).toLocaleString()} tokens
                    {usage.modelId ? ` · ${usage.modelId}` : ''}
                  </p>
                  <span className="cell-meta">{formatDuration(usage.elapsedMs)}</span>
                </li>
              ))}
            </ul>
          ) : (
            <p>No model usage recorded.</p>
          )}
        </section>

        <section className="policy-box">
          <h3>Policy Decisions</h3>
          {evidencePack.policyDecisions.length > 0 ? (
            <ul className="event-list compact-list" role="list">
              {evidencePack.policyDecisions.map((decision) => (
                <li key={`${decision.stepId}-${decision.kind}`}>
                  <strong>{decision.policyName ?? decision.kind}</strong>
                  <p>{decision.rationale ?? 'No rationale recorded.'}</p>
                  <span className="cell-meta">
                    {decision.stepName} · risk {decision.riskScore}
                  </span>
                </li>
              ))}
            </ul>
          ) : (
            <p>No policy decisions recorded.</p>
          )}
        </section>

        <section className="policy-box">
          <h3>Sandbox</h3>
          {evidencePack.sandboxExecutions.length > 0 ? (
            <ul className="event-list compact-list" role="list">
              {evidencePack.sandboxExecutions.map((sandbox) => (
                <li key={`${sandbox.stepId}-${sandbox.sandboxId ?? sandbox.provider}`}>
                  <strong>{sandbox.provider}</strong>
                  <p>
                    {sandbox.stepName} · {sandbox.commandState}
                    {sandbox.sandboxId ? ` · ${sandbox.sandboxId}` : ''}
                  </p>
                  <span className="cell-meta">{formatDuration(sandbox.durationMs)}</span>
                </li>
              ))}
            </ul>
          ) : (
            <p>No sandbox executions recorded.</p>
          )}
          {recentSandboxLogs.length > 0 ? (
            <pre className="adp-pre evidence-log-preview">
              {recentSandboxLogs.map((log) => `[${log.stream}] ${log.message}`).join('\n')}
            </pre>
          ) : null}
        </section>

        <section className="policy-box">
          <h3>Audit</h3>
          {recentAudit.length > 0 ? (
            <ul className="event-list compact-list" role="list">
              {recentAudit.map((audit) => (
                <li key={audit.auditId}>
                  <strong>{audit.action}</strong>
                  <p>{audit.details ?? `${audit.actorType}:${audit.actor} ${audit.outcome}`}</p>
                  <span className="cell-meta">{formatTimestamp(audit.timestamp)}</span>
                </li>
              ))}
            </ul>
          ) : (
            <p>No audit entries recorded.</p>
          )}
        </section>

        <section className="policy-box">
          <h3>Artifacts</h3>
          {evidencePack.artifacts.length > 0 ? (
            <ul className="event-list compact-list" role="list">
              {evidencePack.artifacts.map((artifact) => (
                <li key={`${artifact.source}-${artifact.name}`}>
                  <strong>{artifact.name}</strong>
                  <p>{artifact.source}</p>
                  <span className="cell-meta">
                    {artifact.sizeBytes != null ? formatBytes(artifact.sizeBytes) : artifact.contentType ?? 'metadata only'}
                  </span>
                </li>
              ))}
            </ul>
          ) : (
            <p>No evidence artifacts recorded.</p>
          )}
        </section>
      </div>
    );
  };

  const renderTabContent = () => {
    if (!run) return null;
    switch (activeTab) {
      case 'Summary':
        return (
          <>
            <dl className="definition-list">
              <div><dt>Run ID</dt><dd>{run.id}</dd></div>
              <div><dt>Workflow</dt><dd>{run.workflowName}</dd></div>
              <div><dt>Status</dt><dd>{run.status.replace('_', ' ')}</dd></div>
              <div><dt>Current Step</dt><dd>{run.currentStep ?? '-'}</dd></div>
              <div><dt>Evidence Pack</dt><dd>{evidenceSummary?.readiness ?? 'Unavailable'}</dd></div>
              <div><dt>Tags</dt><dd>{run.tags.join(', ') || '-'}</dd></div>
            </dl>
            {evidenceSummary ? (
              <div className="tag-row" aria-label="Evidence pack contents">
                <span className="chip chip-static">{evidenceSummary.approvals} approval(s)</span>
                <span className="chip chip-static">{evidenceSummary.policyDecisions} policy decision(s)</span>
                <span className="chip chip-static">{evidenceSummary.toolCalls} tool call(s)</span>
                <span className="chip chip-static">{evidenceSummary.artifacts} artifact(s)</span>
                <span className="chip chip-static">{evidenceSummary.events} event(s)</span>
              </div>
            ) : null}
          </>
        );

      case 'Conversation':
        return (
          <ConversationTab
            interactions={interactions}
            error={interactionError}
            canAnswer={canSubmitApprovals}
            onAnswer={handleAnswerInteraction}
          />
        );

      case 'Evidence':
        return renderEvidencePack();

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
          <>
            <dl className="definition-list">
              <div><dt>Selected Step</dt><dd>{selectedStep.name}</dd></div>
              <div><dt>Agent</dt><dd>{selectedStep.agentName ?? selectedStep.runtimeSnapshot?.agentName ?? '-'}</dd></div>
              <div><dt>Action</dt><dd>{selectedStep.runtimeSnapshot?.action ?? '-'}</dd></div>
              <div><dt>Output</dt><dd>{selectedStep.output ?? 'No output captured.'}</dd></div>
              <div><dt>Error</dt><dd>{selectedStep.error ?? 'No error captured.'}</dd></div>
            </dl>
            {selectedStep.runtimeSnapshot?.promptInline ? (
              <section className="policy-box">
                <h3>Prompt</h3>
                <pre className="adp-pre">{selectedStep.runtimeSnapshot.promptInline}</pre>
              </section>
            ) : null}
            {selectedStep.runtimeSnapshot ? (
              <section className="policy-box">
                <h3>Execution</h3>
                <dl className="definition-list">
                  <div><dt>Mode</dt><dd>{selectedStep.runtimeSnapshot.executionMode}</dd></div>
                  <div>
                    <dt>LLM in sandbox</dt>
                    <dd>{selectedStep.runtimeSnapshot.executionMode === 'agent_sandboxed' ? 'yes' : 'no'}</dd>
                  </div>
                </dl>
              </section>
            ) : null}
            {(selectedStep.runtimeSnapshot?.stepArtifacts?.length ?? 0) > 0 ? (
              <section className="policy-box">
                <h3>Step artifacts</h3>
                <ul className="event-list" role="list">
                  {selectedStep.runtimeSnapshot!.stepArtifacts.map((artifact) => (
                    <li key={artifact.name}>
                      <strong>{artifact.name}</strong>
                      {artifact.contentType ? <p>{artifact.contentType}</p> : null}
                      {artifact.uri ? (
                        <a className="btn btn-secondary" href={artifact.uri}>
                          Download
                        </a>
                      ) : null}
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}
            <SandboxExecutionDetails
              sandboxExecution={selectedStep.runtimeSnapshot?.sandboxExecution}
              tokenUsage={selectedStep.runtimeSnapshot?.tokenUsage}
              sectionClassName="policy-box"
              headingClassName=""
            />
            <ModelActivityDetails
              modelTraces={selectedStep.runtimeSnapshot?.modelTraces}
              sectionClassName="policy-box"
              headingClassName=""
            />
          </>
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
                    {approval.status === 'pending' && canSubmitApprovals ? (
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
                    ) : approval.status === 'pending' ? (
                      <p className="cell-meta">Approver role required to submit a decision.</p>
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
        {runTokens.inputTokens + runTokens.outputTokens > 0 ? (
          <span
            className="run-token-total"
            title={`Model tokens used so far — input: ${runTokens.inputTokens.toLocaleString()}, output: ${runTokens.outputTokens.toLocaleString()}`}
          >
            Tokens: {formatTokenCount(runTokens.inputTokens)} in · {formatTokenCount(runTokens.outputTokens)} out
          </span>
        ) : null}
      </section>

      <section className="run-detail-grid">
        <div className="run-detail-left">
          {/* BPMN canvas: the authored process, with execution status overlaid as
              colour markers (loads once workflow XML is fetched). Clicking a step
              selects it, same as the step timeline below. */}
          {workflowXml && (
            <section className="panel graph-panel" aria-label="BPMN workflow canvas">
              <h2>BPMN Canvas</h2>
              <BpmnViewer
                xml={workflowXml}
                stepStatuses={stepStatuses}
                selectedStepName={panelStep?.name ?? null}
                onSelectStep={(name) => {
                  const next = name ? run.steps?.find((s) => s.name === name)?.id ?? null : null;
                  setPanelStepId(next);
                  if (next) { setExpandedStepId(next); lastManualSelectAt.current = Date.now(); }
                }}
              />
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
            reasoningByStep={reasoningByStep}
            onClose={() => setPanelStepId(null)}
          />

          <StepTimeline
            steps={run.steps ?? []}
            interactionCountByStep={interactionCountByStep}
            reasoningByStep={reasoningByStep}
            expandedStepId={expandedStepId}
            onToggleStep={(stepId) => {
              const next = expandedStepId === stepId ? null : stepId;
              setExpandedStepId(next);
              setPanelStepId(next);
              if (next) {
                lastManualSelectAt.current = Date.now();
              }
            }}
          />

          <details className="panel event-monitor-panel run-events-collapsible" aria-label="Run event monitor">
            <summary className="event-monitor-summary">
              <h2>Runtime Events</h2>
              <span className="event-monitor-summary-actions">
                <span className="chip chip-static">{run.events?.length ?? 0} event(s)</span>
                <span className="event-monitor-caret" aria-hidden="true">›</span>
              </span>
            </summary>
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
          </details>
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
            {evidenceExportError ? (
              <p className="validation-error">{evidenceExportError}</p>
            ) : null}
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
            <button
              type="button"
              className="btn btn-secondary"
              disabled={exportingEvidence || !canControlRuns}
              title={canControlRuns ? undefined : 'Operator or Admin role required'}
              onClick={() => void handleEvidenceExport()}
            >
              {exportingEvidence ? 'Exporting…' : 'Export Evidence Pack'}
            </button>
            <button
              type="button"
              className="btn btn-danger"
              aria-label="Cancel run and stop further execution"
              disabled={!canControlRuns}
              title={canControlRuns ? undefined : 'Operator or Admin role required'}
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
