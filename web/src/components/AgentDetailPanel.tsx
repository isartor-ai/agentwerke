import { useEffect, useRef } from 'react';
import type { RunStep, RunEvent } from '../types';
import { RiskBadge } from './RiskBadge';
import { StatusBadge } from './StatusBadge';

export interface AgentDetailPanelProps {
  step: RunStep | null;
  events: RunEvent[];
  onClose: () => void;
}

function formatTs(iso: string): string {
  return new Date(iso).toLocaleString();
}

export function AgentDetailPanel({ step, events, onClose }: AgentDetailPanelProps) {
  const panelRef = useRef<HTMLElement>(null);

  // Close on Escape
  useEffect(() => {
    if (!step) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [step, onClose]);

  // Move focus into panel when it opens so keyboard users can navigate
  useEffect(() => {
    if (step) panelRef.current?.focus();
  }, [step?.id]);

  const stepEvents = step
    ? [...events].filter((e) => e.message.includes(step.name)).reverse()
    : [];

  const duration =
    step?.durationMs ??
    (step?.startedAt && step?.completedAt
      ? new Date(step.completedAt).getTime() - new Date(step.startedAt).getTime()
      : null);

  return (
    <div className={`adp-wrap${step ? ' adp-open' : ''}`} aria-live="polite">
      <div className="adp-inner">
        {step && (
          <article
            className="panel adp"
            ref={panelRef}
            tabIndex={-1}
            role="region"
            aria-label={`Step detail: ${step.name}`}
          >
            {/* ── Header ── */}
            <div className="adp-header">
              <div className="adp-title">
                <strong>{step.name}</strong>
                <StatusBadge status={step.status} />
              </div>
              <button
                type="button"
                className="btn-icon adp-close"
                aria-label="Close step detail"
                onClick={onClose}
              >
                ×
              </button>
            </div>

            {/* ── Agent ── */}
            {step.agentName && (
              <section className="adp-section">
                <h3 className="adp-section-label">Agent</h3>
                <dl className="definition-list">
                  <div><dt>Name</dt><dd>{step.agentName}</dd></div>
                  <div><dt>Step type</dt><dd>{step.type}</dd></div>
                </dl>
              </section>
            )}

            {/* ── Output ── */}
            <section className="adp-section">
              <h3 className="adp-section-label">Output</h3>
              {step.output ? (
                <pre className="adp-pre">{step.output}</pre>
              ) : (
                <p className="adp-empty">No output captured for this step.</p>
              )}
            </section>

            {/* ── Error (conditional) ── */}
            {step.error && (
              <section className="adp-section">
                <h3 className="adp-section-label">Error</h3>
                <pre className="adp-pre adp-pre-error">{step.error}</pre>
              </section>
            )}

            {/* ── Policy decision (conditional) ── */}
            {step.policyDecision && (
              <section className="adp-section">
                <h3 className="adp-section-label">Policy Decision</h3>
                <dl className="definition-list">
                  <div>
                    <dt>Decision</dt>
                    <dd>
                      <span className="chip chip-static">
                        {step.policyDecision.kind.replaceAll('_', ' ')}
                      </span>
                    </dd>
                  </div>
                  <div><dt>Policy</dt><dd>{step.policyDecision.policyName}</dd></div>
                  <div>
                    <dt>Risk</dt>
                    <dd>
                      <RiskBadge
                        level={step.policyDecision.riskLevel}
                        score={step.policyDecision.riskScore}
                      />
                    </dd>
                  </div>
                  <div><dt>Rationale</dt><dd>{step.policyDecision.rationale}</dd></div>
                </dl>
                {step.policyDecision.constraints?.length ? (
                  <div className="tag-row adp-constraints">
                    {step.policyDecision.constraints.map((c) => (
                      <span key={c} className="chip chip-static">{c}</span>
                    ))}
                  </div>
                ) : null}
              </section>
            )}

            {/* ── Timing ── */}
            {(step.startedAt ?? step.completedAt ?? duration !== null) && (
              <section className="adp-section">
                <h3 className="adp-section-label">Timing</h3>
                <dl className="definition-list">
                  {step.startedAt && (
                    <div><dt>Started</dt><dd>{formatTs(step.startedAt)}</dd></div>
                  )}
                  {step.completedAt && (
                    <div><dt>Completed</dt><dd>{formatTs(step.completedAt)}</dd></div>
                  )}
                  {duration !== null && (
                    <div><dt>Duration</dt><dd>{duration.toLocaleString()} ms</dd></div>
                  )}
                </dl>
              </section>
            )}

            {/* ── Step events (conditional) ── */}
            {stepEvents.length > 0 && (
              <section className="adp-section">
                <h3 className="adp-section-label">Step Events</h3>
                <ul className="event-list" role="list">
                  {stepEvents.map((event) => (
                    <li key={event.id}>
                      <strong>{event.type.replaceAll('_', ' ')}</strong>
                      <p>{event.message}</p>
                      <span className="cell-meta">{formatTs(event.createdAt)}</span>
                    </li>
                  ))}
                </ul>
              </section>
            )}
          </article>
        )}
      </div>
    </div>
  );
}
