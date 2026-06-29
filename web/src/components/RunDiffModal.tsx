import type { RunEvent, RunStatus, WorkflowRun } from '../types';

export interface RunDiffModalProps {
  run: WorkflowRun;
  /** BPMN XML of the workflow definition (for extracting expected tasks) */
  workflowXml?: string;
  onClose: () => void;
}

interface StepTrace {
  name: string;
  type: string;
  definitionStatus: 'defined' | 'unplanned';
  executionStatus?: RunStatus;
  durationMs?: number;
  error?: string;
  retries: number;
  timeouts: number;
  events: RunEvent[];
}

/** Parse task names from BPMN XML without pulling in bpmn-js. */
function extractDefinitionTasks(bpmnXml: string): string[] {
  try {
    const parser = new DOMParser();
    const doc = parser.parseFromString(bpmnXml, 'application/xml');
    const tags = ['serviceTask', 'userTask', 'scriptTask', 'manualTask', 'businessRuleTask', 'task'];
    return tags.flatMap((tag) =>
      Array.from(doc.getElementsByTagNameNS('*', tag))
        .map((el) => el.getAttribute('name') ?? '')
        .filter(Boolean),
    );
  } catch {
    return [];
  }
}

function buildTrace(run: WorkflowRun, definitionTaskNames: string[]): StepTrace[] {
  const steps = run.steps ?? [];
  const events = run.events ?? [];

  // Group events by approximate step name match
  const eventsByStep: Record<string, RunEvent[]> = {};
  for (const event of events) {
    let matched = false;
    for (const step of steps) {
      if (event.message.toLowerCase().includes(step.name.toLowerCase())) {
        (eventsByStep[step.name] ??= []).push(event);
        matched = true;
      }
    }
    if (!matched) {
      (eventsByStep['__global__'] ??= []).push(event);
    }
  }

  const definedSet = new Set(definitionTaskNames);
  const traces: StepTrace[] = [];

  // Executed steps (annotate whether they were in the definition)
  for (const step of steps) {
    const stepEvents = eventsByStep[step.name] ?? [];
    const retries = stepEvents.filter((e) => e.type === 'retry_scheduled').length;
    const timeouts = stepEvents.filter((e) => e.type === 'timeout_triggered').length;
    traces.push({
      name: step.name,
      type: step.type,
      definitionStatus: definedSet.has(step.name) ? 'defined' : 'unplanned',
      executionStatus: step.status,
      durationMs: step.durationMs,
      error: step.error,
      retries,
      timeouts,
      events: stepEvents,
    });
  }

  // Definition tasks not in the execution (not-yet-reached or skipped)
  for (const name of definitionTaskNames) {
    if (!traces.some((t) => t.name === name)) {
      traces.push({ name, type: 'task', definitionStatus: 'defined', retries: 0, timeouts: 0, events: [] });
    }
  }

  return traces;
}

function statusClass(status?: RunStatus): string {
  switch (status) {
    case 'completed': return 'diff-status-completed';
    case 'running': return 'diff-status-running';
    case 'awaiting_approval': return 'diff-status-awaiting';
    case 'needs_config': return 'diff-status-awaiting';
    case 'failed':
    case 'blocked': return 'diff-status-failed';
    case 'cancelled': return 'diff-status-cancelled';
    default: return 'diff-status-pending';
  }
}

function StatusChip({ status }: { status?: RunStatus }) {
  const label = status?.replace(/_/g, ' ') ?? 'not reached';
  return <span className={`chip chip-static ${statusClass(status)}`}>{label}</span>;
}

function StepRow({ trace }: { trace: StepTrace }) {
  const hasDeviation = trace.retries > 0 || trace.timeouts > 0 || trace.error || trace.definitionStatus === 'unplanned';
  return (
    <tr className={hasDeviation ? 'diff-row-deviation' : ''}>
      <td>
        <span className="diff-step-name">{trace.name}</span>
        {trace.definitionStatus === 'unplanned' && (
          <span className="chip chip-static diff-badge-unplanned">unplanned</span>
        )}
      </td>
      <td><StatusChip status={trace.executionStatus} /></td>
      <td className="cell-meta">
        {trace.durationMs != null ? `${(trace.durationMs / 1000).toFixed(1)}s` : '—'}
      </td>
      <td>
        {trace.retries > 0 && (
          <span className="chip chip-static diff-badge-retry">+{trace.retries} retr{trace.retries === 1 ? 'y' : 'ies'}</span>
        )}
        {trace.timeouts > 0 && (
          <span className="chip chip-static diff-badge-timeout">+{trace.timeouts} timeout{trace.timeouts > 1 ? 's' : ''}</span>
        )}
        {trace.error && (
          <span className="diff-error-hint" title={trace.error}>error</span>
        )}
      </td>
    </tr>
  );
}

export function RunDiffModal({ run, workflowXml, onClose }: RunDiffModalProps) {
  const definitionTaskNames = workflowXml ? extractDefinitionTasks(workflowXml) : [];
  const traces = buildTrace(run, definitionTaskNames);

  const deviations = traces.filter(
    (t) => t.retries > 0 || t.timeouts > 0 || t.error || t.definitionStatus === 'unplanned' ||
      (t.executionStatus && t.executionStatus !== 'completed' && t.executionStatus !== 'running' && t.executionStatus !== 'pending'),
  );

  const onBackdrop = (e: React.MouseEvent<HTMLDivElement>) => {
    if (e.target === e.currentTarget) onClose();
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (e.key === 'Escape') onClose();
  };

  return (
    <div
      className="diff-modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-label="Execution diff"
      onClick={onBackdrop}
      onKeyDown={onKeyDown}
    >
      <div className="diff-modal">
        <div className="diff-modal-header">
          <h2>Execution Diff — {run.workflowName}</h2>
          <button type="button" className="btn-icon" aria-label="Close diff" onClick={onClose}>
            ×
          </button>
        </div>

        {deviations.length > 0 ? (
          <p className="diff-summary">
            {deviations.length} deviation{deviations.length > 1 ? 's' : ''} from the expected flow.
          </p>
        ) : (
          <p className="diff-summary diff-summary-clean">Execution matched the definition with no deviations.</p>
        )}

        <div className="diff-modal-body">
          <table className="diff-table">
            <thead>
              <tr>
                <th>Step</th>
                <th>Status</th>
                <th>Duration</th>
                <th>Deviations</th>
              </tr>
            </thead>
            <tbody>
              {traces.map((trace, i) => (
                <StepRow key={`${trace.name}-${i}`} trace={trace} />
              ))}
            </tbody>
          </table>

          {(run.events ?? []).length > 0 && (
            <section className="diff-events-section">
              <h3>All Run Events</h3>
              <ul className="event-list" role="list">
                {(run.events ?? []).map((event) => (
                  <li key={event.id}>
                    <strong>{event.type.replaceAll('_', ' ')}</strong>
                    <p>{event.message}</p>
                    <span className="cell-meta">{new Date(event.createdAt).toLocaleString()}</span>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </div>

        <div className="diff-modal-footer">
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
