interface ErrorStateProps {
  message?: string;
  onRetry?: () => void;
  className?: string;
}

export function ErrorState({ message, onRetry, className }: ErrorStateProps) {
  return (
    <section className={`panel error-state ${className ?? ''}`.trim()} role="alert">
      <h2>Unable to load data</h2>
      <p>{message ?? 'Please retry or contact support if the problem persists.'}</p>
      {onRetry ? (
        <button type="button" className="btn btn-secondary" onClick={onRetry}>
          Retry
        </button>
      ) : null}
    </section>
  );
}
