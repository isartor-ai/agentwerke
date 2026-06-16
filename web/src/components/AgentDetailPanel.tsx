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

  // Move focus into panel when it opens so keyboard users can navigate.
  // Intentionally keyed on step identity only (focus on open / step change).
  useEffect(() => {
    if (step) panelRef.current?.focus();
    // eslint-disable-next-line react-hooks/exhaustive-deps
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

            {/* ── Prompt ── */}
            {step.runtimeSnapshot?.promptInline && (
              <section className="adp-section">
                <h3 className="adp-section-label">Prompt</h3>
                <pre className="adp-pre">{step.runtimeSnapshot.promptInline}</pre>
              </section>
            )}

            {/* ── Skills ── */}
            {(step.runtimeSnapshot?.skills?.length ?? 0) > 0 && (
              <section className="adp-section">
                <h3 className="adp-section-label">Skills</h3>
                <ul className="adp-list" role="list">
                  {step.runtimeSnapshot!.skills.map((s) => (
                    <li key={s.skillId} className="adp-list-item">
                      <span className={`chip chip-static${s.selected ? ' chip-active' : ''}`}>
                        {s.selected ? '✓ ' : ''}{s.name ?? s.skillId}
                      </span>
                      {s.fingerprint && (
                        <span className="cell-meta adp-mono">{s.fingerprint.slice(0, 12)}…</span>
                      )}
                    </li>
                  ))}
                </ul>
              </section>
            )}

            {/* ── Tools ── */}
            {(step.runtimeSnapshot?.tools?.length ?? 0) > 0 && (
              <section className="adp-section">
                <h3 className="adp-section-label">Tools</h3>
                <ul className="adp-list" role="list">
                  {step.runtimeSnapshot!.tools.map((t) => (
                    <li key={t.name} className="adp-list-item">
                      <span className="chip chip-static">{t.name}</span>
                      <span className="cell-meta">{t.category}</span>
                    </li>
                  ))}
                </ul>
              </section>
            )}

            {/* ── MCP Servers ── */}
            {(step.runtimeSnapshot?.mcpServers?.length ?? 0) > 0 && (
              <section className="adp-section">
                <h3 className="adp-section-label">MCP Servers</h3>
                <div className="tag-row">
                  {step.runtimeSnapshot!.mcpServers.map((m) => (
                    <span key={m} className="chip chip-static">{m}</span>
                  ))}
                </div>
              </section>
            )}

            {/* ── Hooks ── */}
            {(step.runtimeSnapshot?.hooks?.length ?? 0) > 0 && (
              <section className="adp-section">
                <h3 className="adp-section-label">Hooks</h3>
                <ul className="adp-list" role="list">
                  {step.runtimeSnapshot!.hooks.map((h, i) => (
                    <li key={i} className="adp-list-item">
                      <span className="chip chip-static">{h.event}</span>
                      <span className="cell-meta">{h.type} · {h.decision}</span>
                      {h.durationMs !== undefined && (
                        <span className="cell-meta">{h.durationMs} ms</span>
                      )}
                    </li>
                  ))}
                </ul>
              </section>
            )}

            {/* ── Permissions ── */}
            {step.runtimeSnapshot && (
              <section className="adp-section">
                <h3 className="adp-section-label">Permissions</h3>
                <dl className="definition-list">
                  <div>
                    <dt>Level</dt>
                    <dd><span className="chip chip-static">{step.runtimeSnapshot.permissionLevel}</span></dd>
                  </div>
                  {step.runtimeSnapshot.permissionDecision && (
                    <>
                      <div>
                        <dt>Decision</dt>
                        <dd>{step.runtimeSnapshot.permissionDecision.allowed ? 'Allowed' : 'Denied'}</dd>
                      </div>
                      {step.runtimeSnapshot.permissionDecision.rationale && (
                        <div>
                          <dt>Rationale</dt>
                          <dd>{step.runtimeSnapshot.permissionDecision.rationale}</dd>
                        </div>
                      )}
                    </>
                  )}
                </dl>
                {step.runtimeSnapshot.allowedTools.length > 0 && (
                  <div className="tag-row adp-constraints">
                    {step.runtimeSnapshot.allowedTools.map((t) => (
                      <span key={t} className="chip chip-static">{t}</span>
                    ))}
                  </div>
                )}
              </section>
            )}

            {/* ── Sub-agents ── */}
            {step.runtimeSnapshot?.subAgentsEnabled && (
              <section className="adp-section">
                <h3 className="adp-section-label">Sub-agents</h3>
                <p className="cell-meta">Sub-agent delegation enabled for this step.</p>
              </section>
            )}

            {/* ── Step artifacts ── */}
            {(step.runtimeSnapshot?.stepArtifacts?.length ?? 0) > 0 && (
              <section className="adp-section">
                <h3 className="adp-section-label">Artifacts</h3>
                <ul className="adp-list" role="list">
                  {step.runtimeSnapshot!.stepArtifacts.map((a) => (
                    <li key={a.name} className="adp-list-item">
                      <span className="chip chip-static">{a.name}</span>
                      {a.contentType && <span className="cell-meta">{a.contentType}</span>}
                      {a.uri && (
                        <a className="btn btn-secondary adp-artifact-link" href={a.uri}>
                          Download
                        </a>
                      )}
                    </li>
                  ))}
                </ul>
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
