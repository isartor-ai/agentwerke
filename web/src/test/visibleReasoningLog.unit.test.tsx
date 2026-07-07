import { render, screen } from '@testing-library/react';
import { VisibleReasoningLog } from '../components/VisibleReasoningLog';
import type { VisibleReasoningEntry } from '../utils/visibleReasoning';

describe('VisibleReasoningLog — activity detail', () => {
  it('renders the concrete action detail for tool entries', () => {
    const entries: VisibleReasoningEntry[] = [
      {
        id: 'r1',
        kind: 'reasoning',
        summary: 'Deciding on the minimal change to add a toggle.',
      },
      {
        id: 't1',
        kind: 'tool_started',
        summary: "Calling tool 'sandbox.file_write'.",
        toolName: 'sandbox.file_write',
        status: 'started',
        detail: 'src/App.tsx (340 B)',
      },
      {
        id: 't2',
        kind: 'tool_finished',
        summary: "Tool 'sandbox.shell' completed.",
        toolName: 'sandbox.shell',
        status: 'completed',
        detail: 'exit 0',
      },
    ];

    render(<VisibleReasoningLog entries={entries} />);

    // Reasoning text still renders.
    expect(screen.getByText('Deciding on the minimal change to add a toggle.')).toBeInTheDocument();
    // Concrete action detail is shown for tool activity.
    expect(screen.getByText('src/App.tsx (340 B)')).toBeInTheDocument();
    expect(screen.getByText('exit 0')).toBeInTheDocument();
  });
});
