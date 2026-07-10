import { useState } from 'react';
import type { RunEvent, RunStep } from '../types';
import type { AgentIdentityConfig } from '../utils/agentIdentity';
import { formatTokenCount } from '../utils/tokens';
import {
  getStepEventsForDisplay,
  mergeVisibleReasoningEntries,
  type VisibleReasoningEntry,
} from '../utils/visibleReasoning';
import { AgentIdentityBadge } from './AgentIdentityBadge';
import { ModelActivityDetails } from './ModelActivityDetails';
import { RiskBadge } from './RiskBadge';
import { SandboxExecutionDetails } from './SandboxExecutionDetails';
import { VisibleReasoningLog } from './VisibleReasoningLog';

interface StepTimelineProps {
  steps: RunStep[];
  events?: RunEvent[];
  expandedStepId: string | null;
  onToggleStep: (stepId: string) => void;
  /** Number of agent-conversation interactions produced by each step (#192). */
  interactionCountByStep?: Record<string, number>;
  /** Visible reasoning/progress summaries emitted by each step while it runs. */
  reasoningByStep?: Record<string, VisibleReasoningEntry[]>;
  resolveAgentIdentity?: (name: string) => AgentIdentityConfig | undefined;
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

const THINKING_KINDS = new Set(['started', 'reasoning', 'recorded']);

/** Split a step's reasoning entries into thinking (chain of thought) and tool activity. */
function splitReasoning(entries: VisibleReasoningEntry[]) {
  const thinking: VisibleReasoningEntry[] = [];
  const activity: VisibleReasoningEntry[] = [];
  for (const entry of entries) {
    (THINKING_KINDS.has(entry.kind) ? thinking : activity).push(entry);
  }
  return { thinking, activity };
}

type StepTab = 'thinking' | 'activity' | 'io' | 'policy' | 'runtime' | 'events';

interface StepDetailProps {
  step: RunStep;
  stepEvents: RunEvent[];
  agentName?: string;
  agentIdentity?: AgentIdentityConfig;
  tokenUsage?: { inputTokens: number; outputTokens: number };
  cumulative: CumulativeTokens;
  reasoningItems: VisibleReasoningEntry[];
  interactionCount: number;
}

/** Expanded step body: one thing at a time behind a tab strip, Thinking first. */
function StepDetail({
  step,
  stepEvents,
  agentName,
  agentIdentity,
  tokenUsage,
  cumulative,
  reasoningItems,
  interactionCount,
}: StepDetailProps) {
  const isRunning = step.status === 'running';
  const { thinking, activity } = splitReasoning(reasoningItems);
  const modelTraces = step.runtimeSnapshot?.modelTraces ?? [];
  const toolCallCount =
    modelTraces.reduce((sum, trace) => sum + (trace.toolCalls?.length ?? 0), 0);
  const hasOutput = Boolean(
    step.output ||
    step.error ||
    step.runtimeSnapshot?.promptInline ||
    (step.runtimeSnapshot?.stepArtifacts?.length ?? 0) > 0,
  );
  const hasPolicy = Boolean(step.policyDecision);
  const hasRuntime = Boolean(step.startedAt || step.completedAt || step.runtimeSnapshot);

  const tabs: { id: StepTab; label: string; show: boolean }[] = [
    { id: 'thinking', label: 'Thinking', show: thinking.length > 0 || isRunning },
    { id: 'activity', label: 'Activity', show: activity.length > 0 || modelTraces.length > 0 },
    { id: 'io', label: 'I/O', show: hasOutput },
    { id: 'policy', label: 'Policy', show: hasPolicy },
    { id: 'runtime', label: 'Runtime', show: hasRuntime },
    { id: 'events', label: 'Events', show: stepEvents.length > 0 },
  ];
  const available = tabs.filter((t) => t.show);
  const [active, setActive] = useState<StepTab>(available[0]?.id ?? 'details');
  const current = available.some((t) => t.id === active) ? active : available[0]?.id ?? 'details';

  return (
    <div className="timeline-details">
      <div className="step-tabs" role="tablist" aria-label={`${step.name} detail`}>
        {available.map((tab) => (
          <button
            key={tab.id}
            type="button"
            role="tab"
            aria-selected={current === tab.id}
            className={`step-tab ${current === tab.id ? 'step-tab-active' : ''}`}
            onClick={() => setActive(tab.id)}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <div className="step-tab-panel" role="tabpanel">
        {current === 'thinking' ? (
          <>
            <VisibleReasoningLog entries={thinking} isRunning={isRunning} title="Visible Reasoning" />
            {activity.length > 0 ? (
              <VisibleReasoningLog
                entries={activity}
                isRunning={isRunning}
                title="Live Actions"
              />
            ) : null}
          </>
        ) : null}

        {current === 'activity' ? (
          <>
            <VisibleReasoningLog entries={activity} isRunning={isRunning} title="Tool Activity" />
            <ModelActivityDetails
              modelTraces={modelTraces}
              sectionClassName="timeline-model-work"
              headingClassName="timeline-model-heading"
            />
          </>
        ) : null}

        {current === 'io' ? (
          <>
            {step.output ? (
              <section>
                <h4 className="timeline-section-label">Output</h4>
                <p className="step-output">{step.output}</p>
              </section>
            ) : null}
            {step.error ? (
              <section>
                <h4 className="timeline-section-label">Error</h4>
                <p className="step-output step-output-error">{step.error}</p>
              </section>
            ) : null}
            {step.runtimeSnapshot?.promptInline ? (
              <details className="policy-box adp-collapsible" aria-label={`${step.name} prompt`}>
                <summary className="adp-collapsible-summary">
                  <h4 className="timeline-section-label">Prompt</h4>
                  <span className="adp-collapsible-meta">collapsed by default</span>
                </summary>
                <pre className="adp-pre">{step.runtimeSnapshot.promptInline}</pre>
              </details>
            ) : null}
            {(step.runtimeSnapshot?.stepArtifacts?.length ?? 0) > 0 ? (
              <section className="policy-box">
                <h4 className="timeline-section-label">Step Artifacts</h4>
                <ul className="event-list" role="list">
                  {step.runtimeSnapshot!.stepArtifacts.map((artifact) => (
                    <li key={artifact.name}>
                      <strong>{artifact.name}</strong>
                      {artifact.contentType ? <p>{artifact.contentType}</p> : null}
                      {artifact.uri ? (
                        <a className="btn btn-secondary" href={artifact.uri}>
                          Download
                        </a>
                      ) : null}
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}
          </>
        ) : null}

        {current === 'policy' ? (
          <>
            {step.policyDecision ? (
              <section className="policy-box">
                <h4 className="timeline-section-label">Policy Decision</h4>
                <p>
                  {step.policyDecision.policyName} decided {step.policyDecision.kind}.
                </p>
                <p>{step.policyDecision.rationale}</p>
                <p>
                  <RiskBadge
                    level={step.policyDecision.riskLevel}
                    score={step.policyDecision.riskScore}
                  />
                </p>
              </section>
            ) : null}
          </>
        ) : null}

        {current === 'runtime' ? (
          <>
            {agentName ? (
              <div className="timeline-detail-row">
                <span className="timeline-detail-label">Agent</span>
                <AgentIdentityBadge name={agentName} identity={agentIdentity} isRunning={isRunning} />
              </div>
            ) : null}
            {step.runtimeSnapshot ? (
              <section className="policy-box">
                <h4 className="timeline-section-label">Execution</h4>
                <dl className="definition-list">
                  <div><dt>Mode</dt><dd>{step.runtimeSnapshot.executionMode}</dd></div>
                  <div>
                    <dt>LLM in sandbox</dt>
                    <dd>{step.runtimeSnapshot.executionMode === 'agent_sandboxed' ? 'yes' : 'no'}</dd>
                  </div>
                </dl>
              </section>
            ) : null}
            {tokenUsage ? (
              <p>
                Tokens this step: {tokenUsage.inputTokens.toLocaleString()} in ·{' '}
                {tokenUsage.outputTokens.toLocaleString()} out
              </p>
            ) : null}
            {cumulative.inputTokens + cumulative.outputTokens > 0 ? (
              <p>
                Run total after this step: {cumulative.inputTokens.toLocaleString()} in ·{' '}
                {cumulative.outputTokens.toLocaleString()} out
              </p>
            ) : null}
            {(step.startedAt || step.completedAt) ? (
              <section className="policy-box">
                <h4 className="timeline-section-label">Timing</h4>
                <dl className="definition-list">
                  {step.startedAt ? (
                    <div><dt>Started</dt><dd>{new Date(step.startedAt).toLocaleString()}</dd></div>
                  ) : null}
                  {step.completedAt ? (
                    <div><dt>Completed</dt><dd>{new Date(step.completedAt).toLocaleString()}</dd></div>
                  ) : null}
                  {step.durationMs != null ? (
                    <div><dt>Duration</dt><dd>{step.durationMs.toLocaleString()} ms</dd></div>
                  ) : null}
                </dl>
              </section>
            ) : null}
            {(step.runtimeSnapshot?.skills?.length ?? 0) > 0 ? (
              <section className="policy-box">
                <h4 className="timeline-section-label">Skills</h4>
                <div className="tag-row">
                  {step.runtimeSnapshot!.skills.map((skill) => (
                    <span key={skill.skillId} className="chip chip-static">
                      {skill.selected ? '✓ ' : ''}
                      {skill.name ?? skill.skillId}
                    </span>
                  ))}
                </div>
              </section>
            ) : null}
            {(step.runtimeSnapshot?.tools?.length ?? 0) > 0 ? (
              <section className="policy-box">
                <h4 className="timeline-section-label">Tools</h4>
                <ul className="event-list" role="list">
                  {step.runtimeSnapshot!.tools.map((tool) => (
                    <li key={tool.name}>
                      <strong>{tool.name}</strong>
                      <p>{tool.category}</p>
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}
            {(step.runtimeSnapshot?.mcpServers?.length ?? 0) > 0 ? (
              <section className="policy-box">
                <h4 className="timeline-section-label">MCP Servers</h4>
                <div className="tag-row">
                  {step.runtimeSnapshot!.mcpServers.map((server) => (
                    <span key={server} className="chip chip-static">{server}</span>
                  ))}
                </div>
              </section>
            ) : null}
            {(step.runtimeSnapshot?.hooks?.length ?? 0) > 0 ? (
              <section className="policy-box">
                <h4 className="timeline-section-label">Hooks</h4>
                <ul className="event-list" role="list">
                  {step.runtimeSnapshot!.hooks.map((hook, index) => (
                    <li key={`${hook.event}-${index}`}>
                      <strong>{hook.event}</strong>
                      <p>{hook.type} · {hook.decision}</p>
                    </li>
                  ))}
                </ul>
              </section>
            ) : null}
            <SandboxExecutionDetails
              sandboxExecution={step.runtimeSnapshot?.sandboxExecution}
              tokenUsage={step.runtimeSnapshot?.tokenUsage}
              sectionClassName="policy-box"
              headingClassName=""
            />
          </>
        ) : null}

        {current === 'events' ? (
          <>
            <details className="policy-box adp-collapsible" aria-label={`${step.name} step events`}>
              <summary className="adp-collapsible-summary">
                <h4 className="timeline-section-label">Step Events</h4>
                <span className="adp-collapsible-meta">{stepEvents.length} event(s)</span>
              </summary>
              <ul className="event-list" role="list">
                {stepEvents.map((event) => (
                  <li key={event.id}>
                    <strong>{event.type.replaceAll('_', ' ')}</strong>
                    <p>{event.message}</p>
                    <span className="cell-meta">{new Date(event.createdAt).toLocaleString()}</span>
                  </li>
                ))}
              </ul>
            </details>
          </>
        ) : null}
      </div>

      <div className="step-footer-metrics">
        {cumulative.inputTokens + cumulative.outputTokens > 0 ? (
          <span>Σ {formatTokenCount(cumulative.inputTokens + cumulative.outputTokens)} tokens</span>
        ) : null}
        {toolCallCount > 0 ? <span>{toolCallCount} tool call{toolCallCount === 1 ? '' : 's'}</span> : null}
        {interactionCount > 0 ? (
          <span>{interactionCount} message{interactionCount === 1 ? '' : 's'}</span>
        ) : null}
      </div>
    </div>
  );
}

export function StepTimeline({
  steps,
  events = [],
  expandedStepId,
  onToggleStep,
  interactionCountByStep,
  reasoningByStep,
  resolveAgentIdentity,
}: StepTimelineProps) {
  const cumulativeByStep = cumulativeTokensByStep(steps);

  const detailFor = (step: RunStep) => {
    const agentName = step.agentName ?? step.runtimeSnapshot?.agentName;
    const reasoningItems = mergeVisibleReasoningEntries(
      reasoningByStep?.[step.id] ?? [],
      (step.runtimeSnapshot?.modelTraces ?? [])
        .filter((trace) => Boolean(trace.reasoningSummary?.trim()))
        .map((trace, index) => ({
          id: `trace-${step.id}-${index}`,
          kind: 'recorded' as const,
          summary: trace.reasoningSummary!.trim(),
          createdAt: trace.completedAt ?? trace.startedAt,
        })),
    );
    const stepEvents = getStepEventsForDisplay(step, events);
    return (
      <StepDetail
        step={step}
        stepEvents={stepEvents}
        agentName={agentName}
        agentIdentity={agentName ? resolveAgentIdentity?.(agentName) : undefined}
        tokenUsage={step.runtimeSnapshot?.tokenUsage}
        cumulative={cumulativeByStep[step.id]}
        reasoningItems={reasoningItems}
        interactionCount={interactionCountByStep?.[step.id] ?? 0}
      />
    );
  };

  // Focus the current progress: finished steps become a quiet, collapsed history; only the
  // active steps (running, awaiting, failed, pending) stay expanded on the timeline (#run-detail-ux).
  const finished = steps.filter((s) => s.status === 'completed');
  const active = steps.filter((s) => s.status !== 'completed');
  const isOpen = (step: RunStep) =>
    expandedStepId === step.id || (expandedStepId == null && step.status === 'running');

  const finishedAgents = [
    ...new Set(finished.map((s) => s.agentName ?? s.runtimeSnapshot?.agentName).filter(Boolean)),
  ];
  const finishedTokens = finished.length
    ? cumulativeByStep[finished[finished.length - 1].id]
    : { inputTokens: 0, outputTokens: 0 };

  return (
    <section className="panel timeline-panel" aria-label="Execution timeline">
      <h2>Execution Timeline</h2>

      {finished.length > 0 ? (
        <details className="timeline-completed-group">
          <summary className="timeline-completed-summary">
            <span className="timeline-completed-title">Completed</span>
            <span className="chip chip-static timeline-completed-count">{finished.length} steps</span>
            <span className="timeline-completed-meta">
              {finishedAgents.join(', ')}
              {finishedTokens.inputTokens + finishedTokens.outputTokens > 0
                ? ` · Σ ${formatTokenCount(finishedTokens.inputTokens + finishedTokens.outputTokens)} tokens`
                : ''}
            </span>
          </summary>
          <ol className="timeline timeline-completed-list" role="list">
            {finished.map((step) => {
              const agentName = step.agentName ?? step.runtimeSnapshot?.agentName;
              const open = isOpen(step);
              return (
                <li key={step.id} className="timeline-item">
                  <button
                    type="button"
                    className="timeline-trigger timeline-trigger-compact"
                    aria-expanded={open}
                    onClick={() => onToggleStep(step.id)}
                  >
                    <span className={`timeline-dot ${stepStateClass(step.status)}`} aria-hidden="true" />
                    <span className="timeline-main">
                      <strong>{step.name}</strong>
                    </span>
                    {agentName ? (
                      <AgentIdentityBadge
                        name={agentName}
                        identity={resolveAgentIdentity?.(agentName)}
                        className="timeline-agent-badge"
                      />
                    ) : null}
                    <span className="timeline-check" aria-hidden="true">✓</span>
                  </button>
                  {open ? detailFor(step) : null}
                </li>
              );
            })}
          </ol>
        </details>
      ) : null}

      <ol className="timeline" role="list">
        {active.map((step) => {
          const open = isOpen(step);
          const agentName = step.agentName ?? step.runtimeSnapshot?.agentName;
          const agentIdentity = agentName ? resolveAgentIdentity?.(agentName) : undefined;
          const isRunning = step.status === 'running';
          return (
            <li key={step.id} className={`timeline-item ${isRunning ? 'timeline-item-active' : ''}`}>
              <button
                type="button"
                className="timeline-trigger"
                aria-expanded={open}
                onClick={() => onToggleStep(step.id)}
              >
                <span className={`timeline-dot ${stepStateClass(step.status)}`} aria-hidden="true" />
                <span className="timeline-main">
                  <strong>{step.name}</strong>
                  <span>{step.status.replace('_', ' ')}</span>
                </span>
                {agentName ? (
                  <AgentIdentityBadge
                    name={agentName}
                    identity={agentIdentity}
                    isRunning={isRunning}
                    className="timeline-agent-badge"
                  />
                ) : null}
                {isRunning ? (
                  <span className="chip chip-static timeline-live-badge">
                    <span className="timeline-live-pulse" aria-hidden="true" /> thinking
                  </span>
                ) : null}
              </button>
              {open ? detailFor(step) : null}
            </li>
          );
        })}
      </ol>
    </section>
  );
}
