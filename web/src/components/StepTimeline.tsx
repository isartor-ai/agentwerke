import type { RunStep } from '../types';
import { formatTokenCount } from '../utils/tokens';

interface StepTimelineProps {
  steps: RunStep[];
  expandedStepId: string | null;
  onToggleStep: (stepId: string) => void;
  /** Number of agent-conversation interactions produced by each step (#192). */
  interactionCountByStep?: Record<string, number>;
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
    case 'needs_config':
      return 'timeline-dot-awaiting';
    default:
      return 'timeline-dot-pending';
  }
}

export function StepTimeline({ steps, expandedStepId, onToggleStep, interactionCountByStep }: StepTimelineProps) {
  return (
    <section className="panel timeline-panel" aria-label="Execution timeline">
      <h2>Execution Timeline</h2>
      <ol className="timeline" role="list">
        {steps.map((step) => {
          const expanded = expandedStepId === step.id;
          const interactionCount = interactionCountByStep?.[step.id] ?? 0;
          const tokenUsage = step.runtimeSnapshot?.tokenUsage;
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
                {tokenUsage ? (
                  <span
                    className="chip chip-static timeline-token-badge"
                    title={`Model tokens — input: ${tokenUsage.inputTokens.toLocaleString()}, output: ${tokenUsage.outputTokens.toLocaleString()}`}
                  >
                    {formatTokenCount(tokenUsage.inputTokens)} in · {formatTokenCount(tokenUsage.outputTokens)} out
                  </span>
                ) : null}
                {interactionCount > 0 ? (
                  <span className="chip chip-static timeline-interaction-badge">
                    {interactionCount} message{interactionCount === 1 ? '' : 's'}
                  </span>
                ) : null}
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
