import { useEffect, useMemo, useState } from 'react';
import { apiClient } from '../api/client';
import { DetailDrawer } from '../components/DetailDrawer';
import { EmptyState } from '../components/EmptyState';
import { ErrorState } from '../components/ErrorState';
import { KpiCard } from '../components/KpiCard';
import { LoadingState } from '../components/LoadingState';
import { PageHeader } from '../components/PageHeader';
import { RiskBadge } from '../components/RiskBadge';
import type { ApprovalRequest } from '../types';

function minutesRemaining(deadline: string): number {
  return Math.max(0, Math.floor((new Date(deadline).getTime() - Date.now()) / 60_000));
}

export function ApprovalsDashboard() {
  const [approvals, setApprovals] = useState<ApprovalRequest[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [decisionComment, setDecisionComment] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadApprovals = async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await apiClient.getApprovals();
      setApprovals(data);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'Unknown error');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadApprovals();
    const timer = setInterval(loadApprovals, 15_000);
    return () => clearInterval(timer);
  }, []);

  const selectedApproval = approvals.find((item) => item.id === selectedId) ?? null;

  const pending = approvals.filter((approval) => approval.status === 'pending');
  const highCritical = pending.filter(
    (approval) => approval.riskLevel === 'high' || approval.riskLevel === 'critical',
  ).length;
  const breached = pending.filter((approval) => minutesRemaining(approval.slaDeadline) <= 0).length;

  const submitDecision = async (decision: 'approve' | 'reject' | 'escalate') => {
    if (!selectedApproval) {
      return;
    }

    if (decision === 'reject' && !decisionComment.trim()) {
      setError('A reason is required when rejecting an approval request.');
      return;
    }

    await apiClient.decideApproval(selectedApproval.id, decision, decisionComment);
    setDecisionComment('');
    setSelectedId(null);
    await loadApprovals();
  };

  const sortedPending = useMemo(() => {
    return [...pending].sort((a, b) => {
      const priorityWeight: Record<ApprovalRequest['priority'], number> = {
        urgent: 3,
        high: 2,
        normal: 1,
      };
      return priorityWeight[b.priority] - priorityWeight[a.priority];
    });
  }, [pending]);

  if (loading) {
    return <LoadingState message="Loading approvals" />;
  }

  if (error && !selectedApproval) {
    return <ErrorState message={error} onRetry={loadApprovals} />;
  }

  return (
    <section>
      <PageHeader
        title="Approvals"
        description="Human-in-the-loop review queue for agent actions."
        actions={
          <button type="button" className="btn btn-secondary" onClick={loadApprovals}>
            Refresh
          </button>
        }
      />

      <section className="kpi-grid" aria-label="Approval summary metrics">
        <KpiCard label="Pending" value={pending.length} accent="awaiting" />
        <KpiCard label="High/Critical" value={highCritical} accent="failed" />
        <KpiCard label="SLA Breached" value={breached} accent="blocked" />
        <KpiCard label="Total" value={approvals.length} accent="pending" />
      </section>

      {sortedPending.length === 0 ? (
        <EmptyState title="No pending approvals" description="Approval queue is currently clear." />
      ) : (
        <div className="approval-list">
          {sortedPending.map((approval) => (
            <article key={approval.id} className="panel approval-card">
              <div className="approval-card-head">
                <strong>{approval.workflowName}</strong>
                <RiskBadge level={approval.riskLevel} score={approval.riskScore} />
              </div>
              <p>{approval.actionRequested}</p>
              <p className="cell-meta">
                Requested by {approval.requester} via {approval.agentName}
              </p>
              <p>{approval.policyRationale}</p>
              <div className="tag-row">
                {approval.affectedSystems.map((system) => (
                  <span key={system} className="chip chip-static">
                    {system}
                  </span>
                ))}
              </div>
              <div className="approval-footer">
                <span>{minutesRemaining(approval.slaDeadline)}m remaining</span>
                <button type="button" className="btn btn-secondary" onClick={() => setSelectedId(approval.id)}>
                  Review
                </button>
              </div>
            </article>
          ))}
        </div>
      )}

      <DetailDrawer
        open={Boolean(selectedApproval)}
        onClose={() => setSelectedId(null)}
        title="Approval Request"
      >
        {selectedApproval ? (
          <div className="drawer-body">
            <p>
              <strong>Action:</strong> {selectedApproval.actionRequested}
            </p>
            <p>
              <strong>Workflow:</strong> {selectedApproval.workflowName} ({selectedApproval.runId})
            </p>
            <p>
              <strong>Requester:</strong> {selectedApproval.requester}
            </p>
            <p>
              <strong>Agent:</strong> {selectedApproval.agentName}
            </p>
            <p>
              <strong>Policy rationale:</strong> {selectedApproval.policyRationale}
            </p>
            <p>
              <strong>SLA:</strong> {new Date(selectedApproval.slaDeadline).toLocaleString()}
            </p>

            <label htmlFor="decision-comment">Reason / comment (required for reject)</label>
            <textarea
              id="decision-comment"
              value={decisionComment}
              onChange={(event) => setDecisionComment(event.target.value)}
              rows={4}
            />

            <div className="action-row">
              <button type="button" className="btn btn-primary" onClick={() => submitDecision('approve')}>
                Approve
              </button>
              <button type="button" className="btn btn-danger" onClick={() => submitDecision('reject')}>
                Reject
              </button>
              <button type="button" className="btn btn-secondary" onClick={() => submitDecision('escalate')}>
                Escalate
              </button>
            </div>
          </div>
        ) : null}
      </DetailDrawer>
    </section>
  );
}
