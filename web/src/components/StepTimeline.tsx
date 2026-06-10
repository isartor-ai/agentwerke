import type { RunStep } from '../types';

interface StepTimelineProps {
  steps: RunStep[];
  expandedStepId: string | null;
  onToggleStep: (stepId: string) => void;
}

function stepStateClass(status: RunStep['status']): string {
  switch (status) {
    case 'completed':
      return 'timeline-dot-completed';
    case 'running':
      return 'timeline-dot-running';
    case 'failed':
    case 'blocked':
      return 'timeline-dot-failed';
    case 'awaiting_approval':
      return 'timeline-dot-awaiting';
    default:
      return 'timeline-dot-pending';
  }
}

export function StepTimeline({ steps, expandedStepId, onToggleStep }: StepTimelineProps) {
  return (
    <section className="panel timeline-panel" aria-label="Execution timeline">
      <h2>Execution Timeline</h2>
      <ol className="timeline" role="list">
        {steps.map((step) => {
          const expanded = expandedStepId === step.id;
          return (
            <li key={step.id} className="timeline-item">
              <button
                type="button"
                className="timeline-trigger"
                aria-expanded={expanded}
                onClick={() => onToggleStep(step.id)}
              >
                <span className={`timeline-dot ${stepStateClass(step.status)}`} aria-hidden="true" />
                <span className="timeline-main">
                  <strong>{step.name}</strong>
                  <span>{step.status.replace('_', ' ')}</span>
                </span>
              </button>
              {expanded ? (
                <div className="timeline-details">
                  {step.agentName ? <p>Agent: {step.agentName}</p> : null}
                  {step.output ? <p>Output: {step.output}</p> : null}
                  {step.error ? <p>Error: {step.error}</p> : null}
                  {step.policyDecision ? (
                    <div className="policy-box">
                      <p>
                        Policy {step.policyDecision.policyName} decided {step.policyDecision.kind}.
                      </p>
                      <p>{step.policyDecision.rationale}</p>
                      <p>
                        Risk score {step.policyDecision.riskScore} ({step.policyDecision.riskLevel})
                      </p>
                    </div>
                  ) : null}
                </div>
              ) : null}
            </li>
          );
        })}
      </ol>
    </section>
  );
}
