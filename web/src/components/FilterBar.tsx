import type { RiskLevel, RunStatus } from '../types';

interface FilterBarProps {
  status: 'all' | RunStatus;
  risk: 'all' | RiskLevel;
  search?: string;
  onStatusChange: (status: 'all' | RunStatus) => void;
  onRiskChange: (risk: 'all' | RiskLevel) => void;
  onSearchChange?: (search: string) => void;
}

const statuses: Array<'all' | RunStatus> = [
  'all',
  'running',
  'awaiting_approval',
  'waiting_external',
  'failed',
  'completed',
  'blocked',
  'pending',
  'cancelled',
];

const risks: Array<'all' | RiskLevel> = ['all', 'critical', 'high', 'medium', 'low', 'none'];

function formatLabel(value: string): string {
  return value.replace('_', ' ').replace(/\b\w/g, (char) => char.toUpperCase());
}

export function FilterBar({
  status,
  risk,
  search,
  onStatusChange,
  onRiskChange,
  onSearchChange,
}: FilterBarProps) {
  return (
    <section className="filter-bar" aria-label="Run filters">
      {onSearchChange ? (
        <div className="filter-search">
          <label htmlFor="run-ledger-search" className="filter-title">Search</label>
          <input
            id="run-ledger-search"
            type="search"
            value={search ?? ''}
            placeholder="Filter by run, workflow, owner, step, or tag"
            onChange={(event) => onSearchChange(event.target.value)}
          />
        </div>
      ) : null}
      <div>
        <span className="filter-title">Status</span>
        <div className="chip-group" role="group" aria-label="Status filters">
          {statuses.map((item) => (
            <button
              key={item}
              type="button"
              className={`chip ${status === item ? 'chip-active' : ''}`}
              onClick={() => onStatusChange(item)}
            >
              {formatLabel(item)}
            </button>
          ))}
        </div>
      </div>
      <div>
        <span className="filter-title">Risk</span>
        <div className="chip-group" role="group" aria-label="Risk filters">
          {risks.map((item) => (
            <button
              key={item}
              type="button"
              className={`chip ${risk === item ? 'chip-active' : ''}`}
              onClick={() => onRiskChange(item)}
            >
              {formatLabel(item)}
            </button>
          ))}
        </div>
      </div>
    </section>
  );
}
