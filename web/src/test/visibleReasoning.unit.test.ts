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
