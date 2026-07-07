import type { RunStepModelTrace } from '../types';

export interface ModelActivityDetailsProps {
  modelTraces?: RunStepModelTrace[];
  sectionClassName?: string;
  headingClassName?: string;
}

function formatDuration(ms?: number | null): string {
  if (ms == null) return '-';
  if (ms < 1000) return `${Math.round(ms).toLocaleString()} ms`;
  return `${(ms / 1000).toFixed(1)} s`;
}

function formatTimestamp(value?: string | null): string {
  return value ? new Date(value).toLocaleString() : '-';
}

export function ModelActivityDetails({
  modelTraces,
  sectionClassName = 'adp-section',
  headingClassName = 'adp-section-label',
}: ModelActivityDetailsProps) {
  if (!modelTraces || modelTraces.length === 0) {
    return null;
  }

  return (
    <section className={sectionClassName}>
      <h3 className={headingClassName}>LLM Activity</h3>
      <div className="model-trace-list">
        {modelTraces.map((trace, index) => {
          const toolCalls = trace.toolCalls ?? [];
          return (
            <article
              className="model-trace"
              key={`${trace.startedAt}-${trace.modelId ?? 'model'}-${index}`}
              aria-label={`Model trace ${index + 1}`}
            >
              <dl className="definition-list">
                <div><dt>Status</dt><dd>{trace.status}</dd></div>
                <div><dt>Model</dt><dd>{trace.modelId ?? '-'}</dd></div>
                <div><dt>Started</dt><dd>{formatTimestamp(trace.startedAt)}</dd></div>
                <div><dt>Completed</dt><dd>{formatTimestamp(trace.completedAt)}</dd></div>
                <div><dt>Elapsed</dt><dd>{formatDuration(trace.elapsedMs)}</dd></div>
                <div>
                  <dt>Tokens</dt>
                  <dd>{(trace.inputTokens + trace.outputTokens).toLocaleString()}</dd>
                </div>
              </dl>

              <div className="model-trace-block">
                <h4>Inference Signals</h4>
                <div className="model-signal-grid">
                  <span>{trace.inputTokens.toLocaleString()} input</span>
                  <span>{trace.outputTokens.toLocaleString()} output</span>
                  <span>{toolCalls.length} tool call{toolCalls.length === 1 ? '' : 's'}</span>
                </div>
              </div>

              {trace.reasoningSummary ? (
                <div className="model-trace-block">
                  <h4>Visible Reasoning</h4>
                  <pre className="adp-pre">{trace.reasoningSummary}</pre>
                </div>
              ) : null}

              {toolCalls.length > 0 ? (
                <div className="model-trace-block">
                  <h4>Tool Calls</h4>
                  <ul className="adp-list" role="list">
                    {toolCalls.map((call) => (
                      <li key={call.id || call.name} className="adp-list-item model-tool-call">
                        <span className="chip chip-static">{call.name}</span>
                        {call.inputSummary ? <code>{call.inputSummary}</code> : null}
                      </li>
                    ))}
                  </ul>
                </div>
              ) : null}

              {trace.output ? (
                <div className="model-trace-block">
                  <h4>Visible Output</h4>
                  <pre className="adp-pre">{trace.output}</pre>
                </div>
              ) : null}

              {trace.failureReason ? (
                <div className="model-trace-block">
                  <h4>Error</h4>
                  <pre className="adp-pre adp-pre-error">{trace.failureReason}</pre>
                </div>
              ) : null}
            </article>
          );
        })}
      </div>
    </section>
  );
}
