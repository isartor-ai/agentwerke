interface LoadingStateProps {
  message?: string;
  className?: string;
}

export function LoadingState({ message, className }: LoadingStateProps) {
  return (
    <section className={`panel loading-state ${className ?? ''}`.trim()} role="status" aria-live="polite">
      <div className="spinner" aria-hidden="true" />
      <p>{message ?? 'Loading data...'}</p>
    </section>
  );
}
