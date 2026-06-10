import type { RunStep } from '../types';

interface BpmnRunGraphProps {
  steps: RunStep[];
}

function statusClass(status: RunStep['status']): string {
  switch (status) {
    case 'completed':
      return 'graph-node-completed';
    case 'running':
      return 'graph-node-running';
    case 'awaiting_approval':
      return 'graph-node-awaiting';
    case 'failed':
    case 'blocked':
      return 'graph-node-failed';
    default:
      return 'graph-node-pending';
  }
}

export function BpmnRunGraph({ steps }: BpmnRunGraphProps) {
  if (steps.length === 0) {
    return (
      <section className="panel graph-panel" aria-label="BPMN workflow graph">
        <h2>BPMN Graph</h2>
        <p>No BPMN nodes available for this run.</p>
      </section>
    );
  }

  return (
    <section className="panel graph-panel" aria-label="BPMN workflow graph">
      <h2>BPMN Graph</h2>
      <ol className="graph-lane" role="list">
        {steps.map((step, index) => (
          <li key={step.id} className="graph-item">
            <div className={`graph-node ${statusClass(step.status)}`}>
              <strong>{step.name}</strong>
              <span>{step.type.replace('_', ' ')}</span>
              <span className="graph-node-status">{step.status.replace('_', ' ')}</span>
            </div>
            {index < steps.length - 1 ? <span className="graph-arrow" aria-hidden="true">→</span> : null}
          </li>
        ))}
      </ol>
    </section>
  );
}