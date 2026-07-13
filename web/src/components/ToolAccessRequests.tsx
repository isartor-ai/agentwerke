import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { apiClient } from '../api/client';
import type { ToolAccessRequest } from '../types';

interface ToolAccessRequestsProps {
  canDecide: boolean;
  onToast: (toast: { tone: 'success' | 'error'; title: string; message: string }) => void;
  /** Notifies the parent so it can surface the pending count in its KPIs. */
  onCountChange?: (count: number) => void;
}

/**
 * Pending tool-access escalations (#202): an agent asked for a tool its step does not allow
 * and the run is suspended. Approve allows the tool for the rest of the run, Fail step fails
 * the step, and any guidance text is fed back to the model as the tool result (a denial).
 */
export function ToolAccessRequests({ canDecide, onToast, onCountChange }: ToolAccessRequestsProps) {
  const [requests, setRequests] = useState<ToolAccessRequest[]>([]);
  const [guidanceById, setGuidanceById] = useState<Record<string, string>>({});
  const [submittingId, setSubmittingId] = useState<string | null>(null);
  const onCountChangeRef = useRef(onCountChange);
  onCountChangeRef.current = onCountChange;

  const load = useCallback(async () => {
    try {
      const data = await apiClient.getToolAccessRequests();
      setRequests(data);
      onCountChangeRef.current?.(data.length);
    } catch {
      // Non-blocking side list: the main approvals load surfaces connectivity errors.
    }
  }, []);

  useEffect(() => {
    void load();
    const timer = setInterval(() => void load(), 15_000);
    return () => clearInterval(timer);
  }, [load]);

  const answer = async (request: ToolAccessRequest, response: string, label: string) => {
    try {
      setSubmittingId(request.interactionId);
      await apiClient.answerInteraction(request.runId, request.interactionId, response);
      onToast({
        tone: 'success',
        title: label,
        message: `Tool '${request.toolName ?? 'unknown'}' for ${request.agent} — the step resumes with this decision.`,
      });
      setGuidanceById((current) => {
        const next = { ...current };
        delete next[request.interactionId];
        return next;
      });
      await load();
    } catch (error) {
      onToast({
        tone: 'error',
        title: 'Tool access decision failed',
        message: error instanceof Error ? error.message : 'Unable to submit the decision.',
      });
    } finally {
      setSubmittingId(null);
    }
  };

  if (requests.length === 0) {
    return null;
  }

  return (
    <section className="panel" aria-label="Tool access requests">
      <h2>Tool Access Requests</h2>
      <p className="cell-meta">
        Runs suspended because an agent needs a tool its step does not allow. Approve grants the
        tool for the rest of the run; guidance is returned to the agent as a denial; Fail step
        fails the step.
      </p>
      <div className="approval-list">
        {requests.map((request) => {
          const guidance = guidanceById[request.interactionId] ?? '';
          const busy = submittingId === request.interactionId;
          return (
            <article key={request.interactionId} className="panel approval-card">
              <div className="approval-card-head">
                <strong>
                  Agent {request.agent} needs tool {request.toolName ?? '(unknown)'}
                </strong>
                <span className="chip chip-static">tool access</span>
              </div>
              <p className="cell-meta">
                {request.workflowName ?? 'Workflow'} ·{' '}
                <Link to={`/runs/${request.runId}`}>{request.runId}</Link>
                {request.stepName ? ` · step: ${request.stepName}` : request.stepId ? ` · step: ${request.stepId}` : ''}
                {' · '}
                {new Date(request.createdAt).toLocaleString()}
              </p>
              {request.intent ? (
                <p className="cell-meta">
                  Stated intent: <code>{request.intent}</code>
                </p>
              ) : null}
              {canDecide ? (
                <>
                  <label htmlFor={`guidance-${request.interactionId}`}>
                    Guidance for the agent (sent as a denial)
                  </label>
                  <textarea
                    id={`guidance-${request.interactionId}`}
                    rows={2}
                    value={guidance}
                    onChange={(event) =>
                      setGuidanceById((current) => ({
                        ...current,
                        [request.interactionId]: event.target.value,
                      }))
                    }
                    placeholder="e.g. Use github.comment_issue instead and summarize the review there."
                  />
                  <div className="action-row">
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={busy}
                      onClick={() => void answer(request, 'approve', 'Tool access approved')}
                    >
                      Approve for this run
                    </button>
                    <button
                      type="button"
                      className="btn btn-secondary"
                      disabled={busy || !guidance.trim()}
                      onClick={() => void answer(request, guidance.trim(), 'Guidance sent')}
                    >
                      Deny with guidance
                    </button>
                    <button
                      type="button"
                      className="btn btn-danger"
                      disabled={busy}
                      onClick={() => void answer(request, 'fail', 'Step failed')}
                    >
                      Fail step
                    </button>
                  </div>
                </>
              ) : (
                <p className="cell-meta">Approver role required to decide tool access requests.</p>
              )}
            </article>
          );
        })}
      </div>
    </section>
  );
}
