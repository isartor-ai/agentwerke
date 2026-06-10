interface KpiCardProps {
  label: string;
  value: string | number;
  accent: 'running' | 'completed' | 'awaiting' | 'failed' | 'blocked' | 'pending';
  className?: string;
}

export function KpiCard({ label, value, accent, className }: KpiCardProps) {
  return (
    <article className={`kpi-card kpi-${accent} ${className ?? ''}`.trim()}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </article>
  );
}
