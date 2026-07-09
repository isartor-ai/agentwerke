import { extractAgentReasoningByStep } from '../utils/visibleReasoning';
import type { RunEvent } from '../types';

function toolEvent(id: string, type: string, detail: string, extra: Record<string, unknown> = {}): RunEvent {
  return {
    id,
    type,
    createdAt: new Date(2026, 0, 1, 0, 0, Number(id.replace(/\D/g, '')) || 0).toISOString(),
    message: JSON.stringify({ stepId: 'step-1', summary: `Calling tool 'sandbox.file_write'.`, toolName: 'sandbox.file_write', status: 'started', detail, ...extra }),
  };
}

describe('extractAgentReasoningByStep — tool activity detail (#activity-log)', () => {
  it('carries the concrete action detail onto tool entries', () => {
    const byStep = extractAgentReasoningByStep([
      toolEvent('e1', 'agent_tool_call_started', 'src/App.tsx (340 B)'),
    ]);

    const entry = byStep['step-1'][0];
    expect(entry.kind).toBe('tool_started');
    expect(entry.detail).toBe('src/App.tsx (340 B)');
  });

  it('keeps distinct actions on the same tool as separate entries', () => {
    const byStep = extractAgentReasoningByStep([
      toolEvent('e1', 'agent_tool_call_started', 'src/App.tsx (340 B)'),
      toolEvent('e2', 'agent_tool_call_started', 'src/theme.css (120 B)'),
    ]);

    const details = byStep['step-1'].map((entry) => entry.detail);
    expect(details).toEqual(['src/App.tsx (340 B)', 'src/theme.css (120 B)']);
  });

  it('parses live sandbox log events as activity entries', () => {
    const byStep = extractAgentReasoningByStep([
      {
        id: 'e3',
        type: 'agent_sandbox_log',
        createdAt: new Date(2026, 0, 1, 0, 0, 3).toISOString(),
        message: JSON.stringify({
          stepId: 'step-1',
          summary: 'npm test -- --runInBand',
          status: 'stdout',
        }),
      },
    ]);

    const entry = byStep['step-1'][0];
    expect(entry.kind).toBe('sandbox_log');
    expect(entry.status).toBe('stdout');
    expect(entry.summary).toBe('npm test -- --runInBand');
  });
});

function reasoningEvent(id: string, type: string, summary: string): RunEvent {
  return {
    id,
    type,
    createdAt: new Date(2026, 0, 1, 0, 0, Number(id.replace(/\D/g, '')) || 0).toISOString(),
    message: JSON.stringify({ stepId: 'step-1', summary }),
  };
}

describe('extractAgentReasoningByStep — recorded summary vs streamed reasoning', () => {
  it('upgrades the streamed reasoning block instead of duplicating it as a final entry', () => {
    const byStep = extractAgentReasoningByStep([
      reasoningEvent('e1', 'agent_reasoning_delta', 'The design will use'),
      reasoningEvent('e2', 'agent_reasoning_delta', 'The design will use a JSON file for persistence.'),
      reasoningEvent('e3', 'agent_reasoning_recorded', 'The design will use a JSON file for persistence.'),
    ]);

    const entries = byStep['step-1'];
    expect(entries).toHaveLength(1);
    expect(entries[0].kind).toBe('recorded');
    expect(entries[0].summary).toBe('The design will use a JSON file for persistence.');
  });

  it('keeps a recorded entry that never streamed (mock / non-streaming path)', () => {
    const byStep = extractAgentReasoningByStep([
      reasoningEvent('e1', 'agent_reasoning_recorded', 'Chose the minimal patch.'),
    ]);

    expect(byStep['step-1']).toHaveLength(1);
    expect(byStep['step-1'][0].kind).toBe('recorded');
  });
});
