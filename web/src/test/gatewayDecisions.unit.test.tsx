import { render, screen } from '@testing-library/react';
import { GatewayDecisionList } from '../components/GatewayDecisionList';
import { parseGatewayDecisions } from '../utils/gatewayDecisions';
import type { RunEvent } from '../types';

const gatewayEvent = (overrides?: Partial<RunEvent>): RunEvent => ({
  id: 'evt-1',
  type: 'gateway_evaluated',
  message: JSON.stringify({
    runId: 'run-1',
    gatewayId: 'TestsPass',
    gatewayType: 'exclusive',
    chosenFlowId: 'FlowFail',
    chosenTargetId: 'Fix',
    usedDefaultFlow: true,
    conditions: [
      {
        flowId: 'FlowPass',
        targetRef: 'End',
        expression: '{{output.RunTests}} contains "VERDICT: PASS"',
        result: false,
        detail: '"VERDICT: FAIL" contains "VERDICT: PASS" -> false',
      },
    ],
  }),
  createdAt: '2026-07-10T12:00:00Z',
  ...overrides,
});

describe('parseGatewayDecisions', () => {
  it('extracts decisions from gateway_evaluated events', () => {
    const decisions = parseGatewayDecisions([gatewayEvent()]);

    expect(decisions).toHaveLength(1);
    expect(decisions[0].gatewayId).toBe('TestsPass');
    expect(decisions[0].chosenTargetId).toBe('Fix');
    expect(decisions[0].usedDefaultFlow).toBe(true);
    expect(decisions[0].conditions).toHaveLength(1);
    expect(decisions[0].conditions[0].result).toBe(false);
  });

  it('skips legacy payloads without a decision and other event types', () => {
    const legacy = gatewayEvent({
      id: 'evt-legacy',
      message: JSON.stringify({ runId: 'run-1', gatewayId: 'OldGw', gatewayType: 'exclusive' }),
    });
    const unrelated = gatewayEvent({ id: 'evt-other', type: 'node_entered' });
    const malformed = gatewayEvent({ id: 'evt-broken', message: 'not-json' });

    expect(parseGatewayDecisions([legacy, unrelated, malformed])).toHaveLength(0);
  });
});

describe('GatewayDecisionList', () => {
  it('renders the chosen branch and each evaluated condition', () => {
    render(<GatewayDecisionList events={[gatewayEvent()]} />);

    expect(screen.getByText('Gateway Decisions')).toBeInTheDocument();
    expect(screen.getByText('TestsPass')).toBeInTheDocument();
    expect(screen.getByText(/→ Fix/)).toBeInTheDocument();
    expect(screen.getByText(/default branch/)).toBeInTheDocument();
    expect(screen.getByText('{{output.RunTests}} contains "VERDICT: PASS"')).toBeInTheDocument();
  });

  it('renders nothing when a run has no gateway decisions', () => {
    const { container } = render(<GatewayDecisionList events={[]} />);
    expect(container).toBeEmptyDOMElement();
  });
});
