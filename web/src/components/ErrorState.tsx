interface ErrorStateProps {
  title?: string;
  message?: string;
  onRetry?: () => void;
  retryLabel?: string;
  className?: string;
  variant?: 'panel' | 'inline';
}

export function ErrorState({
  title = 'Unable to load data',
  message,
  onRetry,
  retryLabel = 'Retry',
  className,
  variant = 'panel',
}: ErrorStateProps) {
  const classes = `${variant === 'panel' ? 'panel error-state' : 'error-state error-state-inline'} ${className ?? ''}`.trim();

  return (
    <section className={classes} role="alert">
      <h2>{title}</h2>
      <p>{message ?? 'Please retry or contact support if the problem persists.'}</p>
      {onRetry ? (
        <button type="button" className="btn btn-secondary" onClick={onRetry}>
          {retryLabel}
        </button>
      ) : null}
    </section>
  );
}
