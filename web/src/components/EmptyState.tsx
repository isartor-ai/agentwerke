import type { ReactNode } from 'react';

interface EmptyStateProps {
  title: string;
  description: string;
  action?: ReactNode;
  className?: string;
  variant?: 'panel' | 'inline';
}

export function EmptyState({
  title,
  description,
  action,
  className,
  variant = 'panel',
}: EmptyStateProps) {
  const classes = `${variant === 'panel' ? 'panel empty-state' : 'empty-state empty-state-inline'} ${className ?? ''}`.trim();

  return (
    <section className={classes}>
      <h2>{title}</h2>
      <p>{description}</p>
      {action ? <div className="empty-state-action">{action}</div> : null}
    </section>
  );
}
