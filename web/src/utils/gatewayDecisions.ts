import type { RunEvent } from '../types';

export interface GatewayCondition {
  flowId: string;
  targetRef: string;
  expression: string;
  result: boolean;
  detail?: string | null;
}

export interface GatewayDecision {
  eventId: string;
  gatewayId: string;
  chosenFlowId: string;
  chosenTargetId: string;
  usedDefaultFlow: boolean;
  conditions: GatewayCondition[];
  createdAt: string;
}

/** Parses gateway_evaluated events; legacy payloads without a decision are skipped. */
export function parseGatewayDecisions(events: RunEvent[]): GatewayDecision[] {
  const decisions: GatewayDecision[] = [];
  for (const event of events) {
    if (event.type !== 'gateway_evaluated') continue;
    try {
      const payload = JSON.parse(event.message);
      if (!payload?.chosenFlowId) continue;
      decisions.push({
        eventId: event.id,
        gatewayId: payload.gatewayId ?? 'gateway',
        chosenFlowId: payload.chosenFlowId,
        chosenTargetId: payload.chosenTargetId ?? '',
        usedDefaultFlow: Boolean(payload.usedDefaultFlow),
        conditions: Array.isArray(payload.conditions) ? payload.conditions : [],
        createdAt: event.createdAt,
      });
    } catch {
      // Unparseable event payloads are shown raw in the Runtime Events monitor instead.
    }
  }
  return decisions;
}
