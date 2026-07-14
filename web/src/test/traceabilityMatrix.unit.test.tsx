import { render, screen, within } from '@testing-library/react';
import { TraceabilityMatrix } from '../components/TraceabilityMatrix';
import { AGENTWERKE_NS_PREFIX, AGENTWERKE_NS_URI } from '../bpmn/constants';
import type { RunStep } from '../types';

const WORKFLOW_XML = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions
    xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
    xmlns:${AGENTWERKE_NS_PREFIX}="${AGENTWERKE_NS_URI}" id="defs">
  <bpmn:process id="P">
    <bpmn:serviceTask id="DraftRequirements" name="Requirements Analysis">
      <bpmn:extensionElements>
        <agentwerke:agentTask agent="analyst" action="draft">
          <agentwerke:metadata key="phase" value="requirements" />
          <agentwerke:metadata key="traceability.produces" value="requirements_baseline" />
        </agentwerke:agentTask>
      </bpmn:extensionElements>
    </bpmn:serviceTask>
    <bpmn:serviceTask id="UnitGate" name="Component Traceability Gate">
      <bpmn:extensionElements>
        <agentwerke:agentTask agent="tester" action="gate" requiresEvidence="requirements_baseline,unit_test_results">
          <agentwerke:metadata key="phase" value="component_traceability" />
        </agentwerke:agentTask>
      </bpmn:extensionElements>
    </bpmn:serviceTask>
  </bpmn:process>
</bpmn:definitions>`;

const step = (name: string, status: RunStep['status']): RunStep => ({
  id: name,
  name,
  type: 'serviceTask',
  status,
});

describe('TraceabilityMatrix', () => {
  it('lists governed nodes with phase and joins step status by name', () => {
    render(
      <TraceabilityMatrix
        workflowXml={WORKFLOW_XML}
        steps={[step('Requirements Analysis', 'completed'), step('Component Traceability Gate', 'running')]}
      />,
    );

    const table = screen.getByRole('table');
    const rows = within(table).getAllByRole('row').slice(1); // drop header
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByText('Requirements Analysis')).toBeInTheDocument();
    expect(within(rows[0]).getByText('Completed')).toBeInTheDocument();
    expect(within(rows[1]).getByText('Running')).toBeInTheDocument();
    expect(within(rows[0]).getByText('requirements')).toBeInTheDocument();
  });

  it('marks required evidence satisfied once its producing step completes', () => {
    render(
      <TraceabilityMatrix
        workflowXml={WORKFLOW_XML}
        steps={[step('Requirements Analysis', 'completed'), step('Component Traceability Gate', 'running')]}
      />,
    );

    const gateRow = within(screen.getByRole('table')).getAllByRole('row')[2];
    // requirements_baseline was produced upstream (Requirements Analysis completed) → satisfied.
    expect(within(gateRow).getByText(/✓ requirements_baseline/)).toBeInTheDocument();
    // unit_test_results has no producing step → still pending.
    expect(within(gateRow).getByText(/○ unit_test_results/)).toBeInTheDocument();
    // count reflects 1 of 2 satisfied.
    expect(within(gateRow).getByText('1/2')).toBeInTheDocument();
  });

  it('summarizes evidence coverage from completed producers', () => {
    render(
      <TraceabilityMatrix
        workflowXml={WORKFLOW_XML}
        steps={[step('Requirements Analysis', 'completed')]}
      />,
    );
    // 2 distinct evidence keys (requirements_baseline, unit_test_results); 1 produced → 50%.
    const summary = screen.getByText(
      (_, el) => el?.classList.contains('traceability-summary') ?? false,
    );
    expect(summary.textContent).toMatch(/1\s*of\s*2\s*evidence artifacts produced \(50%\)/);
  });

  it('shows a hint when the workflow has no traceability metadata', () => {
    render(
      <TraceabilityMatrix
        workflowXml={'<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"><bpmn:process id="P"><bpmn:serviceTask id="T" name="T" /></bpmn:process></bpmn:definitions>'}
        steps={[]}
      />,
    );
    expect(screen.getByText(/no/i)).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });
});
