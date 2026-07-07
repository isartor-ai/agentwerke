import type { RunStep } from '../types';
import { formatTokenCount } from '../utils/tokens';
import { AgentIdentityBadge } from './AgentIdentityBadge';
import { ModelActivityDetails } from './ModelActivityDetails';

interface StepTimelineProps {
  steps: RunStep[];
  expandedStepId: string | null;
  onToggleStep: (stepId: string) => void;
  /** Number of agent-conversation interactions produced by each step (#192). */
  interactionCountByStep?: Record<string, number>;
  /** Visible reasoning/progress summaries emitted by each step while it runs. */
  reasoningByStep?: Record<string, string[]>;
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

interface CumulativeTokens {
  inputTokens: number;
  outputTokens: number;
}

/**
 * Running total of model tokens up to and including each step, in timeline order (#200).
 * Failed steps report their partial usage too, so the cumulative reflects real spend.
 */
function cumulativeTokensByStep(steps: RunStep[]): Record<string, CumulativeTokens> {
  const totals: Record<string, CumulativeTokens> = {};
  let inputTokens = 0;
  let outputTokens = 0;
  for (const step of steps) {
    const usage = step.runtimeSnapshot?.tokenUsage;
    if (usage) {
      inputTokens += usage.inputTokens;
      outputTokens += usage.outputTokens;
    }
    totals[step.id] = { inputTokens, outputTokens };
  }
  return totals;
}

function uniqueNonEmpty(items: Array<string | null | undefined>): string[] {
  const seen = new Set<string>();
  const values: string[] = [];
  for (const item of items) {
    const value = item?.trim();
    if (!value || seen.has(value)) continue;
    seen.add(value);
    values.push(value);
  }
  return values;
}

export function StepTimeline({
  steps,
  expandedStepId,
  onToggleStep,
  interactionCountByStep,
  reasoningByStep,
}: StepTimelineProps) {
  const cumulativeByStep = cumulativeTokensByStep(steps);
  return (
    <section className="panel timeline-panel" aria-label="Execution timeline">
      <h2>Execution Timeline</h2>
      <ol className="timeline" role="list">
        {steps.map((step) => {
          const expanded = expandedStepId === step.id;
          const interactionCount = interactionCountByStep?.[step.id] ?? 0;
          const tokenUsage = step.runtimeSnapshot?.tokenUsage;
          const cumulative = cumulativeByStep[step.id];
          const cumulativeTotal = cumulative.inputTokens + cumulative.outputTokens;
          const agentName = step.agentName ?? step.runtimeSnapshot?.agentName;
          const modelTraceCount = step.runtimeSnapshot?.modelTraces?.length ?? 0;
          const reasoningItems = uniqueNonEmpty(reasoningByStep?.[step.id] ?? []);
          const traceReasoningCount = uniqueNonEmpty(
            (step.runtimeSnapshot?.modelTraces ?? []).map((trace) => trace.reasoningSummary),
          ).length;
          const reasoningCount = reasoningItems.length + traceReasoningCount;
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
                {agentName ? (
                  <AgentIdentityBadge name={agentName} className="timeline-agent-badge" />
                ) : null}
                {tokenUsage ? (
                  <span
                    className="chip chip-static timeline-token-badge"
                    title={`Model tokens this step — input: ${tokenUsage.inputTokens.toLocaleString()}, output: ${tokenUsage.outputTokens.toLocaleString()}`}
                  >
                    {formatTokenCount(tokenUsage.inputTokens)} in · {formatTokenCount(tokenUsage.outputTokens)} out
                  </span>
                ) : null}
                {cumulativeTotal > 0 ? (
                  <span
                    className="chip chip-static timeline-cumulative-badge"
                    title={`Tokens used so far (through this step) — input: ${cumulative.inputTokens.toLocaleString()}, output: ${cumulative.outputTokens.toLocaleString()}`}
                  >
                    Σ {formatTokenCount(cumulativeTotal)}
                  </span>
                ) : null}
                {modelTraceCount > 0 ? (
                  <span className="chip chip-static timeline-model-badge">
                    LLM {modelTraceCount} trace{modelTraceCount === 1 ? '' : 's'}
                  </span>
                ) : null}
                {reasoningCount > 0 ? (
                  <span className="chip chip-static timeline-reasoning-badge">
                    Reasoning {reasoningCount}
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
                  {agentName ? (
                    <div className="timeline-detail-row">
                      <span className="timeline-detail-label">Agent</span>
                      <AgentIdentityBadge name={agentName} />
                    </div>
                  ) : null}
                  {tokenUsage ? (
                    <p>
                      Tokens this step: {tokenUsage.inputTokens.toLocaleString()} in ·{' '}
                      {tokenUsage.outputTokens.toLocaleString()} out
                    </p>
                  ) : null}
                  {cumulativeTotal > 0 ? (
                    <p>
                      Run total after this step: {cumulative.inputTokens.toLocaleString()} in ·{' '}
                      {cumulative.outputTokens.toLocaleString()} out
                    </p>
                  ) : null}
                  {reasoningItems.length > 0 ? (
                    <section className="timeline-reasoning-log" aria-label="Visible agent reasoning">
                      <h3>Visible Reasoning</h3>
                      <ol role="list">
                        {reasoningItems.map((item) => (
                          <li key={item}>{item}</li>
                        ))}
                      </ol>
                    </section>
                  ) : step.status === 'running' ? (
                    <section className="timeline-reasoning-log" aria-label="Visible agent reasoning" aria-live="polite">
                      <h3>Visible Reasoning</h3>
                      <p>Agent is preparing the model/tool loop for this step.</p>
                    </section>
                  ) : null}
                  <ModelActivityDetails
                    modelTraces={step.runtimeSnapshot?.modelTraces}
                    sectionClassName="timeline-model-work"
                    headingClassName="timeline-model-heading"
                  />
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
