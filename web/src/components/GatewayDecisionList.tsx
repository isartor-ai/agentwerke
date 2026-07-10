import type { RunEvent } from '../types';
import { parseGatewayDecisions } from '../utils/gatewayDecisions';

/**
 * Renders each exclusive-gateway routing decision of a run: which branch was
 * taken and how every evaluated condition resolved (#203).
 */
export function GatewayDecisionList({ events }: { events: RunEvent[] }) {
  const decisions = parseGatewayDecisions(events);
  if (decisions.length === 0) return null;

  return (
    <section className="panel" aria-label="Gateway decisions">
      <h2>Gateway Decisions</h2>
      <ul className="event-list" role="list">
        {decisions.map((decision) => (
          <li key={decision.eventId}>
            <strong>{decision.gatewayId}</strong>
            <span className="chip chip-static">
              → {decision.chosenTargetId || decision.chosenFlowId}
              {decision.usedDefaultFlow ? ' (default branch)' : ''}
            </span>
            {decision.conditions.map((condition) => (
              <p key={condition.flowId} className="cell-meta">
                {condition.result ? '✓' : '✗'} <code>{condition.expression}</code>
                {condition.detail ? <> — <code>{condition.detail}</code></> : null}
              </p>
            ))}
            <span className="cell-meta">{new Date(decision.createdAt).toLocaleString()}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}
