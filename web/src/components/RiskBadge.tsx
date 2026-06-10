import type { RiskLevel } from '../types';

interface RiskBadgeProps {
  level: RiskLevel;
  score?: number;
  className?: string;
}

const riskLabel: Record<RiskLevel, string> = {
  critical: 'Critical',
  high: 'High',
  medium: 'Medium',
  low: 'Low',
  none: 'None',
};

export function RiskBadge({ level, score, className }: RiskBadgeProps) {
  return (
    <span className={`risk-badge risk-${level} ${className ?? ''}`.trim()}>
      {riskLabel[level]}{typeof score === 'number' ? ` ${score}` : ''}
    </span>
  );
}
