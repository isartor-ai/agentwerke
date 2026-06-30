interface KpiCardProps {
  label: string;
  value: string | number;
  accent: 'running' | 'completed' | 'awaiting' | 'failed' | 'blocked' | 'pending';
  hint?: string;
  className?: string;
}

export function KpiCard({ label, value, accent, hint, className }: KpiCardProps) {
  const accessibleLabel = `${label}: ${value}${hint ? `. ${hint}` : ''}`;

  return (
    <article className={`kpi-card kpi-${accent} ${className ?? ''}`.trim()} aria-label={accessibleLabel}>
      <dl>
        <div>
          <dt>{label}</dt>
          <dd>{value}</dd>
        </div>
      </dl>
      {hint ? <p className="kpi-card-hint">{hint}</p> : null}
    </article>
  );
}
