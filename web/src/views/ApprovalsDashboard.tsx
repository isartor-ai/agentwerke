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
  const [statusFilter, setStatusFilter] = useState<'all' | ApprovalRequest['status']>('pending');
  const [searchTerm, setSearchTerm] = useState('');
  const [submittingDecision, setSubmittingDecision] = useState(false);
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

    try {
      setSubmittingDecision(true);
      setError(null);
      await apiClient.decideApproval(selectedApproval.id, decision, decisionComment);
      setDecisionComment('');
      setSelectedId(null);
      await loadApprovals();
    } catch (submitError) {
      setError(submitError instanceof Error ? submitError.message : 'Unable to submit approval decision.');
    } finally {
      setSubmittingDecision(false);
    }
  };

  const filteredApprovals = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();

    return approvals.filter((approval) => {
      const statusMatches = statusFilter === 'all' || approval.status === statusFilter;
      const queryMatches =
        query.length === 0 ||
        approval.workflowName.toLowerCase().includes(query) ||
        approval.actionRequested.toLowerCase().includes(query) ||
        approval.requester.toLowerCase().includes(query) ||
        approval.agentName.toLowerCase().includes(query);

      return statusMatches && queryMatches;
    });
  }, [approvals, searchTerm, statusFilter]);

  const sortedApprovals = useMemo(() => {
    return [...filteredApprovals].sort((a, b) => {
      const priorityWeight: Record<ApprovalRequest['priority'], number> = {
        urgent: 3,
        high: 2,
        normal: 1,
      };
      const statusWeight = (approval: ApprovalRequest) => (approval.status === 'pending' ? 1 : 0);
      return (
        statusWeight(b) - statusWeight(a) ||
        priorityWeight[b.priority] - priorityWeight[a.priority] ||
        new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
      );
    });
  }, [filteredApprovals]);

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

      <section className="filter-bar" aria-label="Approval filters">
        <div>
          <span className="filter-title">Status</span>
          <div className="chip-group" role="group" aria-label="Approval status filters">
            {(['pending', 'approved', 'rejected', 'escalated'] as const).map((status) => (
              <button
                key={status}
                type="button"
                className={`chip ${statusFilter === status ? 'chip-active' : ''}`}
                onClick={() => setStatusFilter(status)}
              >
                {status.replace('_', ' ')}
              </button>
            ))}
            <button
              type="button"
              className={`chip ${statusFilter === 'all' ? 'chip-active' : ''}`}
              onClick={() => setStatusFilter('all')}
            >
              all
            </button>
          </div>
        </div>
        <div>
          <span className="filter-title">Search</span>
          <input
            type="search"
            value={searchTerm}
            onChange={(event) => setSearchTerm(event.target.value)}
            placeholder="Find workflow, action, requester, or agent"
          />
        </div>
      </section>

      {sortedApprovals.length === 0 ? (
        <EmptyState
          title="No approvals found"
          description="Try a different status filter or search query."
        />
      ) : (
        <div className="approval-list">
          {sortedApprovals.map((approval) => (
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
                <span>
                  {approval.status === 'pending'
                    ? `${minutesRemaining(approval.slaDeadline)}m remaining`
                    : `Decision status: ${approval.status}`}
                </span>
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
            {error ? <p>{error}</p> : null}
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
              <button
                type="button"
                className="btn btn-primary"
                disabled={submittingDecision || selectedApproval.status !== 'pending'}
                onClick={() => submitDecision('approve')}
              >
                Approve
              </button>
              <button
                type="button"
                className="btn btn-danger"
                disabled={submittingDecision || selectedApproval.status !== 'pending'}
                onClick={() => submitDecision('reject')}
              >
                Reject
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                disabled={submittingDecision || selectedApproval.status !== 'pending'}
                onClick={() => submitDecision('escalate')}
              >
                Escalate
              </button>
            </div>
          </div>
        ) : null}
      </DetailDrawer>
    </section>
  );
}
