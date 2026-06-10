import type { RunStatus } from '../types';

interface StatusBadgeProps {
  status: RunStatus;
  className?: string;
}

const statusLabel: Record<RunStatus, string> = {
  running: 'Running',
  completed: 'Completed',
  failed: 'Failed',
  pending: 'Pending',
  cancelled: 'Cancelled',
  blocked: 'Blocked',
  awaiting_approval: 'Awaiting Approval',
};

export function StatusBadge({ status, className }: StatusBadgeProps) {
  return (
    <span className={`status-badge status-${status} ${className ?? ''}`.trim()}>
      {statusLabel[status]}
    </span>
  );
}
