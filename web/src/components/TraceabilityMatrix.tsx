import { useMemo } from 'react';
import type { RunStatus, RunStep } from '../types';
import { StatusBadge } from './StatusBadge';
import {
  parseTraceability,
  collectEvidenceKeys,
  type TraceabilityNode,
} from '../bpmn/traceability';

interface TraceabilityMatrixProps {
  workflowXml: string;
  steps: RunStep[];
}

const EXTENSION_LABEL: Record<NonNullable<TraceabilityNode['extension']>, string> = {
  agentTask: 'Agent task',
  approvalTask: 'Approval gate',
  externalEvent: 'Test-result wait',
};

/**
 * Run-scoped traceability matrix (rec #1 of docs/evaluations/v-model-process-test.md): joins the
 * workflow's authored traceability metadata (phase, produces/consumes/verifies, requiresEvidence)
 * with this run's step statuses so requirements, design, tests, and approvals can be compared
 * side by side. Evidence is treated as "produced" once the node that produces it has completed.
 */
export function TraceabilityMatrix({ workflowXml, steps }: TraceabilityMatrixProps) {
  const nodes = useMemo(() => parseTraceability(workflowXml), [workflowXml]);

  const { statusOf, producedEvidence, coverage, evidenceKeys } = useMemo(() => {
    // Steps are keyed by their node name — the same join the run diagram (BpmnViewer) uses.
    const byName = new Map<string, RunStep>();
    for (const step of steps) {
      if (step.name) byName.set(step.name, step);
    }
    const statusOf = (node: TraceabilityNode): RunStatus | undefined =>
      byName.get(node.name)?.status;

    const producedEvidence = new Set<string>();
    for (const node of nodes) {
      if (statusOf(node) === 'completed') {
        for (const key of node.produces) producedEvidence.add(key);
      }
    }
    const evidenceKeys = collectEvidenceKeys(nodes);
    const coverage = evidenceKeys.length
      ? Math.round((producedEvidence.size / evidenceKeys.length) * 100)
      : 0;
    return { statusOf, producedEvidence, coverage, evidenceKeys };
  }, [nodes, steps]);

  if (nodes.length === 0) {
    return (
      <p className="cell-meta">
        This workflow has no <code>agentwerke:metadata</code> traceability keys, so there is nothing
        to trace. Add <code>phase</code> and <code>traceability.*</code> metadata to its tasks to
        populate this view.
      </p>
    );
  }

  return (
    <section className="traceability" aria-label="Traceability matrix">
      <p className="cell-meta traceability-summary">
        <strong>{producedEvidence.size}</strong> of <strong>{evidenceKeys.length}</strong> evidence
        artifacts produced ({coverage}%) across {nodes.length} governed steps.
      </p>

      <div className="table-wrap">
        <table className="traceability-matrix">
          <thead>
            <tr>
              <th scope="col">Phase</th>
              <th scope="col">Step</th>
              <th scope="col">Status</th>
              <th scope="col">Produces</th>
              <th scope="col">Verifies / consumes</th>
              <th scope="col">Requires evidence</th>
            </tr>
          </thead>
          <tbody>
            {nodes.map((node) => {
              const status = statusOf(node);
              const links = [...node.verifies, ...node.consumes];
              return (
                <tr key={node.id}>
                  <td>{node.phase ?? <span className="cell-meta">—</span>}</td>
                  <td>
                    <strong>{node.name}</strong>
                    <span className="cell-meta">
                      {node.extension ? EXTENSION_LABEL[node.extension] : node.elementType}
                    </span>
                  </td>
                  <td>
                    {status ? (
                      <StatusBadge status={status} />
                    ) : (
                      <span className="cell-meta">Not started</span>
                    )}
                  </td>
                  <td>{renderKeys(node.produces, (key) => producedEvidence.has(key))}</td>
                  <td>{renderKeys(links)}</td>
                  <td>{renderRequires(node.requiresEvidence, producedEvidence)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/** Renders evidence/link keys as chips; when `isMet` is given, met keys get the success tone. */
function renderKeys(keys: string[], isMet?: (key: string) => boolean) {
  if (keys.length === 0) return <span className="cell-meta">—</span>;
  return (
    <span className="traceability-keys">
      {keys.map((key) => (
        <span
          key={key}
          className={`chip chip-static${isMet && isMet(key) ? ' chip-success' : ''}`}
        >
          {key}
        </span>
      ))}
    </span>
  );
}

/** Requires-evidence cell: each key marked satisfied (produced upstream) or still pending. */
function renderRequires(keys: string[], produced: Set<string>) {
  if (keys.length === 0) return <span className="cell-meta">—</span>;
  const met = keys.filter((key) => produced.has(key)).length;
  return (
    <span className="traceability-keys">
      <span className="cell-meta traceability-require-count">
        {met}/{keys.length}
      </span>
      {keys.map((key) => {
        const satisfied = produced.has(key);
        return (
          <span
            key={key}
            className={`chip chip-static${satisfied ? ' chip-success' : ' chip-pending'}`}
            title={satisfied ? 'Produced upstream' : 'Not yet produced'}
          >
            {satisfied ? '✓ ' : '○ '}
            {key}
          </span>
        );
      })}
    </span>
  );
}
